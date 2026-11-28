// ImageTools.cs - part of the Voidcons mod: the compression system that turns ANY picture into a small icon.
//
// WHAT IT DOES
//   Reads a picture file (PNG, JPG, BMP, GIF, TIFF...), lets the move-and-crop editor pick any square part of it,
//   shrinks that part to 128 x 128, and writes it as a small, optimized PNG file (at most 256 colors). That PNG is
//   what Voidcons saves as icon.png and sends to other players, so it is kept as small as possible.
//
// HOW TO RUN / WHAT IT NEEDS
//   Compiled into Voidcons.dll by build.ps1. Needs Windows (it uses GDI+, Windows' built-in image library,
//   which every Windows PC has) and plain .NET 6. No Unity, so it can run on a background thread.
//
// HOW IT WORKS (three steps)
//   1. LoadPicture: GDI+ opens the file and hands us its pixels. GDI+ already knows JPG, GIF, BMP and more,
//      so we don't have to write a decoder for each format. Files GDI+ can't read (WebP, HEIC, AVIF...) go to
//      WicReader.cs, which uses Windows' newer image system.
//   2. Resample: takes any rectangle of the picture (it may stick out past the edges; that part becomes
//      transparent) and shrinks or grows it by AVERAGING: every new pixel is the average of all old pixels it
//      covers. That keeps it smooth instead of jagged. Colors are weighted by their transparency ("premultiplied
//      alpha"), so see-through pixels don't darken the edges. The crop editor and FitSquare both use it.
//   3. EncodePng: writes the pixels as a PNG, trying two ways and keeping the smaller file:
//      a palette PNG (the 256 colors that fit the picture best, each pixel one byte) and a full-color PNG (each row
//      gets the PNG "filter" that compresses best; no alpha channel when nothing is see-through).
//      zlib packs both at its smallest setting.

