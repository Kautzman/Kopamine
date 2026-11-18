using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public enum AudioDownloadStage
    {
        Starting = 0,
        Downloading = 1,
        Converting = 2,
        Completed = 3
    }

    public sealed class AudioDownloadProgress
    {
        public AudioDownloadProgress(AudioDownloadStage stage, double? percent)
        {
            this.Stage = stage;
            this.Percent = percent;
        }

        public AudioDownloadStage Stage { get; }

        /// <summary>
        /// 0-100, or null when the stage has no measurable progress.
        /// </summary>
        public double? Percent { get; }
    }

    public interface IYouTubeAudioService
    {
        /// <summary>
        /// Returns a direct, short-lived audio stream URL for instant previews, or null when cancelled.
        /// </summary>
        Task<Uri> GetPreviewStreamUriAsync(string videoId, CancellationToken cancellationToken);

        /// <summary>
        /// Downloads the best audio and converts it to MP3. Results are cached per video, and concurrent
        /// requests for the same video share one download. Cancelling only stops waiting (the method then
        /// returns null); the download itself continues so that a later request can reuse it.
        /// </summary>
        Task<string> DownloadMp3Async(string videoId, IProgress<AudioDownloadProgress> progress, CancellationToken cancellationToken);
    }
}
