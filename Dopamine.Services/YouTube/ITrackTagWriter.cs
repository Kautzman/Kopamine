namespace Dopamine.Services.YouTube
{
    public sealed class TrackTags
    {
        public string Title { get; set; }

        /// <summary>Multiple values separated by ';' (same convention as the tag editor).</summary>
        public string Artists { get; set; }

        public string AlbumArtists { get; set; }

        public string Album { get; set; }

        public string Genres { get; set; }

        public uint? Year { get; set; }

        public uint? TrackNumber { get; set; }

        public string Comment { get; set; }

        /// <summary>JPEG bytes, or null for no cover.</summary>
        public byte[] Artwork { get; set; }
    }

    public interface ITrackTagWriter
    {
        void Write(string filePath, TrackTags tags);
    }
}
