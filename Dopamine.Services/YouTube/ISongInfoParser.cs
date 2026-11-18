namespace Dopamine.Services.YouTube
{
    public sealed class SongInfoGuess
    {
        public SongInfoGuess(string title, string artist)
        {
            this.Title = title ?? string.Empty;
            this.Artist = artist ?? string.Empty;
        }

        public string Title { get; }

        public string Artist { get; }
    }

    /// <summary>
    /// Guesses song title and artist from a video's title and uploader, to pre-fill the tag editor.
    /// </summary>
    public interface ISongInfoParser
    {
        SongInfoGuess Parse(YouTubeVideo video);
    }
}
