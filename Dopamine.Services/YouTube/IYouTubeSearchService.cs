using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public sealed class UploadDateResolution
    {
        public UploadDateResolution(string videoId, DateTime uploadDate)
        {
            this.VideoId = videoId;
            this.UploadDate = uploadDate;
        }

        public string VideoId { get; }

        public DateTime UploadDate { get; }
    }

    public interface IYouTubeSearchService
    {
        /// <summary>
        /// Fast search. Upload dates are approximate (derived from "x years ago") at this stage.
        /// Returns an empty list when cancelled.
        /// </summary>
        Task<IList<YouTubeVideo>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);

        /// <summary>
        /// Looks up the exact upload date of each video and reports them as they arrive.
        /// Completes quietly when cancelled.
        /// </summary>
        Task ResolveUploadDatesAsync(IList<string> videoIds, IProgress<UploadDateResolution> progress, CancellationToken cancellationToken);
    }
}
