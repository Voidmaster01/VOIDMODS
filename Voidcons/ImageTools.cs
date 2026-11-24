// ImageTools.cs - part of the Voidcons mod: the compression system that turns ANY picture into a small icon.
//
// WHAT IT DOES
//   Reads a picture file (PNG, JPG, BMP, GIF, TIFF...), lets the move-and-crop editor pick any square part of it,
//   shrinks that part to 64 x 64, and writes it as a small, optimized PNG file. That PNG is what Voidcons saves
//   as icon.png and sends to other players.
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
//   3. EncodePng: writes the pixels as a PNG. Each row gets the PNG "filter" that makes it compress best,
//      then zlib packs everything at its smallest setting. Pictures without transparency are stored without
//      the alpha channel, which makes them a quarter smaller.

using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Voidcons
{
    internal static class ImageTools
    {
        // Every icon is made 256 x 256 and round, like the game's own backer icons (Brian makes those with a
        // circle crop tool), in HD so they stay sharp on big screens.
        public const int IconSize = 256;
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
        // The crop editor works on this smaller copy: it's plenty for a 64 x 64 result and keeps memory low.
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

        // Encodes RGBA pixels (top row first) as a PNG file. See PngReader.cs for how the format is built.
        public static byte[] EncodePng(byte[] rgba, int width, int height)
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
