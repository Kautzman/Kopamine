using Digimezzo.Foundation.Core.IO;
using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Utils;
using Dopamine.Services.Entities;
using Dopamine.Services.Folders;
using Dopamine.Services.YouTube;
using Dopamine.Utils;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Dopamine.ViewModels.Common
{
    /// <summary>
    /// Trim + tag editor shown after a YouTube download completes.
    /// </summary>
    public class YouTubeTrimViewModel : BindableBase, IDisposable
    {
        private const double MinimumSelectionSeconds = 1.0;
        private const double EndPreviewSeconds = 5.0;
        private const double PositionTolerance = 0.05;
        private static readonly TimeSpan PositionPollInterval = TimeSpan.FromMilliseconds(40);

        private readonly IAudioPreviewPlayer player;
        private readonly IYouTubeImportService importService;
        private readonly IFoldersService foldersService;
        private readonly ISongInfoParser songInfoParser;
        private readonly DispatcherTimer positionTimer;

        private YouTubeVideo video;
        private Uri audioUri;
        private string audioPath;
        private bool isDisposed;

        private double durationSeconds;
        private double startSeconds;
        private double endSeconds;
        private double positionSeconds;
        private bool isPlaying;

        private string title;
        private string artists;
        private string album;
        private string albumArtists;
        private string genres;
        private string year;
        private string trackNumber;
        private string comment;
        private bool useThumbnailAsCover = true;
        private string fileBaseName;
        private bool isFileNameOverridden;
        private ObservableCollection<FolderViewModel> folders;
        private FolderViewModel selectedFolder;
        private bool isLoadingFolders;
        private bool isSaving;
        private string errorMessage;

        public YouTubeTrimViewModel(IAudioPreviewPlayer player, IYouTubeImportService importService, IFoldersService foldersService, ISongInfoParser songInfoParser)
        {
            this.player = player;
            this.importService = importService;
            this.foldersService = foldersService;
            this.songInfoParser = songInfoParser;

            this.positionTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = PositionPollInterval };
            this.positionTimer.Tick += this.PositionTimer_Tick;

            this.PlayPauseCommand = new DelegateCommand(this.TogglePlayPause);
            this.SeekCommand = new DelegateCommand<object>(value => this.SeekTo(ToDouble(value)));
            this.SetStartToPlayheadCommand = new DelegateCommand(() => this.StartSeconds = this.PositionSeconds);
            this.SetEndToPlayheadCommand = new DelegateCommand(() => this.EndSeconds = this.PositionSeconds);
            this.PlayFromStartCommand = new DelegateCommand(() => this.PlayFrom(this.StartSeconds));
            this.PlayEndingCommand = new DelegateCommand(() => this.PlayFrom(Math.Max(this.StartSeconds, this.EndSeconds - EndPreviewSeconds)));
            this.ResetTrimCommand = new DelegateCommand(this.ResetTrim);
            this.OpenVideoCommand = new DelegateCommand(() => Actions.TryOpenLink(this.video?.WebPageUrl));
            this.ResetFileNameCommand = new DelegateCommand(this.ResetFileName);
        }

        public DelegateCommand PlayPauseCommand { get; }

        public DelegateCommand<object> SeekCommand { get; }

        public DelegateCommand SetStartToPlayheadCommand { get; }

        public DelegateCommand SetEndToPlayheadCommand { get; }

        public DelegateCommand PlayFromStartCommand { get; }

        public DelegateCommand PlayEndingCommand { get; }

        public DelegateCommand ResetTrimCommand { get; }

        public DelegateCommand OpenVideoCommand { get; }

        public DelegateCommand ResetFileNameCommand { get; }

        #region Source

        public string VideoTitle => this.video?.Title;

        public string VideoUploader => this.video?.Uploader;

        public string ThumbnailUrl => this.video?.ThumbnailUrl;

        #endregion

        #region Trimming

        public double DurationSeconds
        {
            get { return this.durationSeconds; }
            private set
            {
                if (SetProperty<double>(ref this.durationSeconds, Math.Max(0, value)))
                {
                    RaisePropertyChanged(nameof(this.DurationText));
                }
            }
        }

        public double StartSeconds
        {
            get { return this.startSeconds; }
            set
            {
                double upperLimit = Math.Max(0, this.endSeconds - MinimumSelectionSeconds);
                double clamped = Clamp(value, 0, upperLimit);

                if (SetProperty<double>(ref this.startSeconds, clamped))
                {
                    this.RaiseSelectionChanged();
                }
                else
                {
                    // The text box may hold an out-of-range value: always resync it
                    RaisePropertyChanged(nameof(this.StartText));
                }
            }
        }

        public double EndSeconds
        {
            get { return this.endSeconds; }
            set
            {
                double lowerLimit = Math.Min(this.durationSeconds, this.startSeconds + MinimumSelectionSeconds);
                double clamped = Clamp(value, lowerLimit, this.durationSeconds);

                if (SetProperty<double>(ref this.endSeconds, clamped))
                {
                    this.RaiseSelectionChanged();
                }
                else
                {
                    RaisePropertyChanged(nameof(this.EndText));
                }
            }
        }

        public double PositionSeconds
        {
            get { return this.positionSeconds; }
            private set
            {
                if (SetProperty<double>(ref this.positionSeconds, value))
                {
                    RaisePropertyChanged(nameof(this.PositionText));
                }
            }
        }

        public bool IsPlaying
        {
            get { return this.isPlaying; }
            private set { SetProperty<bool>(ref this.isPlaying, value); }
        }

        public string StartText
        {
            get { return TimeCode.Format(this.startSeconds, true); }
            set
            {
                double parsed;

                if (TimeCode.TryParse(value, out parsed))
                {
                    this.StartSeconds = parsed;
                }

                RaisePropertyChanged(nameof(this.StartText));
            }
        }

        public string EndText
        {
            get { return TimeCode.Format(this.endSeconds, true); }
            set
            {
                double parsed;

                if (TimeCode.TryParse(value, out parsed))
                {
                    this.EndSeconds = parsed;
                }

                RaisePropertyChanged(nameof(this.EndText));
            }
        }

        public string PositionText => TimeCode.Format(this.positionSeconds, true);

        public string DurationText => TimeCode.Format(this.durationSeconds, false);

        public string SelectionLengthText => TimeCode.Format(this.endSeconds - this.startSeconds, true);

        #endregion

        #region Metadata

        public string Title
        {
            get { return this.title; }
            set
            {
                if (SetProperty<string>(ref this.title, value) && !this.isFileNameOverridden)
                {
                    // Follow the title until the user types their own file name
                    this.SetFileBaseName(this.importService.SuggestFileBaseName(value));
                }
            }
        }

        public string Artists
        {
            get { return this.artists; }
            set { SetProperty<string>(ref this.artists, value); }
        }

        public string Album
        {
            get { return this.album; }
            set { SetProperty<string>(ref this.album, value); }
        }

        public string AlbumArtists
        {
            get { return this.albumArtists; }
            set { SetProperty<string>(ref this.albumArtists, value); }
        }

        public string Genres
        {
            get { return this.genres; }
            set { SetProperty<string>(ref this.genres, value); }
        }

        public string Year
        {
            get { return this.year; }
            set { SetProperty<string>(ref this.year, value); }
        }

        public string TrackNumber
        {
            get { return this.trackNumber; }
            set { SetProperty<string>(ref this.trackNumber, value); }
        }

        public string Comment
        {
            get { return this.comment; }
            set { SetProperty<string>(ref this.comment, value); }
        }

        public bool UseThumbnailAsCover
        {
            get { return this.useThumbnailAsCover; }
            set { SetProperty<bool>(ref this.useThumbnailAsCover, value); }
        }

        /// <summary>
        /// Editable file name, without extension. Defaults to the title with spaces removed.
        /// </summary>
        public string FileBaseName
        {
            get { return this.fileBaseName; }
            set
            {
                // Clearing the box goes back to following the title
                bool isOverride = !string.IsNullOrWhiteSpace(value)
                    && !string.Equals(value, this.importService.SuggestFileBaseName(this.title), StringComparison.Ordinal);

                this.SetFileBaseName(value);
                this.IsFileNameOverridden = isOverride;
            }
        }

        public bool IsFileNameOverridden
        {
            get { return this.isFileNameOverridden; }
            private set { SetProperty<bool>(ref this.isFileNameOverridden, value); }
        }

        /// <summary>
        /// The exact file name that will be written (before a " (2)" suffix is added for duplicates).
        /// </summary>
        public string FinalFileName => this.importService.BuildFileName(this.EffectiveFileBaseName);

        private string EffectiveFileBaseName => string.IsNullOrWhiteSpace(this.fileBaseName)
            ? this.importService.SuggestFileBaseName(this.title)
            : this.fileBaseName;

        #endregion

        #region Destination

        public ObservableCollection<FolderViewModel> Folders
        {
            get { return this.folders; }
            private set
            {
                if (SetProperty<ObservableCollection<FolderViewModel>>(ref this.folders, value))
                {
                    RaisePropertyChanged(nameof(this.HasNoFolders));
                }
            }
        }

        public FolderViewModel SelectedFolder
        {
            get { return this.selectedFolder; }
            set { SetProperty<FolderViewModel>(ref this.selectedFolder, value); }
        }

        public bool IsLoadingFolders
        {
            get { return this.isLoadingFolders; }
            private set
            {
                if (SetProperty<bool>(ref this.isLoadingFolders, value))
                {
                    RaisePropertyChanged(nameof(this.HasNoFolders));
                }
            }
        }

        public bool HasNoFolders => !this.isLoadingFolders && (this.folders == null || this.folders.Count == 0);

        #endregion

        #region State

        public bool IsSaving
        {
            get { return this.isSaving; }
            private set { SetProperty<bool>(ref this.isSaving, value); }
        }

        public string ErrorMessage
        {
            get { return this.errorMessage; }
            private set
            {
                if (SetProperty<string>(ref this.errorMessage, value))
                {
                    RaisePropertyChanged(nameof(this.HasError));
                }
            }
        }

        public bool HasError => !string.IsNullOrEmpty(this.errorMessage);

        #endregion

        public void Initialize(YouTubeVideo video, string audioPath)
        {
            this.video = video ?? throw new ArgumentNullException(nameof(video));
            this.audioPath = audioPath;
            this.audioUri = new Uri(audioPath, UriKind.Absolute);

            SongInfoGuess guess = this.songInfoParser.Parse(video);
            this.Title = guess.Title;
            this.Artists = guess.Artist;

            // Use the search duration until the player reports the real one
            double knownDuration = video.Duration.HasValue ? video.Duration.Value.TotalSeconds : 0;
            this.DurationSeconds = knownDuration;
            this.endSeconds = knownDuration;
            this.startSeconds = 0;
            this.RaiseSelectionChanged();

            RaisePropertyChanged(nameof(this.VideoTitle));
            RaisePropertyChanged(nameof(this.VideoUploader));
            RaisePropertyChanged(nameof(this.ThumbnailUrl));

            this.player.MediaOpened += this.Player_MediaOpened;
            this.player.MediaEnded += this.Player_MediaEnded;
            this.player.StateChanged += this.Player_StateChanged;
            this.player.Open(this.audioUri);
            this.positionTimer.Start();

            this.LoadFoldersAsync();
        }

        /// <summary>
        /// Invoked by the dialog's Save button. Returning false keeps the dialog open.
        /// </summary>
        public async Task<bool> SaveAsync()
        {
            YouTubeImportRequest request;

            if (!this.TryBuildRequest(out request))
            {
                return false;
            }

            this.IsSaving = true;
            this.ErrorMessage = null;

            // Release the source file before trimming/deleting it
            this.player.Close();

            try
            {
                string savedPath = await this.importService.ImportAsync(request, CancellationToken.None);

                // Null means the import was cancelled: keep the dialog open
                return savedPath != null;
            }
            catch (Exception ex)
            {
                LogClient.Error("Could not save YouTube video {0}. Exception: {1}", this.video.Id, ex.Message);
                this.ErrorMessage = ResourceUtils.GetString("Language_YouTube_Error_Saving").Replace("{error}", YouTubeErrorMessages.Describe(ex));

                // Let the user keep editing
                this.player.Open(this.audioUri);
                return false;
            }
            finally
            {
                this.IsSaving = false;
            }
        }

        public void Dispose()
        {
            if (this.isDisposed)
            {
                return;
            }

            this.isDisposed = true;
            this.positionTimer.Stop();
            this.positionTimer.Tick -= this.PositionTimer_Tick;
            this.player.MediaOpened -= this.Player_MediaOpened;
            this.player.MediaEnded -= this.Player_MediaEnded;
            this.player.StateChanged -= this.Player_StateChanged;

            if (this.player.Source == this.audioUri)
            {
                this.player.Close();
            }
        }

        private bool TryBuildRequest(out YouTubeImportRequest request)
        {
            request = null;
            uint? parsedYear;
            uint? parsedTrackNumber;

            if (string.IsNullOrWhiteSpace(this.Title))
            {
                this.ErrorMessage = ResourceUtils.GetString("Language_YouTube_Title_Required");
                return false;
            }

            if (!TryParseOptionalNumber(this.Year, out parsedYear))
            {
                this.ErrorMessage = ResourceUtils.GetString("Language_YouTube_Year_Must_Be_Number");
                return false;
            }

            if (!TryParseOptionalNumber(this.TrackNumber, out parsedTrackNumber))
            {
                this.ErrorMessage = ResourceUtils.GetString("Language_YouTube_Track_Number_Must_Be_Number");
                return false;
            }

            if (this.SelectedFolder == null)
            {
                this.ErrorMessage = ResourceUtils.GetString(this.HasNoFolders ? "Language_YouTube_No_Collection_Folders" : "Language_YouTube_Select_Folder");
                return false;
            }

            request = new YouTubeImportRequest
            {
                VideoId = this.video.Id,
                SourceAudioPath = this.audioPath,
                TrimStart = TimeSpan.FromSeconds(this.StartSeconds),
                TrimEnd = TimeSpan.FromSeconds(this.EndSeconds),
                DestinationFolder = this.SelectedFolder.Path,
                FileBaseName = this.EffectiveFileBaseName,
                UseThumbnailAsCover = this.UseThumbnailAsCover,
                Tags = new TrackTags
                {
                    Title = this.Title.Trim(),
                    Artists = this.Artists,
                    AlbumArtists = this.AlbumArtists,
                    Album = this.Album,
                    Genres = this.Genres,
                    Year = parsedYear,
                    TrackNumber = parsedTrackNumber,
                    Comment = this.Comment
                }
            };

            return true;
        }

        private async void LoadFoldersAsync()
        {
            this.IsLoadingFolders = true;

            try
            {
                IList<FolderViewModel> collectionFolders = await this.foldersService.GetFoldersAsync();
                this.Folders = new ObservableCollection<FolderViewModel>(collectionFolders);

                string lastUsedFolder = this.importService.LastUsedFolder;
                FolderViewModel preferredFolder = collectionFolders.FirstOrDefault(f => string.Equals(f.Path, lastUsedFolder, StringComparison.OrdinalIgnoreCase));

                this.SelectedFolder = preferredFolder ?? collectionFolders.FirstOrDefault();
            }
            catch (Exception ex)
            {
                LogClient.Error("Could not load the collection folders. Exception: {0}", ex.Message);
                this.Folders = new ObservableCollection<FolderViewModel>();
            }
            finally
            {
                this.IsLoadingFolders = false;
            }
        }

        private void SetFileBaseName(string value)
        {
            if (SetProperty<string>(ref this.fileBaseName, value, nameof(this.FileBaseName)))
            {
                RaisePropertyChanged(nameof(this.FinalFileName));
            }
        }

        private void ResetFileName()
        {
            this.IsFileNameOverridden = false;
            this.SetFileBaseName(this.importService.SuggestFileBaseName(this.title));
        }

        private void TogglePlayPause()
        {
            if (this.player.IsPlaying)
            {
                this.player.Pause();
                return;
            }

            // Play button previews the trimmed song: restart at the start marker when outside the selection
            bool isOutsideSelection = this.PositionSeconds < this.StartSeconds - PositionTolerance || this.PositionSeconds >= this.EndSeconds - PositionTolerance;
            this.PlayFrom(isOutsideSelection ? this.StartSeconds : this.PositionSeconds);
        }

        private void PlayFrom(double seconds)
        {
            if (this.player.Source != this.audioUri)
            {
                this.player.Open(this.audioUri);
            }

            this.SeekTo(seconds);
            this.player.Play();
        }

        private void SeekTo(double seconds)
        {
            double clamped = Clamp(seconds, 0, this.DurationSeconds);
            this.player.Position = TimeSpan.FromSeconds(clamped);
            this.PositionSeconds = clamped;
        }

        private void ResetTrim()
        {
            this.startSeconds = 0;
            this.endSeconds = this.DurationSeconds;
            this.RaiseSelectionChanged();
        }

        private void RaiseSelectionChanged()
        {
            RaisePropertyChanged(nameof(this.StartSeconds));
            RaisePropertyChanged(nameof(this.EndSeconds));
            RaisePropertyChanged(nameof(this.StartText));
            RaisePropertyChanged(nameof(this.EndText));
            RaisePropertyChanged(nameof(this.SelectionLengthText));
        }

        private void PositionTimer_Tick(object sender, EventArgs e)
        {
            if (!this.player.IsPlaying || this.player.Source != this.audioUri)
            {
                return;
            }

            double current = this.player.Position.TotalSeconds;

            // Stop at the end marker so playback previews exactly what will be saved
            if (current >= this.EndSeconds)
            {
                this.player.Pause();
                this.SeekTo(this.EndSeconds);
                return;
            }

            this.PositionSeconds = current;
        }

        private void Player_MediaOpened(object sender, EventArgs e)
        {
            TimeSpan? actualDuration = this.player.Duration;

            if (!actualDuration.HasValue || actualDuration.Value.TotalSeconds <= 0)
            {
                return;
            }

            bool endWasAtDuration = this.endSeconds >= this.durationSeconds - PositionTolerance;
            this.DurationSeconds = actualDuration.Value.TotalSeconds;

            if (endWasAtDuration || this.endSeconds > this.durationSeconds)
            {
                this.endSeconds = this.durationSeconds;
            }

            this.startSeconds = Math.Min(this.startSeconds, Math.Max(0, this.endSeconds - MinimumSelectionSeconds));
            this.RaiseSelectionChanged();
        }

        private void Player_MediaEnded(object sender, EventArgs e)
        {
            this.SeekTo(this.EndSeconds);
        }

        private void Player_StateChanged(object sender, EventArgs e)
        {
            this.IsPlaying = this.player.IsPlaying && this.player.Source == this.audioUri;
        }

        private static bool TryParseOptionalNumber(string text, out uint? number)
        {
            number = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            uint parsed;

            if (uint.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
            {
                number = parsed;
                return true;
            }

            return false;
        }

        private static double ToDouble(object value)
        {
            if (value is double)
            {
                return (double)value;
            }

            double parsed;

            return value != null && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (maximum < minimum)
            {
                return minimum;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
