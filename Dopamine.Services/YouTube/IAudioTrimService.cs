using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public interface IAudioTrimService
    {
        /// <summary>
        /// Writes the [start, end) range of an MP3 to a new MP3 file without re-encoding.
        /// Returns false when cancelled (no usable output is written).
        /// </summary>
        Task<bool> TrimMp3Async(string inputPath, string outputPath, TimeSpan start, TimeSpan end, CancellationToken cancellationToken);
    }
}
