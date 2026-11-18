namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Shows the trim + tag editor for a downloaded video.
    /// </summary>
    public interface IYouTubeTrimDialogService
    {
        /// <returns>True when the song was saved to the collection.</returns>
        bool ShowTrimDialog(YouTubeVideo video, string audioPath);
    }
}
