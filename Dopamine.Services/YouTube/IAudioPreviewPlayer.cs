using System;

namespace Dopamine.Services.YouTube
{
    public class PreviewFailedEventArgs : EventArgs
    {
        public PreviewFailedEventArgs(Uri source, string message)
        {
            this.Source = source;
            this.Message = message;
        }

        public Uri Source { get; }

        public string Message { get; }
    }

    /// <summary>
    /// Lightweight player for auditioning YouTube audio, independent of the main playback queue.
    /// Must be used from the UI thread.
    /// </summary>
    public interface IAudioPreviewPlayer
    {
        Uri Source { get; }

        bool IsOpen { get; }

        bool IsPlaying { get; }

        TimeSpan Position { get; set; }

        TimeSpan? Duration { get; }

        void Open(Uri source);

        /// <summary>
        /// Starts playback, pausing the main player if it is playing.
        /// </summary>
        void Play();

        void Pause();

        /// <summary>
        /// Stops playback and releases the source (and any file lock).
        /// </summary>
        void Close();

        event EventHandler MediaOpened;

        event EventHandler MediaEnded;

        event EventHandler<PreviewFailedEventArgs> MediaFailed;

        /// <summary>
        /// Raised whenever IsPlaying or IsOpen changes.
        /// </summary>
        event EventHandler StateChanged;
    }
}
