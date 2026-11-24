// PngReader.cs - part of ModConfigs (shared by all VOIDMODS): reads PNG files into raw pixels, in plain C#.
//
// WHAT IT DOES
//   Decode(png, maxSize, ...) turns the bytes of a PNG file into RGBA pixels (4 bytes per pixel, top row first),
//   or returns null with a reason. ModConfigs and Voidcons use it for every picture they show, because this game build doesn't
//   include Unity's own PNG reader (ImageConversion.LoadImage was stripped out).
//
// HOW TO RUN / WHAT IT NEEDS
//   It is compiled into ModConfigs.dll by build.ps1 together with the other .cs files. Plain .NET 6, no Unity.

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ModConfigs
{
    // ================= PNG reader =================
    // A small PNG decoder in plain C#, because this game build doesn't include Unity's own one.
    // A PNG file is: an 8-byte signature, then "chunks". Each chunk is [4-byte length][4-letter type][data][4-byte CRC].
    //   IHDR = width, height and pixel format.  PLTE = color palette.  tRNS = transparency for palette colors.
    //   IDAT = the pixels, compressed with zlib (several IDAT chunks are one stream glued together).  IEND = the end.
    // The pixels come as rows, and each row starts with a "filter" byte: a trick that stores each byte as the
    // difference to its neighbours, which compresses better. Undoing those filters gives the real bytes back.
    // Output: RGBA bytes (red, green, blue, alpha; 4 per pixel), top row first. Interlaced PNGs are not supported.
    public static class PngReader
    {
        public static byte[] Decode(byte[] png, int maxSize, out int width, out int height, out string error)
        {
            width = height = 0;
            error = null;
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (png.Length < 8)
            {
                error = "not a PNG file";
                return null;
            }
            for (int i = 0; i < 8; i++)
                if (png[i] != signature[i])
                {
                    error = "not a PNG file";
                    return null;
                }

            int bitDepth = 0, colorType = -1, interlace = 0;
            byte[] palette = null, paletteAlpha = null;
            var compressed = new MemoryStream(); // collects all IDAT data

            // Walk the chunks. Numbers in PNG are "big-endian" (most significant byte first), see ReadUInt32.
            int pos = 8;
            while (pos + 8 <= png.Length)
            {
                int length = (int)ReadUInt32(png, pos);
                string type = Encoding.ASCII.GetString(png, pos + 4, 4);
                int data = pos + 8;
                if (length < 0 || data + length + 4 > png.Length)
                {
                    error = "the file is cut off or damaged";
                    return null;
                }
                if (type == "IHDR")
                {
                    width = (int)ReadUInt32(png, data);
                    height = (int)ReadUInt32(png, data + 4);
                    bitDepth = png[data + 8];
                    colorType = png[data + 9];
                    interlace = png[data + 12];
                }
                else if (type == "PLTE")
                    palette = Slice(png, data, length);
                else if (type == "tRNS")
                    paletteAlpha = Slice(png, data, length);
                else if (type == "IDAT")
                    compressed.Write(png, data, length);
                else if (type == "IEND")
                    break;
                pos = data + length + 4; // skip the data and the CRC
            }

            // Check the format before doing any work.
            if (width <= 0 || height <= 0)
            {
                error = "no picture size found";
                return null;
            }
            if (width > maxSize || height > maxSize)
            {
                error = $"the picture is {width}x{height}, the limit is {maxSize}x{maxSize}";
                return null;
            }
            if (interlace != 0)
            {
                error = "interlaced PNGs are not supported (save it again without 'interlaced')";
                return null;
            }
            // Channels per pixel for each PNG color type: 0 gray, 2 RGB, 3 palette, 4 gray+alpha, 6 RGBA.
            int channels;
            switch (colorType)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 3: channels = 1; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: error = $"unknown PNG color type {colorType}"; return null;
            }
            bool depthOk = bitDepth == 8 || bitDepth == 16 || ((colorType == 0 || colorType == 3) && (bitDepth == 1 || bitDepth == 2 || bitDepth == 4));
            if (!depthOk || (colorType == 3 && bitDepth == 16))
            {
                error = $"unsupported bit depth {bitDepth}";
                return null;
            }
            if (colorType == 3 && palette == null)
            {
                error = "palette PNG without a palette";
                return null;
            }

            int bitsPerPixel = channels * bitDepth;
            int stride = (width * bitsPerPixel + 7) / 8;       // bytes per row, rounded up
            int filterStep = Math.Max(1, bitsPerPixel / 8);    // bytes per pixel, as the filters count them

            // Decompress. A zlib stream is the compressed data plus a small header; ZLibStream (built into .NET 6)
            // unpacks it. "using" closes the streams automatically when the block ends.
            byte[] raw = new byte[height * (stride + 1)];
            compressed.Position = 0;
            using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int n = zlib.Read(raw, read, raw.Length - read);
                    if (n <= 0)
                        break;
                    read += n;
                }
                if (read < raw.Length)
                {
                    error = "the picture data is incomplete";
                    return null;
                }
            }

            // Undo the row filters, in place. prev = the row above (all zeros for the first row).
            var rows = new byte[height * stride];
            var prev = new byte[stride];
            var cur = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                int filter = raw[y * (stride + 1)];
                Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
                for (int x = 0; x < stride; x++)
                {
                    int left = x >= filterStep ? cur[x - filterStep] : 0;
                    int up = prev[x];
                    int upLeft = x >= filterStep ? prev[x - filterStep] : 0;
                    int add;
                    switch (filter)
                    {
                        case 0: add = 0; break;                       // None
                        case 1: add = left; break;                    // Sub: difference to the left byte
                        case 2: add = up; break;                      // Up: difference to the byte above
                        case 3: add = (left + up) / 2; break;         // Average of left and above
                        case 4: add = Paeth(left, up, upLeft); break; // Paeth: whichever neighbour predicts best
                        default: error = $"bad row filter {filter}"; return null;
                    }
                    cur[x] = (byte)(cur[x] + add); // (byte) keeps it in 0..255, wrapping around like PNG expects
                }
                Buffer.BlockCopy(cur, 0, rows, y * stride, stride);
                // Swap the two row buffers: this row becomes "the row above" for the next one.
                byte[] swap = prev;
                prev = cur;
                cur = swap;
            }

            // Turn every pixel into 4 bytes of RGBA.
            var rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int o = (y * width + x) * 4;
                    byte r, g, b, a = 255;
                    if (colorType == 3)
                    {
                        int index = SmallSample(rows, rowStart, x, bitDepth);
                        if (index * 3 + 2 >= palette.Length)
                        {
                            error = "a pixel points outside the palette";
                            return null;
                        }
                        r = palette[index * 3];
                        g = palette[index * 3 + 1];
                        b = palette[index * 3 + 2];
                        if (paletteAlpha != null && index < paletteAlpha.Length)
                            a = paletteAlpha[index];
                    }
                    else if (bitDepth < 8)
                    {
                        // Gray with 1, 2 or 4 bits: stretch the value to 0..255 (for 1 bit: 0 -> 0, 1 -> 255).
                        int v = SmallSample(rows, rowStart, x, bitDepth);
                        r = g = b = (byte)(v * 255 / ((1 << bitDepth) - 1));
                    }
                    else
                    {
                        // 8 or 16 bits per channel. For 16 bits only the high byte is used (plenty for an icon).
                        int bytesPerChannel = bitDepth / 8;
                        int p = rowStart + x * channels * bytesPerChannel;
                        byte c0 = rows[p];
                        if (colorType == 0) { r = g = b = c0; }
                        else if (colorType == 4) { r = g = b = c0; a = rows[p + bytesPerChannel]; }
                        else
                        {
                            r = c0;
                            g = rows[p + bytesPerChannel];
                            b = rows[p + 2 * bytesPerChannel];
                            if (colorType == 6)
                                a = rows[p + 3 * bytesPerChannel];
                        }
                    }
                    rgba[o] = r;
                    rgba[o + 1] = g;
                    rgba[o + 2] = b;
                    rgba[o + 3] = a;
                }
            }
            return rgba;
        }

        // Reads a 4-byte big-endian number: the first byte is the biggest part ("<< 24" multiplies by 2^24).
        private static uint ReadUInt32(byte[] b, int i)
        {
            return (uint)(b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]);
        }

        // Reads pixel x from a row packed with 1, 2, 4 or 8 bits per pixel. Several small pixels share one byte,
        // leftmost pixel in the highest bits; the shift moves pixel x's bits to the bottom and the mask keeps them.
        private static int SmallSample(byte[] rows, int rowStart, int x, int bitDepth)
        {
            if (bitDepth == 8)
                return rows[rowStart + x];
            int bit = x * bitDepth;
            int shift = 8 - bitDepth - (bit % 8);
            return (rows[rowStart + bit / 8] >> shift) & ((1 << bitDepth) - 1);
        }

        // The Paeth predictor from the PNG standard: guess = left + up - upLeft, then pick the neighbour closest to it.
        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc)
                return a;
            return pb <= pc ? b : c;
        }

        private static byte[] Slice(byte[] data, int start, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(data, start, result, 0, length);
            return result;
        }
    }
}
