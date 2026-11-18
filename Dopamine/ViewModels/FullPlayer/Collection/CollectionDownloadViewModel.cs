using Digimezzo.Foundation.Core.IO;
using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Utils;
using Dopamine.Services.Dialog;
using Dopamine.Services.YouTube;
using Dopamine.Utils;
using Dopamine.ViewModels.Common;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.ViewModels.FullPlayer.Collection
{
    /// <summary>
    /// The "Download" collection page: search YouTube, preview, download and hand off to the trim editor.
    /// </summary>
    public class CollectionDownloadViewModel : BindableBase
    {
        private const int MaximumResults = 30;
        private const int ErrorIconGlyph = 0xE711;
        private const int IconSize = 16;

        private readonly IYouTubeSearchService searchService;
        private readonly IYouTubeAudioService audioService;
        private readonly IYouTubeAudioCache audioCache;
        private readonly IAudioPreviewPlayer previewPlayer;
        private readonly IYouTubeTrimDialogService trimDialogService;
        private readonly IDialogService dialogService;

        private string searchText;
        private bool isSearching;
        private bool isDownloadInProgress;
        private string statusMessage;
        private ObservableCollection<YouTubeVideoItemViewModel> results = new ObservableCollection<YouTubeVideoItemViewModel>();

        private CancellationTokenSource searchCancellation;
        private CancellationTokenSource previewCancellation;
        private YouTubeVideoItemViewModel previewItem;
        private bool isPreviewFromLocalFile;

        public CollectionDownloadViewModel(IYouTubeSearchService searchService, IYouTubeAudioService audioService, IYouTubeAudioCache audioCache,
            IAudioPreviewPlayer previewPlayer, IYouTubeTrimDialogService trimDialogService, IDialogService dialogService)
        {
            this.searchService = searchService;
            this.audioService = audioService;
            this.audioCache = audioCache;
            this.previewPlayer = previewPlayer;
            this.trimDialogService = trimDialogService;
            this.dialogService = dialogService;

            this.SearchCommand = new DelegateCommand(async () => await this.SearchAsync(), () => !string.IsNullOrWhiteSpace(this.SearchText))
                .ObservesProperty(() => this.SearchText);

            this.PreviewCommand = new DelegateCommand<YouTubeVideoItemViewModel>(async item => await this.TogglePreviewAsync(item));

            this.DownloadCommand = new DelegateCommand<YouTubeVideoItemViewModel>(async item => await this.DownloadAsync(item), _ => !this.IsDownloadInProgress)
                .ObservesProperty(() => this.IsDownloadInProgress);

            this.OpenVideoCommand = new DelegateCommand<YouTubeVideoItemViewModel>(item =>
            {
                if (item != null)
                {
                    Actions.TryOpenLink(item.Video.WebPageUrl);
                }
            });

            this.UnloadedCommand = new DelegateCommand(this.StopPreview);

            this.StatusMessage = ResourceUtils.GetString("Language_YouTube_Search_Hint");
        }

        public DelegateCommand SearchCommand { get; }

        public DelegateCommand<YouTubeVideoItemViewModel> PreviewCommand { get; }

        public DelegateCommand<YouTubeVideoItemViewModel> DownloadCommand { get; }

        public DelegateCommand<YouTubeVideoItemViewModel> OpenVideoCommand { get; }

        public DelegateCommand UnloadedCommand { get; }

        public string SearchText
        {
            get { return this.searchText; }
            set { SetProperty<string>(ref this.searchText, value); }
        }

        public bool IsSearching
        {
            get { return this.isSearching; }
            private set { SetProperty<bool>(ref this.isSearching, value); }
        }

        public bool IsDownloadInProgress
        {
            get { return this.isDownloadInProgress; }
            private set { SetProperty<bool>(ref this.isDownloadInProgress, value); }
        }

        public string StatusMessage
        {
            get { return this.statusMessage; }
            private set
            {
                if (SetProperty<string>(ref this.statusMessage, value))
                {
                    RaisePropertyChanged(nameof(this.HasStatusMessage));
                }
            }
        }

        public bool HasStatusMessage => !string.IsNullOrEmpty(this.statusMessage);

        public ObservableCollection<YouTubeVideoItemViewModel> Results
        {
            get { return this.results; }
            private set { SetProperty<ObservableCollection<YouTubeVideoItemViewModel>>(ref this.results, value); }
        }

        private async Task SearchAsync()
        {
            string query = this.SearchText?.Trim();

            if (string.IsNullOrEmpty(query))
            {
                return;
            }

            this.searchCancellation?.Cancel();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            this.searchCancellation = cancellation;

            this.StopPreview();
            this.Results = new ObservableCollection<YouTubeVideoItemViewModel>();
            this.StatusMessage = null;
            this.IsSearching = true;

            try
            {
                IList<YouTubeVideo> videos = await this.searchService.SearchAsync(query, MaximumResults, cancellation.Token);

                if (cancellation.Token.IsCancellationRequested)
                {
                    // A newer search replaced this one
                    return;
                }

                List<YouTubeVideoItemViewModel> items = videos.Select(v => new YouTubeVideoItemViewModel(v)).ToList();
                this.Results = new ObservableCollection<YouTubeVideoItemViewModel>(items);
                this.IsSearching = false;

                if (items.Count == 0)
                {
                    this.StatusMessage = ResourceUtils.GetString("Language_YouTube_No_Results");
                    return;
                }

                await this.ResolveExactUploadDatesAsync(items, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // A newer search replaced this one
            }
            catch (Exception ex)
            {
                LogClient.Error("YouTube search for '{0}' failed. Exception: {1}", query, ex.Message);
                this.StatusMessage = ResourceUtils.GetString("Language_YouTube_Search_Failed").Replace("{error}", YouTubeErrorMessages.Describe(ex));
            }
            finally
            {
                if (cancellation == this.searchCancellation)
                {
                    this.IsSearching = false;
                }
            }
        }

        private Task ResolveExactUploadDatesAsync(IList<YouTubeVideoItemViewModel> items, CancellationToken cancellationToken)
        {
            Dictionary<string, YouTubeVideoItemViewModel> itemsById = new Dictionary<string, YouTubeVideoItemViewModel>();

            foreach (YouTubeVideoItemViewModel item in items)
            {
                itemsById[item.Id] = item;
            }

            // Progress<T> marshals each report back to the UI thread
            Progress<UploadDateResolution> progress = new Progress<UploadDateResolution>(resolution =>
            {
                YouTubeVideoItemViewModel item;

                if (!cancellationToken.IsCancellationRequested && itemsById.TryGetValue(resolution.VideoId, out item))
                {
                    item.ApplyUploadDate(resolution.UploadDate);
                }
            });

            return this.searchService.ResolveUploadDatesAsync(itemsById.Keys.ToList(), progress, cancellationToken);
        }

        private async Task TogglePreviewAsync(YouTubeVideoItemViewModel item)
        {
            if (item == null)
            {
                return;
            }

            bool wasPreviewingThisItem = this.previewItem == item;
            this.StopPreview();

            if (wasPreviewingThisItem)
            {
                return;
            }

            CancellationTokenSource cancellation = new CancellationTokenSource();
            this.previewCancellation = cancellation;
            this.previewItem = item;
            item.PreviewState = PreviewState.Loading;

            this.previewPlayer.MediaOpened += this.PreviewPlayer_MediaOpened;
            this.previewPlayer.MediaFailed += this.PreviewPlayer_MediaFailed;
            this.previewPlayer.MediaEnded += this.PreviewPlayer_MediaEnded;
            this.previewPlayer.StateChanged += this.PreviewPlayer_StateChanged;

            try
            {
                string cachedPath;

                if (this.TryGetLocalAudio(item, out cachedPath))
                {
                    this.PlayPreview(new Uri(cachedPath), true);
                    return;
                }

                Uri streamUri = await this.audioService.GetPreviewStreamUriAsync(item.Id, cancellation.Token);

                if (streamUri == null || cancellation.Token.IsCancellationRequested)
                {
                    // The preview was stopped while the stream address was being fetched
                    return;
                }

                this.PlayPreview(streamUri, false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ExternalToolMissingException ex)
            {
                this.StopPreview();
                this.ShowError(ex);
            }
            catch (Exception ex)
            {
                LogClient.Warning("Could not get a preview stream for {0}, falling back to a download. Exception: {1}", item.Id, ex.ToString());
                await this.PreviewFromDownloadAsync(item, cancellation.Token);
            }
        }

        private async Task PreviewFromDownloadAsync(YouTubeVideoItemViewModel item, CancellationToken cancellationToken)
        {
            try
            {
                item.PreviewState = PreviewState.Loading;
                string path = await this.audioService.DownloadMp3Async(item.Id, null, cancellationToken);

                if (path == null || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                this.PlayPreview(new Uri(path), true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogClient.Error("Preview failed for {0}. Exception: {1}", item.Id, ex.ToString());

                if (this.previewItem == item)
                {
                    this.StopPreview();
                }

                this.ShowError(ex);
            }
        }

        private bool TryGetLocalAudio(YouTubeVideoItemViewModel item, out string path)
        {
            // A previous download (or preview fallback) left the MP3 in the cache: play it instantly.
            return this.audioCache.TryGetCachedAudio(item.Id, out path);
        }

        private void PlayPreview(Uri source, bool isLocalFile)
        {
            this.isPreviewFromLocalFile = isLocalFile;
            this.previewPlayer.Open(source);
            this.previewPlayer.Play();
        }

        private void StopPreview()
        {
            this.previewCancellation?.Cancel();
            this.previewCancellation = null;

            this.previewPlayer.MediaOpened -= this.PreviewPlayer_MediaOpened;
            this.previewPlayer.MediaFailed -= this.PreviewPlayer_MediaFailed;
            this.previewPlayer.MediaEnded -= this.PreviewPlayer_MediaEnded;
            this.previewPlayer.StateChanged -= this.PreviewPlayer_StateChanged;

            if (this.previewItem != null)
            {
                this.previewItem.PreviewState = PreviewState.Idle;
                this.previewItem = null;
                this.previewPlayer.Close();
            }
        }

        private void PreviewPlayer_MediaOpened(object sender, EventArgs e)
        {
            if (this.previewItem != null)
            {
                this.previewItem.PreviewState = PreviewState.Playing;
            }
        }

        private async void PreviewPlayer_MediaFailed(object sender, PreviewFailedEventArgs e)
        {
            YouTubeVideoItemViewModel item = this.previewItem;

            if (item == null || this.previewCancellation == null)
            {
                return;
            }

            if (this.isPreviewFromLocalFile)
            {
                this.StopPreview();
                return;
            }

            // Media Foundation could not play the stream URL: download it and play the file instead.
            await this.PreviewFromDownloadAsync(item, this.previewCancellation.Token);
        }

        private void PreviewPlayer_MediaEnded(object sender, EventArgs e)
        {
            this.StopPreview();
        }

        private void PreviewPlayer_StateChanged(object sender, EventArgs e)
        {
            // The preview was closed elsewhere (e.g. the main player resumed)
            if (!this.previewPlayer.IsOpen && this.previewItem != null && this.previewItem.PreviewState == PreviewState.Playing)
            {
                this.StopPreview();
            }
        }

        private async Task DownloadAsync(YouTubeVideoItemViewModel item)
        {
            if (item == null || this.IsDownloadInProgress)
            {
                return;
            }

            this.StopPreview();
            this.IsDownloadInProgress = true;
            item.IsDownloading = true;
            item.ApplyProgress(new AudioDownloadProgress(AudioDownloadStage.Starting, null));

            try
            {
                Progress<AudioDownloadProgress> progress = new Progress<AudioDownloadProgress>(item.ApplyProgress);
                string audioPath = await this.audioService.DownloadMp3Async(item.Id, progress, CancellationToken.None);

                item.IsDownloading = false;

                if (audioPath == null)
                {
                    return;
                }

                if (this.trimDialogService.ShowTrimDialog(item.Video, audioPath))
                {
                    item.IsAddedToCollection = true;
                }
            }
            catch (Exception ex)
            {
                LogClient.Error("Download of YouTube video {0} failed. Exception: {1}", item.Id, ex.Message);
                this.ShowError(ex);
            }
            finally
            {
                item.IsDownloading = false;
                this.IsDownloadInProgress = false;
            }
        }

        private void ShowError(Exception ex)
        {
            this.dialogService.ShowNotification(
                ErrorIconGlyph,
                IconSize,
                ResourceUtils.GetString("Language_Error"),
                ResourceUtils.GetString("Language_YouTube_Download_Failed").Replace("{error}", YouTubeErrorMessages.Describe(ex)),
                ResourceUtils.GetString("Language_Ok"),
                !(ex is ExternalToolMissingException),
                ResourceUtils.GetString("Language_Log_File"));
        }
    }
}
