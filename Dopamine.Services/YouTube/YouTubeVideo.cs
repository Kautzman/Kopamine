using System;

namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Immutable description of a single YouTube search result.
    /// </summary>
    public sealed class YouTubeVideo
    {
        private const string WatchUrlPrefix = "https://www.youtube.com/watch?v=";
        private const string ThumbnailUrlFormat = "https://i.ytimg.com/vi/{0}/mqdefault.jpg";

        public YouTubeVideo(string id, string title, string uploader, TimeSpan? duration, DateTime? uploadDate, bool isUploadDateApproximate)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A video id is required.", nameof(id));
            }

            this.Id = id;
            this.Title = title ?? string.Empty;
            this.Uploader = uploader ?? string.Empty;
            this.Duration = duration;
            this.UploadDate = uploadDate;
            this.IsUploadDateApproximate = uploadDate.HasValue && isUploadDateApproximate;
        }

        public string Id { get; }

        public string Title { get; }

        public string Uploader { get; }

        public TimeSpan? Duration { get; }

        public DateTime? UploadDate { get; }

        /// <summary>
        /// True when the upload date was derived from relative text such as "3 years ago".
        /// </summary>
        public bool IsUploadDateApproximate { get; }

        public string WebPageUrl => WatchUrlPrefix + this.Id;

        public string ThumbnailUrl => string.Format(ThumbnailUrlFormat, this.Id);

        public YouTubeVideo WithExactUploadDate(DateTime uploadDate)
        {
            return new YouTubeVideo(this.Id, this.Title, this.Uploader, this.Duration, uploadDate, false);
        }
    }
}