using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Voidcons
{
    internal static class ImageTools
    {
        // Every icon is made 128 x 128 and round, like the game's own backer icons (Brian makes those with a
        // circle crop tool). 128 (not 256) because icons travel over the network to every player: a 128 x 128
        // palette PNG is about 5-8 KB instead of 30-100 KB for a 256 x 256 full-color one (void's choice, 2026-11-28).
        public const int IconSize = 128;
        private const long MaxInputBytes = 30L * 1024 * 1024; // refuse picture files over 30 MB
        private const int MaxInputSide = 10000;             // and pictures wider or taller than 10000 pixels

        // The whole pipeline: picture file in, small PNG bytes out (or null and a reason in "error").
        // "info" describes what happened, for the status line in the panel.
        public static byte[] MakeIcon(string path, out string info, out string error)
        {
            info = null;
            byte[] rgba = LoadPicture(path, out int width, out int height, out error);
            if (rgba == null)
                return null;
            byte[] square = FitSquare(rgba, width, height, IconSize);
            byte[] png = EncodePng(square, IconSize, IconSize);
            info = $"{Path.GetFileName(path)} ({width}x{height}) -> {IconSize}x{IconSize} PNG, {png.Length / 1024.0:0.0} KB";
            return png;
        }

        // ================= 1. reading any picture with GDI+ =================
        // GDI+ is a C library inside Windows (gdiplus.dll). [DllImport] lets C# call its functions directly.
        // Every function returns a status number: 0 means OK. "out IntPtr" hands back a "handle": a number that
        // stands for an object living inside GDI+, which we must give back (dispose) when we're done.

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInput
        {
            public uint GdiplusVersion;          // must be 1
            public IntPtr DebugEventCallback;
            public int SuppressBackgroundThread;
            public int SuppressExternalCodecs;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GpRect
        {
            public int X, Y, Width, Height;
        }

        // Describes a block of pixels that GDI+ has "locked" for us to read. Scan0 is the memory address of the
        // first row; Stride is how many bytes one row takes (it can be more than width * 4).
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapData
        {
            public uint Width, Height;
            public int Stride, PixelFormat;
            public IntPtr Scan0;
            public UIntPtr Reserved;
        }

        private const int PixelFormat32bppARGB = 0x0026200A; // 4 bytes per pixel, in memory order Blue, Green, Red, Alpha
        private const uint ImageLockModeRead = 1;

        [DllImport("gdiplus.dll")] private static extern int GdiplusStartup(out IntPtr token, ref StartupInput input, IntPtr output);
        [DllImport("gdiplus.dll")] private static extern void GdiplusShutdown(IntPtr token);
        [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] private static extern int GdipCreateBitmapFromFile(string filename, out IntPtr bitmap);
        [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(IntPtr image, out uint width);
        [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(IntPtr image, out uint height);
        [DllImport("gdiplus.dll")] private static extern int GdipBitmapLockBits(IntPtr bitmap, ref GpRect rect, uint flags, int format, ref BitmapData data);
        [DllImport("gdiplus.dll")] private static extern int GdipBitmapUnlockBits(IntPtr bitmap, ref BitmapData data);
        [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(IntPtr image);

        // Reads a picture file into RGBA bytes (top row first). The nested try/finally blocks make sure every
        // GDI+ object is given back in reverse order, even when something fails halfway.
        // Reads a picture: GDI+ first (it handles the classic formats), and WIC for everything GDI+ can't read.
        public static byte[] LoadPicture(string path, out int width, out int height, out string error)
        {
            byte[] rgba = LoadWithGdiPlus(path, out width, out height, out error);
            if (rgba != null || error != NotAPicture)
                return rgba; // done, or a problem WIC can't fix either (missing file, too big...)
            return WicReader.Load(path, MaxInputSide, out width, out height, out error);
        }

        private const string NotAPicture = "this file is not a picture Windows can read";

        private static byte[] LoadWithGdiPlus(string path, out int width, out int height, out string error)
        {
            width = height = 0;
            error = null;
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                error = "the file doesn't exist";
                return null;
            }
            if (file.Length > MaxInputBytes)
            {
                error = $"the file is {file.Length / (1024 * 1024)} MB, the limit is {MaxInputBytes / (1024 * 1024)} MB";
                return null;
            }

            var input = new StartupInput { GdiplusVersion = 1 };
            if (GdiplusStartup(out IntPtr token, ref input, IntPtr.Zero) != 0)
            {
                error = "Windows' image library (GDI+) could not start";
                return null;
            }
            try
            {
                if (GdipCreateBitmapFromFile(path, out IntPtr bitmap) != 0)
                {
                    error = NotAPicture; // LoadPicture then tries WIC
                    return null;
                }
                try
                {
                    GdipGetImageWidth(bitmap, out uint w);
                    GdipGetImageHeight(bitmap, out uint h);
                    if (w == 0 || h == 0 || w > MaxInputSide || h > MaxInputSide)
                    {
                        error = $"the picture is {w}x{h}; it must be between 1 and {MaxInputSide} pixels on each side";
                        return null;
                    }
                    width = (int)w;
                    height = (int)h;

                    // Ask GDI+ for the whole picture as 32-bit ARGB, whatever format the file had.
                    var rect = new GpRect { X = 0, Y = 0, Width = width, Height = height };
                    var data = new BitmapData();
                    if (GdipBitmapLockBits(bitmap, ref rect, ImageLockModeRead, PixelFormat32bppARGB, ref data) != 0)
                    {
                        error = "the picture's pixels could not be read";
                        return null;
                    }
                    try
                    {
                        // Copy row by row (Marshal.Copy reads from a memory address into a C# array), then swap
                        // Blue and Red, because GDI+ stores B,G,R,A and we want R,G,B,A.
                        var rgba = new byte[width * height * 4];
                        var row = new byte[width * 4];
                        for (int y = 0; y < height; y++)
                        {
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                            for (int x = 0; x < width; x++)
                            {
                                int i = x * 4, o = (y * width + x) * 4;
                                rgba[o] = row[i + 2];     // red
                                rgba[o + 1] = row[i + 1]; // green
                                rgba[o + 2] = row[i];     // blue
                                rgba[o + 3] = row[i + 3]; // alpha
                            }
                        }
                        return rgba;
                    }
                    finally
                    {
                        GdipBitmapUnlockBits(bitmap, ref data);
                    }
                }
                finally
                {
                    GdipDisposeImage(bitmap); // also unlocks the file on disk
                }
            }
            finally
            {
                GdiplusShutdown(token);
            }
        }

        // ================= 2. resampling (shrinking, growing, cropping) =================

        // Takes the rectangle (left, top, srcW, srcH) of a picture, measured in its pixels (fractions allowed, and it
        // may stick out past the edges), and turns it into an outW x outH picture.
        // Each new pixel covers a rectangle of old pixels, often with fractional edges (for example old pixels
        // 2.4 to 5.1). Every old pixel adds its color times "how much of it is covered" (its weight). Colors are
        // multiplied by alpha first (premultiplied), so a fully transparent pixel adds no color at all. At the end:
        //   color = summed colors / summed alpha        alpha = summed alpha / the new pixel's full area
        // Dividing alpha by the FULL area (not only the part inside the picture) is what makes the parts outside the
        // picture fade to transparent.
        public static byte[] Resample(byte[] rgba, int width, int height, double left, double top, double srcW, double srcH, int outW, int outH)
        {
            var result = new byte[outW * outH * 4]; // starts all zero = fully transparent
            double stepX = srcW / outW, stepY = srcH / outH; // old pixels per new pixel
            double area = stepX * stepY;
            for (int y = 0; y < outH; y++)
            {
                double sy0 = top + y * stepY, sy1 = sy0 + stepY;
                int yFrom = Math.Max(0, (int)Math.Floor(sy0)), yTo = Math.Min(height, (int)Math.Ceiling(sy1));
                for (int x = 0; x < outW; x++)
                {
                    double sx0 = left + x * stepX, sx1 = sx0 + stepX;
                    int xFrom = Math.Max(0, (int)Math.Floor(sx0)), xTo = Math.Min(width, (int)Math.Ceiling(sx1));
                    double r = 0, g = 0, b = 0, a = 0;
                    // Walk the old pixels this new pixel touches (only those inside the picture).
                    for (int sy = yFrom; sy < yTo; sy++)
                    {
                        double wy = Math.Min(sy1, sy + 1) - Math.Max(sy0, sy); // how much of this row is covered
                        for (int sx = xFrom; sx < xTo; sx++)
                        {
                            double w = wy * (Math.Min(sx1, sx + 1) - Math.Max(sx0, sx));
                            int i = (sy * width + sx) * 4;
                            double pa = rgba[i + 3] * w;
                            r += rgba[i] * pa;
                            g += rgba[i + 1] * pa;
                            b += rgba[i + 2] * pa;
                            a += pa;
                        }
                    }
                    if (a > 0)
                    {
                        int o = (y * outW + x) * 4;
                        result[o] = (byte)Math.Round(r / a);     // undo the premultiplying
                        result[o + 1] = (byte)Math.Round(g / a);
                        result[o + 2] = (byte)Math.Round(b / a);
                        result[o + 3] = (byte)Math.Round(Math.Min(255, a / area));
                    }
                }
            }
            return result;
        }

        // Makes a square picture round: everything outside the biggest circle that fits becomes transparent.
        // The edge is smoothed ("anti-aliased"): a pixel half inside the circle keeps half its opacity. For each
        // pixel, (radius - distance from the center + 0.5) is roughly how much of it lies inside the circle;
        // clamping that to 0..1 gives the share to keep.
        public static void ApplyCircle(byte[] rgba, int size)
        {
            double center = size / 2.0, radius = size / 2.0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double dx = x + 0.5 - center, dy = y + 0.5 - center;
                    double inside = Math.Max(0, Math.Min(1, radius - Math.Sqrt(dx * dx + dy * dy) + 0.5));
                    int a = (y * size + x) * 4 + 3;
                    rgba[a] = (byte)Math.Round(rgba[a] * inside);
                }
        }

        // Fits a whole picture inside a size x size square, keeping its shape, centered, with transparent edges:
        // a square crop as big as the longer side, centered, so the shorter side gets empty (transparent) margins.
        public static byte[] FitSquare(byte[] rgba, int width, int height, int size)
        {
            int side = Math.Max(width, height);
            return Resample(rgba, width, height, (width - side) / 2.0, (height - side) / 2.0, side, side, size, size);
        }

        // Shrinks a picture so neither side is bigger than maxSide (smaller pictures are returned unchanged).
        // The crop editor works on this smaller copy: it's plenty for a 128 x 128 result and keeps memory low.
        public static byte[] FitInside(byte[] rgba, ref int width, ref int height, int maxSide)
        {
            if (width <= maxSide && height <= maxSide)
                return rgba;
            double scale = Math.Min((double)maxSide / width, (double)maxSide / height);
            int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
            byte[] result = Resample(rgba, width, height, 0, 0, width, height, w, h);
            width = w;   // "ref" parameters: the caller's variables get the new size
            height = h;
            return result;
        }

        // ================= 3. writing a small PNG =================

        // Encodes RGBA pixels (top row first) as the SMALLEST PNG we can make, because every byte of an icon is
        // sent over the network to other players (and the game's developer pays for that traffic).
        // It makes two versions and keeps the smaller one:
        //   - a palette PNG: at most 256 different colors, each pixel stored as one number (0-255) that points into
        //     the color list. Photos lose a tiny bit of color detail; at icon size you can hardly see it, and the file
        //     is about 5 to 6 times smaller.
        //   - a full-color PNG (every pixel keeps its exact color). Only wins for very simple pictures.
        public static byte[] EncodePng(byte[] rgba, int width, int height)
        {
            byte[] palette = EncodePalettePng(rgba, width, height);
            byte[] full = EncodeTrueColorPng(rgba, width, height);
            return palette.Length <= full.Length ? palette : full;
        }

        // ---- palette PNG ("indexed color", PNG color type 3) ----
        // Step 1 picks up to 256 colors that represent the picture well (Quantize, below).
        // Step 2 replaces every pixel by the number of its closest palette color.
        // Step 3 writes the PNG: PLTE = the colors (R,G,B), tRNS = their transparency (A), IDAT = the numbers.
        private static byte[] EncodePalettePng(byte[] rgba, int width, int height)
        {
            uint[] palette = Quantize(rgba, 256);

            // Colors with transparency go FIRST in the list: tRNS only has to list alphas up to the last see-through
            // color (the rest count as fully opaque), so this keeps tRNS short. Array.Sort with a comparison method.
            Array.Sort(palette, CompareByAlpha);

            // Map every pixel to its closest palette color. Most pictures repeat colors a lot, so the answer for each
            // exact color is remembered in a Dictionary (a lookup table: color -> palette number).
            var lookup = new System.Collections.Generic.Dictionary<uint, byte>();
            var raw = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0); // filter 0 = "none": filters help photos, but not lists of palette numbers
                for (int x = 0; x < width; x++)
                {
                    uint color = Pack(rgba, (y * width + x) * 4);
                    if (!lookup.TryGetValue(color, out byte index))
                    {
                        index = Nearest(palette, color);
                        lookup[color] = index;
                    }
                    raw.WriteByte(index);
                }
            }

            var packed = new MemoryStream();
            using (var zlib = new ZLibStream(packed, CompressionLevel.SmallestSize, true))
                raw.WriteTo(zlib);

            // PLTE: 3 bytes (R,G,B) per color. tRNS: 1 alpha byte per color, up to the last one that isn't opaque.
            var plte = new byte[palette.Length * 3];
            int lastSeeThrough = -1;
            for (int i = 0; i < palette.Length; i++)
            {
                plte[i * 3] = (byte)(palette[i] >> 24);
                plte[i * 3 + 1] = (byte)(palette[i] >> 16);
                plte[i * 3 + 2] = (byte)(palette[i] >> 8);
                if ((byte)palette[i] != 255)
                    lastSeeThrough = i;
            }
            var trns = new byte[lastSeeThrough + 1];
            for (int i = 0; i < trns.Length; i++)
                trns[i] = (byte)palette[i];

            var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var header = new byte[13];
            WriteUInt32(header, 0, (uint)width);
            WriteUInt32(header, 4, (uint)height);
            header[8] = 8; // 8 bits per palette number
            header[9] = 3; // color type 3 = palette
            WriteChunk(png, "IHDR", header);
            WriteChunk(png, "PLTE", plte);
            if (trns.Length > 0)
                WriteChunk(png, "tRNS", trns);
            WriteChunk(png, "IDAT", packed.ToArray());
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        // One RGBA pixel packed into one 32-bit number: 0xRRGGBBAA. ">>" (shift right) and "(byte)" take parts back out.
        // Fully transparent pixels all become 0, because their color is invisible anyway (fewer colors to store).
        private static uint Pack(byte[] rgba, int i)
        {
            if (rgba[i + 3] == 0)
                return 0;
            return ((uint)rgba[i] << 24) | ((uint)rgba[i + 1] << 16) | ((uint)rgba[i + 2] << 8) | rgba[i + 3];
        }

        // Palette order: see-through colors first (keeps tRNS short), then dark to bright. With colors sorted by
        // brightness, neighboring pixels usually get neighboring numbers, which zlib packs better.
        private static int CompareByAlpha(uint a, uint b)
        {
            int byAlpha = ((byte)a).CompareTo((byte)b);
            return byAlpha != 0 ? byAlpha : Brightness(a).CompareTo(Brightness(b));
        }

        // Rough brightness as the eye sees it (green counts most, blue least).
        private static int Brightness(uint c)
        {
            return (int)((c >> 24) & 255) * 299 + (int)((c >> 16) & 255) * 587 + (int)((c >> 8) & 255) * 114;
        }

        // The palette number whose color is closest to "color" (smallest squared distance over R, G, B and A).
        private static byte Nearest(uint[] palette, uint color)
        {
            int best = 0;
            long bestDistance = long.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                long d = 0;
                for (int shift = 0; shift < 32; shift += 8)
                {
                    long diff = (int)((palette[i] >> shift) & 255) - (int)((color >> shift) & 255);
                    d += diff * diff;
                }
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return (byte)best;
        }

        // "Median cut": picks at most maxColors colors that represent the picture.
        //   1. Count how often each exact color appears. If there are few enough, use them all (no loss at all).
        //   2. Otherwise put all colors in one "box". Repeatedly take the box whose colors are spread the most
        //      (the biggest range in red, green, blue or alpha, times how many pixels it holds) and cut it in two at
        //      the middle pixel along that channel. Stop at maxColors boxes.
        //   3. Each box becomes one palette color: the average of its colors, weighted by how often each appears.
        private static uint[] Quantize(byte[] rgba, int maxColors)
        {
            var counts = new System.Collections.Generic.Dictionary<uint, int>();
            for (int i = 0; i < rgba.Length; i += 4)
            {
                uint c = Pack(rgba, i);
                counts.TryGetValue(c, out int n); // n is 0 when the color isn't there yet
                counts[c] = n + 1;
            }
            if (counts.Count <= maxColors)
                return new System.Collections.Generic.List<uint>(counts.Keys).ToArray();

            var boxes = new System.Collections.Generic.List<ColorBox> { new ColorBox(counts) };
            while (boxes.Count < maxColors)
            {
                // Find the box most worth cutting.
                int pick = -1;
                long bestScore = 0;
                for (int i = 0; i < boxes.Count; i++)
                {
                    long score = (long)boxes[i].Range(out _) * boxes[i].Pixels;
                    if (boxes[i].Colors.Count > 1 && score > bestScore)
                    {
                        bestScore = score;
                        pick = i;
                    }
                }
                if (pick < 0)
                    break; // nothing left to cut
                ColorBox other = boxes[pick].Split();
                boxes.Add(other);
            }
            var result = new uint[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
                result[i] = boxes[i].Average();
            return result;
        }

        // One box of the median cut: a list of (color, how many pixels have it).
        private sealed class ColorBox
        {
            public System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<uint, int>> Colors;
            public long Pixels;

            public ColorBox(System.Collections.Generic.Dictionary<uint, int> counts)
                : this(new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<uint, int>>(counts)) { }

            private ColorBox(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<uint, int>> colors)
            {
                Colors = colors;
                foreach (var pair in colors)
                    Pixels += pair.Value;
            }

            // The biggest spread (max - min) over the four channels, and which channel ("shift": 24 = R ... 0 = A).
            public int Range(out int shift)
            {
                shift = 24;
                int best = -1;
                for (int s = 0; s < 32; s += 8)
                {
                    int min = 255, max = 0;
                    foreach (var pair in Colors)
                    {
                        int v = (int)((pair.Key >> s) & 255);
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                    if (max - min > best)
                    {
                        best = max - min;
                        shift = s;
                    }
                }
                return best;
            }

            // Sorts the colors along the most spread channel and moves the second half (by pixel count) into a new box.
            public ColorBox Split()
            {
                Range(out int shift);
                // Sort with a small comparer object that knows which channel to compare (VOIDMODS avoids lambdas
                // that capture variables, see build.ps1).
                Colors.Sort(new ChannelOrder(shift));
                long half = Pixels / 2, seen = 0;
                int cut = 1;
                for (int i = 0; i < Colors.Count - 1; i++)
                {
                    seen += Colors[i].Value;
                    cut = i + 1;
                    if (seen >= half)
                        break;
                }
                var second = new ColorBox(Colors.GetRange(cut, Colors.Count - cut));
                Colors.RemoveRange(cut, Colors.Count - cut);
                Pixels -= second.Pixels;
                return second;
            }

            // The pixel-weighted average color of the box.
            public uint Average()
            {
                double r = 0, g = 0, b = 0, a = 0;
                foreach (var pair in Colors)
                {
                    r += ((pair.Key >> 24) & 255) * (double)pair.Value;
                    g += ((pair.Key >> 16) & 255) * (double)pair.Value;
                    b += ((pair.Key >> 8) & 255) * (double)pair.Value;
                    a += (pair.Key & 255) * (double)pair.Value;
                }
                return ((uint)Math.Round(r / Pixels) << 24) | ((uint)Math.Round(g / Pixels) << 16)
                     | ((uint)Math.Round(b / Pixels) << 8) | (uint)Math.Round(a / Pixels);
            }
        }

        // Compares two (color, count) pairs by one channel. An IComparer is an object with a Compare method that
        // List.Sort calls to ask "which of these two comes first?" (negative = a first, positive = b first).
        private sealed class ChannelOrder : System.Collections.Generic.IComparer<System.Collections.Generic.KeyValuePair<uint, int>>
        {
            private readonly int shift;
            public ChannelOrder(int shift) { this.shift = shift; }
            public int Compare(System.Collections.Generic.KeyValuePair<uint, int> a, System.Collections.Generic.KeyValuePair<uint, int> b)
            {
                return ((a.Key >> shift) & 255).CompareTo((b.Key >> shift) & 255);
            }
        }

        // ---- full-color PNG (PNG color type 2 or 6) ----
        // Encodes RGBA pixels (top row first) as a PNG file. See PngReader.cs for how the format is built.
        private static byte[] EncodeTrueColorPng(byte[] rgba, int width, int height)
        {
            // If every pixel is fully opaque, store only R,G,B (color type 2); otherwise R,G,B,A (color type 6).
            bool opaque = true;
            for (int i = 3; i < rgba.Length; i += 4)
                if (rgba[i] != 255) { opaque = false; break; }
            int channels = opaque ? 3 : 4;
            int stride = width * channels;

            // Build the raw rows: [filter byte][row bytes] for each row, choosing the best filter per row.
            var raw = new MemoryStream();
            var prev = new byte[stride];
            var cur = new byte[stride];
            var candidate = new byte[stride];
            var best = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    for (int c = 0; c < channels; c++)
                        cur[x * channels + c] = rgba[(y * width + x) * 4 + c];

                // Try all 5 PNG filters and keep the one whose bytes are "smallest" (closest to 0 when read as
                // -128..127). Small, similar numbers are what zlib compresses best. This is the usual rule of thumb.
                int bestFilter = 0;
                long bestScore = long.MaxValue;
                for (int f = 0; f < 5; f++)
                {
                    long score = 0;
                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= channels ? cur[i - channels] : 0;
                        int up = prev[i];
                        int upLeft = i >= channels ? prev[i - channels] : 0;
                        int predict = f == 0 ? 0 : f == 1 ? left : f == 2 ? up : f == 3 ? (left + up) / 2 : Paeth(left, up, upLeft);
                        byte v = (byte)(cur[i] - predict);
                        candidate[i] = v;
                        score += v < 128 ? v : 256 - v;
                    }
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestFilter = f;
                        Buffer.BlockCopy(candidate, 0, best, 0, stride);
                    }
                }
                raw.WriteByte((byte)bestFilter);
                raw.Write(best, 0, stride);
                // This row becomes "the row above" for the next one (swap the two buffers).
                byte[] swap = prev;
                prev = cur;
                cur = swap;
            }

            // Compress the rows with zlib at its smallest setting. "using" finishes and closes the stream.
            var packed = new MemoryStream();
            using (var zlib = new ZLibStream(packed, CompressionLevel.SmallestSize, true))
                raw.WriteTo(zlib);

            // Assemble the file: signature, IHDR (size and format), IDAT (pixels), IEND (end).
            var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var header = new byte[13];
            WriteUInt32(header, 0, (uint)width);
            WriteUInt32(header, 4, (uint)height);
            header[8] = 8;                          // 8 bits per channel
            header[9] = (byte)(opaque ? 2 : 6);     // color type: 2 = RGB, 6 = RGBA
            // bytes 10-12 stay 0: standard compression, standard filters, no interlacing
            WriteChunk(png, "IHDR", header);
            WriteChunk(png, "IDAT", packed.ToArray());
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        // A chunk is [length][type][data][CRC]. The CRC is a checksum over type + data that lets readers spot damage.
        private static void WriteChunk(MemoryStream png, string type, byte[] data)
        {
            var length = new byte[4];
            WriteUInt32(length, 0, (uint)data.Length);
            png.Write(length, 0, 4);
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            png.Write(typeBytes, 0, 4);
            png.Write(data, 0, data.Length);
            uint crc = Crc32(typeBytes, 0xFFFFFFFFu);
            crc = Crc32(data, crc) ^ 0xFFFFFFFFu;
            var crcBytes = new byte[4];
            WriteUInt32(crcBytes, 0, crc);
            png.Write(crcBytes, 0, 4);
        }

        // Writes a number as 4 bytes, biggest part first ("big-endian"), as PNG wants.
        private static void WriteUInt32(byte[] b, int i, uint v)
        {
            b[i] = (byte)(v >> 24);
            b[i + 1] = (byte)(v >> 16);
            b[i + 2] = (byte)(v >> 8);
            b[i + 3] = (byte)v;
        }

        // CRC-32 as PNG defines it. The table holds a precomputed answer for each possible byte value, so each
        // byte costs one table lookup instead of 8 bit-by-bit steps. "^" is XOR (flip the bits where the other
        // number has a 1), and "uint" is a whole number without a sign (0 to about 4 billion).
        private static uint[] crcTable;
        private static uint Crc32(byte[] data, uint crc)
        {
            if (crcTable == null)
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                crcTable = table;
            }
            foreach (byte b in data)
                crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        // The Paeth predictor (same as in PngReader): guess = left + up - upLeft, pick the neighbour closest to it.
        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc)
                return a;
            return pb <= pc ? b : c;
        }
    }
}
