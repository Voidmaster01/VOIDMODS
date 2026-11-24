// WicReader.cs - part of the Voidcons mod: reads pictures with WIC (Windows Imaging Component).
//
// WHAT IT DOES
//   Load(path) turns a picture file into RGBA pixels (4 bytes per pixel, top row first), using WIC, the image
//   system Windows Photos uses. It reads every format Windows has a "codec" (decoder) for: WebP (built into
//   Windows 11; Windows 10 needs the free "WebP Image Extensions"), HEIC/HEIF and AVIF (with Windows' free HEIF /
//   AV1 extensions), JPEG XR, DDS, and all the classics. ImageTools uses it when GDI+ (the older Windows image
//   library) can't read a file.
//
// HOW TO RUN / WHAT IT NEEDS
//   Compiled into Voidcons.dll by build.ps1. Windows only. No Unity, so it can run on a background thread.
//
// HOW IT WORKS: COM
//   WIC is a "COM" library: Windows objects that are used through "interfaces", fixed lists of functions.
//   C# can call them when we describe each interface with [ComImport] + its Guid (the interface's unique ID)
//   and list its functions in EXACTLY the same order as Windows defines them, because COM finds a function by
//   its position in that list, not by its name. That's why some functions below are listed but never used:
//   they hold the places of the ones before the functions we need.
//   The steps: make the WIC "factory" -> open the file as a "decoder" -> take frame 0 (the first picture; for
//   animated files, the first frame) -> a "format converter" turns it into 32-bit BGRA -> copy the pixels out.
//   If anything fails, COM reports an error code, which .NET turns into an exception; Load catches it.

using System;
using System.Runtime.InteropServices;

namespace Voidcons
{
    internal static class WicReader
    {
        public static byte[] Load(string path, int maxSide, out int width, out int height, out string error)
        {
            width = height = 0;
            error = null;
            object factoryObj = null, decoder = null, frame = null, converter = null;
            try
            {
                // Make the factory. Type.GetTypeFromCLSID + Activator.CreateInstance is how .NET creates a COM object
                // from its class ID (the CLSID of WIC's imaging factory).
                Type factoryType = Type.GetTypeFromCLSID(new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a"));
                factoryObj = Activator.CreateInstance(factoryType);
                var factory = (IWICImagingFactory)factoryObj;

                // Open the file. 0x80000000 = "read access"; 0 = read the file's metadata only when needed.
                decoder = factory.CreateDecoderFromFilename(path, IntPtr.Zero, 0x80000000, 0);
                frame = ((IWICBitmapDecoder)decoder).GetFrame(0);
                var source = (IWICBitmapSource)frame;
                source.GetSize(out uint w, out uint h);
                if (w == 0 || h == 0 || w > maxSide || h > maxSide)
                {
                    error = $"the picture is {w}x{h}; it must be between 1 and {maxSide} pixels on each side";
                    return null;
                }

                // Convert whatever the file holds into 32-bit BGRA (blue, green, red, alpha), the most widely
                // supported target format, then copy all rows out into one array.
                converter = factory.CreateFormatConverter();
                var conv = (IWICFormatConverter)converter;
                Guid bgra = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc90f"); // GUID_WICPixelFormat32bppBGRA
                conv.Initialize(source, ref bgra, 0, IntPtr.Zero, 0.0, 0);
                width = (int)w;
                height = (int)h;
                int stride = width * 4;
                var pixels = new byte[stride * height];
                // CopyPixels writes into memory we hand over by address. GCHandle "pins" the array so .NET can't
                // move it in memory while Windows writes into it; Free() un-pins it afterwards.
                GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    ((IWICBitmapSource)converter).CopyPixels(IntPtr.Zero, (uint)stride, (uint)pixels.Length, pin.AddrOfPinnedObject());
                }
                finally
                {
                    pin.Free();
                }
                // BGRA -> RGBA: swap the blue and red bytes of every pixel.
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte blue = pixels[i];
                    pixels[i] = pixels[i + 2];
                    pixels[i + 2] = blue;
                }
                return pixels;
            }
            catch (Exception e)
            {
                // The usual cases: not a picture at all, or no codec for this format (for example HEIC without the
                // HEIF extension). The technical message goes to the log; the player gets a plain explanation.
                VoidconsMod.Instance?.LoggerInstance.Msg("WIC could not read " + System.IO.Path.GetFileName(path) + ": " + e.Message.Trim());
                error = "this isn't a picture, or Windows can't read this kind of file. WebP, HEIC and AVIF need " +
                        "Windows' free WebP / HEIF / AV1 Image Extensions (WebP is built into Windows 11)";
                width = height = 0;
                return null;
            }
            finally
            {
                // COM objects count who uses them; ReleaseComObject says "we're done", in reverse order of creation.
                foreach (object o in new[] { converter, frame, decoder, factoryObj })
                    if (o != null)
                        Marshal.ReleaseComObject(o);
            }
        }

        // ---- The WIC interfaces we use, with their functions in Windows' order ----
        // Functions we never call are declared with "void" and no parameters: they only hold their place in the list.

        [ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICImagingFactory
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            object CreateDecoderFromFilename([MarshalAs(UnmanagedType.LPWStr)] string fileName, IntPtr vendor, uint desiredAccess, int metadataOptions);
            void CreateDecoderFromStream();
            void CreateDecoderFromFileHandle();
            void CreateComponentInfo();
            void CreateDecoder();
            void CreateEncoder();
            void CreatePalette();
            [return: MarshalAs(UnmanagedType.Interface)]
            object CreateFormatConverter();
        }

        [ComImport, Guid("9EDDE9E7-8DEE-47ea-99DF-E6FAF2ED44BF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapDecoder
        {
            void QueryCapability();
            void Initialize();
            void GetContainerFormat();
            void GetDecoderInfo();
            void CopyPalette();
            void GetMetadataQueryReader();
            void GetPreview();
            void GetColorContexts();
            void GetThumbnail();
            void GetFrameCount();
            [return: MarshalAs(UnmanagedType.Interface)]
            object GetFrame(uint index);
        }

        // The base of every WIC picture: frames and converters both start with these five functions.
        [ComImport, Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapSource
        {
            void GetSize(out uint width, out uint height);
            void GetPixelFormat(out Guid format);
            void GetResolution(out double dpiX, out double dpiY);
            void CopyPalette(IntPtr palette);
            void CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
        }

        // A format converter is a bitmap source with two more functions after the five base ones, so the base ones
        // are repeated here first (COM interfaces in C# must list inherited functions again, in order).
        [ComImport, Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICFormatConverter
        {
            void GetSize(out uint width, out uint height);
            void GetPixelFormat(out Guid format);
            void GetResolution(out double dpiX, out double dpiY);
            void CopyPalette(IntPtr palette);
            void CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
            void Initialize([MarshalAs(UnmanagedType.Interface)] IWICBitmapSource source, ref Guid destinationFormat,
                int dither, IntPtr palette, double alphaThresholdPercent, int paletteTranslate);
        }
    }
}
