using Digimezzo.Foundation.Core.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public class YtDlpAudioService : IYouTubeAudioService
    {
        private const string ProgressMarker = "[kopamine]";
        private const string ExtractAudioMarker = "[ExtractAudio]";
        private const string PreviewFormat = "bestaudio[ext=m4a]/bestaudio";

        private readonly IYtDlpClient ytDlpClient;
        private readonly IExternalToolLocator toolLocator;
        private readonly IYouTubeAudioCache cache;
        private readonly object inflightLock = new object();
        private readonly Dictionary<string, InflightDownload> inflightDownloads = new Dictionary<string, InflightDownload>();

        public YtDlpAudioService(IYtDlpClient ytDlpClient, IExternalToolLocator toolLocator, IYouTubeAudioCache cache)
        {
            this.ytDlpClient = ytDlpClient;
            this.toolLocator = toolLocator;
            this.cache = cache;
        }

        public async Task<Uri> GetPreviewStreamUriAsync(string videoId, CancellationToken cancellationToken)
        {
            List<string> arguments = new List<string>
            {
                "-f", PreviewFormat,
                "--get-url",
                "--no-playlist",
                "--no-warnings",
                ToWatchUrl(videoId)
            };

            ProcessResult result = await this.ytDlpClient.RunAsync(arguments, null, cancellationToken).ConfigureAwait(false);

            if (result.IsCancelled)
            {
                return null;
            }

            this.ytDlpClient.EnsureSuccess(result);

            using (StringReader reader = new StringReader(result.StandardOutput))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    Uri uri;

                    if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(line.Trim(), UriKind.Absolute, out uri))
                    {
                        return uri;
                    }
                }
            }

            throw new ExternalToolFailedException("yt-dlp did not return a stream URL.", result.ExitCode);
        }

        public async Task<string> DownloadMp3Async(string videoId, IProgress<AudioDownloadProgress> progress, CancellationToken cancellationToken)
        {
            string cachedPath;

            if (this.cache.TryGetCachedAudio(videoId, out cachedPath))
            {
                progress?.Report(new AudioDownloadProgress(AudioDownloadStage.Completed, 100));
                return cachedPath;
            }

            // Fail fast (on the caller's thread) when a required tool is missing.
            this.toolLocator.GetRequiredPath(ExternalTool.YtDlp);
            this.toolLocator.GetRequiredPath(ExternalTool.Ffmpeg);

            InflightDownload download;

            lock (this.inflightLock)
            {
                if (!this.inflightDownloads.TryGetValue(videoId, out download))
                {
                    download = new InflightDownload();
                    this.inflightDownloads[videoId] = download;
                    download.Completion = this.RunDownloadAsync(videoId, download);
                }
            }

            Action<AudioDownloadProgress> handler = p => progress?.Report(p);
            download.ProgressChanged += handler;

            try
            {
                if (download.LastProgress != null)
                {
                    handler(download.LastProgress);
                }

                return await WithCancellation(download.Completion, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                download.ProgressChanged -= handler;
            }
        }

        private async Task<string> RunDownloadAsync(string videoId, InflightDownload download)
        {
            try
            {
                // Let the caller attach its progress handler before work starts.
                await Task.Yield();

                download.Report(new AudioDownloadProgress(AudioDownloadStage.Starting, null));

                string outputTemplate = Path.Combine(this.cache.Folder, "%(id)s.%(ext)s");

                List<string> arguments = new List<string>
                {
                    "-f", "bestaudio/best",
                    "--extract-audio",
                    "--audio-format", "mp3",
                    "--audio-quality", "0",
                    "--ffmpeg-location", this.toolLocator.GetRequiredPath(ExternalTool.Ffmpeg),
                    "--no-playlist",
                    "--no-mtime",
                    "--newline",
                    "--progress-template", "download:" + ProgressMarker + " %(progress._percent_str)s",
                    "-o", outputTemplate,
                    ToWatchUrl(videoId)
                };

                Action<string> onOutputLine = line => ParseProgressLine(line, download);

                LogClient.Info("Downloading audio for YouTube video {0}", videoId);
                // Never cancelled: callers only stop waiting, so a later request can reuse this download.
                ProcessResult result = await this.ytDlpClient.RunAsync(arguments, onOutputLine, CancellationToken.None).ConfigureAwait(false);
                this.ytDlpClient.EnsureSuccess(result);

                string path;

                if (!this.cache.TryGetCachedAudio(videoId, out path))
                {
                    throw new ExternalToolFailedException("The download finished, but the MP3 file was not created.", result.ExitCode);
                }

                download.Report(new AudioDownloadProgress(AudioDownloadStage.Completed, 100));

                return path;
            }
            finally
            {
                lock (this.inflightLock)
                {
                    this.inflightDownloads.Remove(videoId);
                }
            }
        }

        private static void ParseProgressLine(string line, InflightDownload download)
        {
            if (line.StartsWith(ProgressMarker, StringComparison.Ordinal))
            {
                string percentText = line.Substring(ProgressMarker.Length).Trim().TrimEnd('%').Trim();
                double percent;

                if (double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                {
                    download.Report(new AudioDownloadProgress(AudioDownloadStage.Downloading, Math.Max(0, Math.Min(100, percent))));
                }
            }
            else if (line.StartsWith(ExtractAudioMarker, StringComparison.Ordinal))
            {
                download.Report(new AudioDownloadProgress(AudioDownloadStage.Converting, null));
            }
        }

        private static string ToWatchUrl(string videoId)
        {
            return "https://www.youtube.com/watch?v=" + videoId;
        }

        /// <summary>
        /// Awaits the task, or returns default(T) (null for reference types) as soon as the token is cancelled.
        /// </summary>
        private static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return await task.ConfigureAwait(false);
            }

            TaskCompletionSource<bool> cancelled = new TaskCompletionSource<bool>();

            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (task != await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false))
                {
                    return default(T);
                }
            }

            return await task.ConfigureAwait(false);
        }

        private sealed class InflightDownload
        {
            public Task<string> Completion { get; set; }

            public AudioDownloadProgress LastProgress { get; private set; }

            public event Action<AudioDownloadProgress> ProgressChanged;

            public void Report(AudioDownloadProgress progress)
            {
                this.LastProgress = progress;
                this.ProgressChanged?.Invoke(progress);
            }
        }
    }
}
