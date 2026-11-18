using Digimezzo.Foundation.Core.Utils;
using Dopamine.Services.YouTube;
using Prism.Mvvm;
using System;
using System.Globalization;

namespace Dopamine.ViewModels.Common
{
    public enum PreviewState
    {
        Idle = 0,
        Loading = 1,
        Playing = 2
    }

    /// <summary>
    /// One row in the YouTube search results.
    /// </summary>
    public class YouTubeVideoItemViewModel : BindableBase
    {
        private const string ExactDateFormat = "MMM d, yyyy";
        private const string ApproximateDateFormat = "MMM yyyy";

        private YouTubeVideo video;
        private PreviewState previewState;
        private bool isDownloading;
        private double downloadPercent;
        private bool isDownloadIndeterminate;
        private string downloadStatusText;
        private bool isAddedToCollection;

        public YouTubeVideoItemViewModel(YouTubeVideo video)
        {
            this.video = video ?? throw new ArgumentNullException(nameof(video));
            this.DurationText = TimeCode.Format(video.Duration);
        }

        public YouTubeVideo Video => this.video;

        public string Id => this.video.Id;

        public string Title => this.video.Title;

        public string Uploader => this.video.Uploader;

        public string DurationText { get; }

        public string ThumbnailUrl => this.video.ThumbnailUrl;

        public string UploadDateText
        {
            get
            {
                if (!this.video.UploadDate.HasValue)
                {
                    return string.Empty;
                }

                return this.video.IsUploadDateApproximate
                    ? "~" + this.video.UploadDate.Value.ToString(ApproximateDateFormat, CultureInfo.CurrentCulture)
                    : this.video.UploadDate.Value.ToString(ExactDateFormat, CultureInfo.CurrentCulture);
            }
        }

        public bool HasUploadDate => this.video.UploadDate.HasValue;

        public PreviewState PreviewState
        {
            get { return this.previewState; }
            set
            {
                if (SetProperty<PreviewState>(ref this.previewState, value))
                {
                    RaisePropertyChanged(nameof(this.IsPreviewLoading));
                    RaisePropertyChanged(nameof(this.IsPreviewPlaying));
                    RaisePropertyChanged(nameof(this.IsPreviewIdle));
                }
            }
        }

        public bool IsPreviewLoading => this.previewState == PreviewState.Loading;

        public bool IsPreviewPlaying => this.previewState == PreviewState.Playing;

        public bool IsPreviewIdle => this.previewState == PreviewState.Idle;

        public bool IsDownloading
        {
            get { return this.isDownloading; }
            set
            {
                if (SetProperty<bool>(ref this.isDownloading, value))
                {
                    RaisePropertyChanged(nameof(this.ShowDownloadButton));
                }
            }
        }

        public double DownloadPercent
        {
            get { return this.downloadPercent; }
            private set { SetProperty<double>(ref this.downloadPercent, value); }
        }

        public bool IsDownloadIndeterminate
        {
            get { return this.isDownloadIndeterminate; }
            private set { SetProperty<bool>(ref this.isDownloadIndeterminate, value); }
        }

        public string DownloadStatusText
        {
            get { return this.downloadStatusText; }
            private set { SetProperty<string>(ref this.downloadStatusText, value); }
        }

        public bool IsAddedToCollection
        {
            get { return this.isAddedToCollection; }
            set
            {
                if (SetProperty<bool>(ref this.isAddedToCollection, value))
                {
                    RaisePropertyChanged(nameof(this.ShowDownloadButton));
                }
            }
        }

        public bool ShowDownloadButton => !this.isDownloading && !this.isAddedToCollection;

        public void ApplyUploadDate(DateTime uploadDate)
        {
            this.video = this.video.WithExactUploadDate(uploadDate);
            RaisePropertyChanged(nameof(this.UploadDateText));
            RaisePropertyChanged(nameof(this.HasUploadDate));
        }

        public void ApplyProgress(AudioDownloadProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            this.IsDownloadIndeterminate = !progress.Percent.HasValue;
            this.DownloadPercent = progress.Percent ?? 0;

            switch (progress.Stage)
            {
                case AudioDownloadStage.Downloading:
                    this.DownloadStatusText = string.Format(CultureInfo.CurrentCulture, "{0} {1:0}%", ResourceUtils.GetString("Language_YouTube_Downloading"), this.DownloadPercent);
                    break;
                case AudioDownloadStage.Converting:
                    this.DownloadStatusText = ResourceUtils.GetString("Language_YouTube_Converting");
                    break;
                case AudioDownloadStage.Completed:
                    this.DownloadStatusText = string.Empty;
                    break;
                default:
                    this.DownloadStatusText = ResourceUtils.GetString("Language_YouTube_Starting");
                    break;
            }
        }
    }
}
