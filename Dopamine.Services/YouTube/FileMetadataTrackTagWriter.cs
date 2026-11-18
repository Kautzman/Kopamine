using Dopamine.Data.Metadata;
using System;
using System.Globalization;

namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Writes tags through the application's existing <see cref="FileMetadata"/> (taglib-sharp) wrapper,
    /// so downloaded files are tagged exactly like files edited in the tag editor.
    /// </summary>
    public class FileMetadataTrackTagWriter : ITrackTagWriter
    {
        public void Write(string filePath, TrackTags tags)
        {
            if (tags == null)
            {
                throw new ArgumentNullException(nameof(tags));
            }

            FileMetadata metadata = new FileMetadata(filePath);

            metadata.Title = ToValue(tags.Title);

            if (HasText(tags.Artists))
            {
                metadata.Artists = ToValue(tags.Artists);
            }

            if (HasText(tags.AlbumArtists))
            {
                metadata.AlbumArtists = ToValue(tags.AlbumArtists);
            }

            if (HasText(tags.Album))
            {
                metadata.Album = ToValue(tags.Album);
            }

            if (HasText(tags.Genres))
            {
                metadata.Genres = ToValue(tags.Genres);
            }

            if (HasText(tags.Comment))
            {
                metadata.Comment = ToValue(tags.Comment);
            }

            if (tags.Year.HasValue)
            {
                metadata.Year = ToValue(tags.Year.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (tags.TrackNumber.HasValue)
            {
                metadata.TrackNumber = ToValue(tags.TrackNumber.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (tags.Artwork != null && tags.Artwork.Length > 0)
            {
                metadata.ArtworkData = new MetadataArtworkValue { Value = tags.Artwork };
            }

            metadata.Save();
        }

        private static bool HasText(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }

        private static MetadataValue ToValue(string value)
        {
            // Using the setter (rather than the constructor) marks the value as changed so FileMetadata writes it.
            return new MetadataValue { Value = (value ?? string.Empty).Trim() };
        }
    }
}
