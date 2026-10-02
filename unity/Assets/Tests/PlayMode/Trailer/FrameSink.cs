// FrameSink.cs - frames read back from the GPU, piped raw into ffmpeg,
// which encodes the shot to an MP4 as they come. No window opens.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Unity.Collections;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public sealed class FrameSink : IDisposable
    {
        readonly Process process;
        readonly Stream input;
        readonly StringBuilder errors = new StringBuilder();
        readonly Thread drain;
        byte[] buffer;
        public int Frames { get; private set; }
        public string Path { get; }

        // RGB24 frames bottom row first, as ReadPixels leaves them.
        public FrameSink(string ffmpeg, string path, int width, int height, int fps)
        {
            Path = path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var psi = new ProcessStartInfo(ffmpeg,
                $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -s {width}x{height} -r {fps} -i - " +
                $"-vf vflip -c:v libx264 -preset medium -crf 12 -pix_fmt yuv420p -movflags +faststart \"{path}\"")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            process = Process.Start(psi);
            input = process.StandardInput.BaseStream;
            drain = new Thread(() =>
            {
                try
                {
                    string line;
                    while ((line = process.StandardError.ReadLine()) != null)
                        lock (errors) errors.AppendLine(line);
                }
                catch (Exception) { }
            }) { IsBackground = true };
            drain.Start();
        }

        public void Write(NativeArray<byte> frame)
        {
            if (buffer == null || buffer.Length != frame.Length) buffer = new byte[frame.Length];
            frame.CopyTo(buffer);
            input.Write(buffer, 0, buffer.Length);
            Frames++;
        }

        // What ffmpeg said, empty when all went well.
        public string Errors { get { lock (errors) return errors.ToString(); } }

        public int ExitCode { get; private set; } = -1;

        public void Dispose()
        {
            try { input.Flush(); input.Close(); } catch (IOException) { }
            if (!process.WaitForExit(600000)) process.Kill();
            ExitCode = process.ExitCode;
            drain.Join(2000);
            process.Dispose();
        }
    }
}
