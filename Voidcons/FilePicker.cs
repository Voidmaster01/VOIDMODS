// FilePicker.cs - part of the Voidcons mod: opens Windows' normal "Open file" window to pick a picture,
// and reads the chosen picture, all on a background thread.
//
// WHAT IT DOES
//   Start() opens the window (unless one is already open). When the player picks a file, ImageTools reads it
//   and shrinks it to at most 1024 pixels on a side, for the move-and-crop editor. The game's main loop
//   collects the result with TryTakeResult() every frame.
//
// HOW TO RUN / WHAT IT NEEDS
//   Compiled into Voidcons.dll by build.ps1. Needs Windows (comdlg32.dll, the "common dialogs" library).
//
// WHY A BACKGROUND THREAD
//   GetOpenFileName waits until the player closes the window. On the game's main thread that would freeze the
//   game. A thread is a second line of work that runs at the same time, so the game keeps drawing meanwhile.
//   Unity objects must only be touched from the main thread, so this thread only works with plain files and
//   bytes, and hands its result over through a field protected by a "lock" (only one thread at a time may
//   read or write it, so they never see half-written data).

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Voidcons
{
    internal static class FilePicker
    {
        // The result of one pick: the picture's pixels (RGBA, top row first; null if nothing usable), its size,
        // and a message for the status line.
        internal sealed class Result
        {
            public byte[] Rgba;
            public int Width, Height;
            public string Message;
        }

        public const int MaxEditSide = 1024; // pictures are shrunk to this size for the crop editor

        private static readonly object Gate = new object(); // the "lock" object both threads agree on
        private static Result pending;                     // a finished result waiting for the main thread
        private static volatile bool busy;                 // volatile = always read the latest value, never a cached copy
        private static IntPtr owner;                       // the game window, so the dialog appears in front of it

        public static bool Busy
        {
            get { return busy; }
        }

        // OPENFILENAMEW is the settings block GetOpenFileNameW expects. The fields must be in exactly this order
        // and size (Sequential layout, Unicode text), because the C library reads them by position in memory.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public IntPtr lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;      // a buffer we allocate; Windows writes the chosen path into it
            public int nMaxFile;
            public IntPtr lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        private const int OFN_NOCHANGEDIR = 0x8;         // don't change the game's current folder
        private const int OFN_PATHMUSTEXIST = 0x800;
        private const int OFN_FILEMUSTEXIST = 0x1000;
        private const int OFN_EXPLORER = 0x80000;        // the modern Explorer-style window

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] private static extern bool GetOpenFileNameW(ref OpenFileName ofn);
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();

        // Opens the window on a new thread. Call it from the main thread (it remembers the game window first).
        public static void Start()
        {
            if (busy)
                return;
            busy = true;
            owner = GetActiveWindow();
            var thread = new Thread(new ThreadStart(Run));
            thread.IsBackground = true;                  // it never keeps the game alive on exit
            thread.SetApartmentState(ApartmentState.STA); // Windows dialogs need this "single-threaded apartment" mode
            thread.Start();
        }

        // Gives the main thread a finished result once (or null when there is none yet).
        public static Result TryTakeResult()
        {
            lock (Gate)
            {
                Result r = pending;
                pending = null;
                return r;
            }
        }

        // The background thread: show the window, then compress the chosen picture. try/finally makes sure
        // "busy" goes back to false and the path buffer is freed, whatever happens.
        private static void Run()
        {
            var result = new Result();
            IntPtr buffer = IntPtr.Zero;
            try
            {
                const int maxChars = 1024;
                // AllocHGlobal reserves raw memory outside C#'s control; it must be freed with FreeHGlobal.
                // Unicode uses 2 bytes per character. The buffer must start empty (all zeros).
                buffer = Marshal.AllocHGlobal(maxChars * 2);
                Marshal.Copy(new byte[maxChars * 2], 0, buffer, maxChars * 2);

                var ofn = new OpenFileName();
                ofn.lStructSize = Marshal.SizeOf(typeof(OpenFileName));
                ofn.hwndOwner = owner;
                // The filter list: pairs of "label" and "patterns", each ending with \0, and an extra \0 at the end.
                ofn.lpstrFilter = "Pictures (PNG, JPG, WebP, GIF, BMP, HEIC, AVIF...)\0" +
                    "*.png;*.jpg;*.jpeg;*.jfif;*.webp;*.gif;*.bmp;*.tif;*.tiff;*.ico;*.heic;*.heif;*.avif;*.jxr;*.wdp\0" +
                    "All files\0*.*\0\0";
                ofn.lpstrFile = buffer;
                ofn.nMaxFile = maxChars;
                ofn.lpstrTitle = "Voidcons - choose your player icon";
                ofn.Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;

                if (!GetOpenFileNameW(ref ofn))
                {
                    result.Message = "No picture chosen.";
                    return;
                }
                string path = Marshal.PtrToStringUni(buffer); // read the chosen path back out of the buffer
                byte[] rgba = ImageTools.LoadPicture(path, out int width, out int height, out string error);
                if (rgba == null)
                {
                    result.Message = "Can't use that picture: " + error;
                    return;
                }
                int w = width, h = height;
                result.Rgba = ImageTools.FitInside(rgba, ref w, ref h, MaxEditSide);
                result.Width = w;
                result.Height = h;
                result.Message = $"{System.IO.Path.GetFileName(path)} ({width}x{height})";
            }
            catch (Exception e)
            {
                result.Rgba = null;
                result.Message = "Choosing a picture failed: " + e.Message;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
                lock (Gate)
                    pending = result;
                busy = false;
            }
        }
    }
}
