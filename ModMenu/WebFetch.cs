// WebFetch.cs - part of ModMenu: downloads files with the GAME's own web code (Unity's UnityWebRequest).
//
// WHAT IT DOES
//   ModMenu needs to download two kinds of files: the approved mod list and the approved mod DLLs.
//   Earlier versions used .NET's HttpClient for that. Antivirus programs judge a mod partly by which tools it uses,
//   and "a DLL that has its own internet downloader and writes .dll files" looks like a typical "dropper" virus,
//   even when (like here) every file is checked against an approved fingerprint first. So ModMenu now asks the
//   game's own engine to download, the same way the game itself talks to the internet.
//
// HOW TO USE IT
//   byte[] data = WebFetch.Get("https://...");   // from a BACKGROUND thread only (it waits for the answer)
//   WebFetch.Tick();                              // every frame, from ModMenuMod.OnUpdate (the main thread)
//
// HOW IT WORKS (and why it is built this way)
//   Unity objects, UnityWebRequest included, may only be used on the game's MAIN thread. ModMenu's downloads run on
//   a BACKGROUND thread (so the game never freezes while waiting). WebFetch connects the two:
//     1. Get() (background thread) puts a Request in a thread-safe queue, then waits on its "Done" signal.
//     2. Tick() (main thread, every frame) takes new requests from the queue, starts a UnityWebRequest for each,
//        and checks the running ones. When one finishes, it copies the bytes (or the error) into the Request and
//        sets the Done signal.
//     3. Get() wakes up and returns the bytes, or throws a WebFetchException with the reason.
//
// WHAT IT NEEDS
//   The game's UnityEngine.UnityWebRequestModule (the game uses it itself, so it is complete).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.Networking;

namespace ModMenu
{
    // A failed download. StatusCode is the web server's answer number (404 = not found), or 0 when there was none
    // (no internet, timeout...).
    internal sealed class WebFetchException : Exception
    {
        public readonly long StatusCode;
        public WebFetchException(string message, long statusCode) : base(message) { StatusCode = statusCode; }
    }

    internal static class WebFetch
    {
        private const int TimeoutSeconds = 60;
        private const ulong MaxBytes = 64UL * 1024 * 1024; // no mod is anywhere near 64 MB; stops a runaway download

        // One download: filled in by the main thread, waited for by the background thread.
        private sealed class Request
        {
            public string Url;
            public byte[] Result;
            public string Error;
            public long StatusCode;
            // A ManualResetEventSlim is a signal one thread can wait on (Wait) until another thread sets it (Set).
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public UnityWebRequest Web;
        }

        // ConcurrentQueue = a first-in-first-out list that two threads may use at the same time safely.
        private static readonly ConcurrentQueue<Request> Waiting = new ConcurrentQueue<Request>();
        private static readonly List<Request> Running = new List<Request>(); // only touched by the main thread

        // BACKGROUND THREAD: downloads one https:// address and returns its bytes. Throws WebFetchException on failure.
        public static byte[] Get(string url)
        {
            var request = new Request { Url = url };
            Waiting.Enqueue(request);
            // Wait a little longer than the download's own timeout, in case the game is busy loading for a moment.
            if (!request.Done.Wait(TimeSpan.FromSeconds(TimeoutSeconds + 30)))
                throw new WebFetchException("The download took too long.", 0);
            if (request.Error != null)
                throw new WebFetchException(request.Error, request.StatusCode);
            return request.Result;
        }

        // MAIN THREAD, every frame: start new downloads and finish the ones that are done.
        public static void Tick()
        {
            // 1. Start everything that was asked for since the last frame.
            while (Waiting.TryDequeue(out Request request))
            {
                try
                {
                    request.Web = UnityWebRequest.Get(request.Url);
                    request.Web.timeout = TimeoutSeconds;
                    request.Web.SendWebRequest();
                    Running.Add(request);
                }
                catch (Exception e)
                {
                    Finish(request, null, "Couldn't start the download: " + e.Message, 0);
                }
            }

            // 2. Check the running ones. Walking backwards lets us remove items while going through the list.
            for (int i = Running.Count - 1; i >= 0; i--)
            {
                Request request = Running[i];
                UnityWebRequest web = request.Web;
                try
                {
                    if (!web.isDone)
                    {
                        if (web.downloadedBytes > MaxBytes)
                        {
                            web.Abort();
                            Running.RemoveAt(i);
                            Finish(request, null, "The file is far too big to be a mod.", 0);
                        }
                        continue;
                    }
                    Running.RemoveAt(i);
                    if (web.result == UnityWebRequest.Result.Success)
                        Finish(request, ToBytes(web.downloadHandler.data), null, web.responseCode);
                    else
                        Finish(request, null, web.error ?? "the download failed", web.responseCode);
                }
                catch (Exception e)
                {
                    Running.RemoveAt(i);
                    Finish(request, null, "The download failed: " + e.Message, 0);
                }
            }
        }

        // Stores the outcome, frees Unity's request, and wakes the waiting background thread.
        private static void Finish(Request request, byte[] result, string error, long statusCode)
        {
            request.Result = result;
            request.Error = error;
            request.StatusCode = statusCode;
            try { request.Web?.Dispose(); } catch (Exception) { } // "?." = only if it isn't null
            request.Web = null;
            request.Done.Set();
        }

        // Copies the game's byte array (an Il2Cpp array living in the game's memory) into a normal C# byte array.
        private static byte[] ToBytes(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> data)
        {
            if (data == null)
                return new byte[0];
            var result = new byte[data.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = data[i];
            return result;
        }
    }
}
