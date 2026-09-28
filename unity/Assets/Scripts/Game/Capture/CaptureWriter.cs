// CaptureWriter.cs - PNG files from read-back pixels on a worker, one job
// after another, with each result handed back through Pump. ClipWriter
// writes a clip's frames as they come and its sheet after the last.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OpenKingdomsUnity.Game.Capture
{
    public static class CaptureWriter
    {
        static readonly object gate = new object();
        static Task chain = Task.CompletedTask;
        static readonly ConcurrentQueue<Action> done = new ConcurrentQueue<Action>();
        static int pending;

        public static int Pending => Volatile.Read(ref pending);

        // Runs work on a worker, after every job queued before it. then runs
        // on the main thread at the next Pump with its result or its error.
        public static void Run(Func<string> work, Action<string, Exception> then)
        {
            Interlocked.Increment(ref pending);
            lock (gate)
                chain = chain.ContinueWith(_ =>
                {
                    string result = null;
                    Exception error = null;
                    try { result = work(); }
                    catch (Exception e) { error = e; }
                    if (then != null) done.Enqueue(() => then(result, error));
                    Interlocked.Decrement(ref pending);
                }, TaskScheduler.Default);
        }

        // Main thread, once a frame.
        public static void Pump()
        {
            while (done.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // For tests and quitting: waits for every job, then pumps.
        public static bool WaitIdle(int milliseconds)
        {
            Task t;
            lock (gate) t = chain;
            bool ok = t.Wait(milliseconds) && Pending == 0;
            Pump();
            return ok;
        }

        // RGBA32 rows from the bottom up, as PNG bytes.
        public static byte[] Png(byte[] rgba, int w, int h) =>
            ImageConversion.EncodeArrayToPNG(rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h);

        // Writes one picture, and LATEST.txt when latestDir is set.
        public static void Shot(byte[] rgba, int w, int h, bool flip, string path, string latestDir, Action<string, Exception> then)
        {
            Run(() =>
            {
                if (flip) ContactSheet.FlipRows(rgba, w, h);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, Png(rgba, w, h));
                if (latestDir != null) CaptureFiles.WriteLatest(latestDir, path);
                return path;
            }, then);
        }
    }

    // One clip being written: frames in any order, then the sheet.
    public sealed class ClipWriter
    {
        public readonly string Folder, Sheet, LatestDir;
        public readonly int Frames;
        public int Received => Volatile.Read(ref received);
        public bool Finished { get; private set; }
        readonly byte[][] thumbs;
        readonly float[] times;
        ContactSheet.Plan plan;
        bool planned;
        int received;
        readonly Action<string, Exception> then;

        public ClipWriter(string folder, string sheet, int frames, string latestDir, Action<string, Exception> then)
        {
            Folder = folder;
            Sheet = sheet;
            Frames = Mathf.Max(1, frames);
            LatestDir = latestDir;
            thumbs = new byte[Frames][];
            times = new float[Frames];
            this.then = then;
            Directory.CreateDirectory(folder);
        }

        // Frame index (from 0) at seconds from the clip's start. The sheet is
        // written after the last one.
        public void Add(int index, byte[] rgba, int w, int h, bool flip, float seconds)
        {
            if (index < 0 || index >= Frames || Finished) return;
            if (!planned) { plan = ContactSheet.PlanFor(Frames, w, h); planned = true; }
            var p = plan;
            string path = Path.Combine(Folder, CaptureFiles.FrameName(index + 1));
            times[index] = seconds;
            CaptureWriter.Run(() =>
            {
                if (flip) ContactSheet.FlipRows(rgba, w, h);
                File.WriteAllBytes(path, CaptureWriter.Png(rgba, w, h));
                thumbs[index] = w == p.ThumbW && h == p.ThumbH ? rgba : ContactSheet.Shrink(rgba, w, h, p.ThumbW, p.ThumbH);
                return path;
            }, null);
            if (Interlocked.Increment(ref received) == Frames) Finish();
        }

        // A frame that could not be taken, so the sheet does not wait for it.
        public void Skip(int index)
        {
            if (index < 0 || index >= Frames || Finished) return;
            if (Interlocked.Increment(ref received) == Frames) Finish();
        }

        // Writes the sheet from the frames there are, when a clip is cut short.
        public void Finish()
        {
            if (Finished) return;
            Finished = true;
            if (!planned) { then?.Invoke(null, new InvalidOperationException("The clip has no frames.")); return; }
            var p = plan;
            CaptureWriter.Run(() =>
            {
                File.WriteAllBytes(Sheet, CaptureWriter.Png(ContactSheet.Compose(p, thumbs, times), p.Width, p.Height));
                if (LatestDir != null) CaptureFiles.WriteLatest(LatestDir, Sheet);
                return Sheet;
            }, then);
        }
    }
}
