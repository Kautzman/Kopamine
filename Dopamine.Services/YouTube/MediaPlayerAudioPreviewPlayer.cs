using Digimezzo.Foundation.Core.Logging;
using Dopamine.Services.Playback;
using System;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Preview player built on WPF's MediaPlayer (Media Foundation), which handles both
    /// HTTPS stream URLs and local files. It also acts as the "audio focus" arbiter:
    /// starting a preview pauses the main player, and resuming the main player stops the preview.
    /// A new MediaPlayer is created for every source: WPF's MediaPlayer throws a NullReferenceException
    /// in MediaPlayerState.OpenMedia when an HTTP(S) source is opened on an instance that was closed before.
    /// </summary>
    public class MediaPlayerAudioPreviewPlayer : IAudioPreviewPlayer
    {
        private MediaPlayer mediaPlayer;
        private readonly Dispatcher dispatcher;
        private readonly IPlaybackService playbackService;
        private bool isPlaying;

        public MediaPlayerAudioPreviewPlayer(IPlaybackService playbackService)
        {
            this.playbackService = playbackService;
            this.dispatcher = Dispatcher.CurrentDispatcher;

            // When the user resumes or starts something in the main player, the preview yields.
            this.playbackService.PlaybackResumed += (_, __) => this.CloseOnUiThread();
            this.playbackService.PlaybackSuccess += (_, __) => this.CloseOnUiThread();
        }

        public Uri Source { get; private set; }

        public bool IsOpen => this.Source != null;

        public bool IsPlaying => this.isPlaying;

        public TimeSpan Position
        {
            get { return this.mediaPlayer != null ? this.mediaPlayer.Position : TimeSpan.Zero; }
            set
            {
                if (this.mediaPlayer != null)
                {
                    this.mediaPlayer.Position = value < TimeSpan.Zero ? TimeSpan.Zero : value;
                }
            }
        }

        /// <summary>
        /// Null until MediaOpened has fired for the current source.
        /// </summary>
        public TimeSpan? Duration => this.mediaPlayer != null && this.mediaPlayer.NaturalDuration.HasTimeSpan
            ? this.mediaPlayer.NaturalDuration.TimeSpan
            : (TimeSpan?)null;

        public event EventHandler MediaOpened;

        public event EventHandler MediaEnded;

        public event EventHandler<PreviewFailedEventArgs> MediaFailed;

        public event EventHandler StateChanged;

        public void Open(Uri source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            this.Close();

            // Fresh instance per source (see class remarks). Duration stays null until MediaOpened fires.
            MediaPlayer player = new MediaPlayer();
            this.AttachHandlers(player);
            this.mediaPlayer = player;
            this.Source = source;

            try
            {
                player.Open(source);
            }
            catch (Exception ex)
            {
                LogClient.Error("Could not open preview source '{0}'. Exception: {1}", source, ex.ToString());
                this.Close();
                throw;
            }

            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async void Play()
        {
            if (!this.IsOpen || this.mediaPlayer == null)
            {
                return;
            }

            this.mediaPlayer.Volume = Math.Max(0.0, Math.Min(1.0, this.playbackService.Volume));
            this.mediaPlayer.Play();
            this.SetIsPlaying(true);

            if (this.playbackService.IsPlaying)
            {
                try
                {
                    await this.playbackService.PlayOrPauseAsync();
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not pause the main player for a preview. Exception: {0}", ex.Message);
                }
            }
        }

        public void Pause()
        {
            if (!this.IsOpen || this.mediaPlayer == null)
            {
                return;
            }

            this.mediaPlayer.Pause();
            this.SetIsPlaying(false);
        }

        public void Close()
        {
            bool wasOpen = this.IsOpen;
            MediaPlayer player = this.mediaPlayer;

            if (player != null)
            {
                // Detach first so late events from this instance can never reach the next preview
                this.mediaPlayer = null;
                this.DetachHandlers(player);
                player.Stop();
                player.Close();
            }

            this.Source = null;
            this.isPlaying = false;

            if (wasOpen)
            {
                this.StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void AttachHandlers(MediaPlayer player)
        {
            player.MediaOpened += this.MediaPlayer_MediaOpened;
            player.MediaEnded += this.MediaPlayer_MediaEnded;
            player.MediaFailed += this.MediaPlayer_MediaFailed;
        }

        private void DetachHandlers(MediaPlayer player)
        {
            player.MediaOpened -= this.MediaPlayer_MediaOpened;
            player.MediaEnded -= this.MediaPlayer_MediaEnded;
            player.MediaFailed -= this.MediaPlayer_MediaFailed;
        }

        private void MediaPlayer_MediaOpened(object sender, EventArgs e)
        {
            if (sender == this.mediaPlayer)
            {
                this.MediaOpened?.Invoke(this, EventArgs.Empty);
            }
        }

        private void MediaPlayer_MediaEnded(object sender, EventArgs e)
        {
            if (sender != this.mediaPlayer)
            {
                return;
            }

            this.SetIsPlaying(false);
            this.MediaEnded?.Invoke(this, EventArgs.Empty);
        }

        private void MediaPlayer_MediaFailed(object sender, ExceptionEventArgs e)
        {
            if (sender != this.mediaPlayer)
            {
                return;
            }

            Uri failedSource = this.Source;
            string message = e.ErrorException != null ? e.ErrorException.Message : "Unknown error";
            LogClient.Warning("Preview playback failed for '{0}'. Exception: {1}", failedSource, message);

            this.Close();
            this.MediaFailed?.Invoke(this, new PreviewFailedEventArgs(failedSource, message));
        }

        private void CloseOnUiThread()
        {
            if (this.dispatcher.CheckAccess())
            {
                this.Close();
            }
            else
            {
                this.dispatcher.BeginInvoke(new Action(this.Close));
            }
        }

        private void SetIsPlaying(bool value)
        {
            if (this.isPlaying == value)
            {
                return;
            }

            this.isPlaying = value;
            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
