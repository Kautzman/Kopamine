using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public sealed class YouTubeImportRequest
    {
        public string VideoId { get; set; }

        public string SourceAudioPath { get; set; }

        public TimeSpan TrimStart { get; set; }

        public TimeSpan TrimEnd { get; set; }

        public string DestinationFolder { get; set; }

        public TrackTags Tags { get; set; }

        /// <summary>
        /// File name without extension. When empty, the name suggested for the title is used.
        /// </summary>
        public string FileBaseName { get; set; }

        public bool UseThumbnailAsCover { get; set; }
    }

    public interface IYouTubeImportService
    {
        /// <summary>
        /// The folder used by the last successful import during this session, or null.
        /// </summary>
        string LastUsedFolder { get; }

        /// <summary>
        /// Suggested file name (without extension) for a title: spaces removed, unsafe characters replaced.
        /// </summary>
        string SuggestFileBaseName(string title);

        /// <summary>
        /// The file name (with extension) that will be written for the given base name.
        /// </summary>
        string BuildFileName(string fileBaseName);

        /// <summary>
        /// Trims, tags and moves the audio into the destination folder, then refreshes the collection.
        /// Returns the full path of the new file, or null when cancelled (nothing is written to the collection).
        /// </summary>
        Task<string> ImportAsync(YouTubeImportRequest request, CancellationToken cancellationToken);
    }
}
