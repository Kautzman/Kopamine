using Digimezzo.Foundation.Core.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Trims with ffmpeg's stream copy. Every MP3 frame (~26 ms) is independently decodable,
    /// so cutting without re-encoding is frame-accurate and avoids a second generation of quality loss.
    /// </summary>
    public class FfmpegAudioTrimService : IAudioTrimService
    {
        private const string SecondsFormat = "0.000";

        private readonly IExternalToolLocator toolLocator;
        private readonly IProcessRunner processRunner;

        public FfmpegAudioTrimService(IExternalToolLocator toolLocator, IProcessRunner processRunner)
        {
            this.toolLocator = toolLocator;
            this.processRunner = processRunner;
        }

        public async Task<bool> TrimMp3Async(string inputPath, string outputPath, TimeSpan start, TimeSpan end, CancellationToken cancellationToken)
        {
            if (!System.IO.File.Exists(inputPath))
            {
                throw new FileNotFoundException("The downloaded audio file no longer exists.", inputPath);
            }

            if (start < TimeSpan.Zero)
            {
                start = TimeSpan.Zero;
            }

            if (end <= start)
            {
                throw new ArgumentException("The end of the selection must be after its start.", nameof(end));
            }

            string ffmpegPath = this.toolLocator.GetRequiredPath(ExternalTool.Ffmpeg);

            List<string> arguments = new List<string>
            {
                "-hide_banner",
                "-nostdin",
                "-loglevel", "error",
                "-y",
                "-ss", FormatSeconds(start),
                "-i", inputPath,
                "-t", FormatSeconds(end - start),
                "-map", "0:a:0",
                "-c:a", "copy",
                // Tags are written afterwards; drop whatever the source carried.
                "-map_metadata", "-1",
                "-id3v2_version", "3",
                outputPath
            };

            ProcessResult result = await this.processRunner.RunAsync(new ProcessRequest(ffmpegPath, arguments), cancellationToken).ConfigureAwait(false);

            if (result.IsCancelled)
            {
                return false;
            }

            if (!result.IsSuccess || !System.IO.File.Exists(outputPath))
            {
                LogClient.Error("ffmpeg trim failed with exit code {0}. {1}", result.ExitCode, result.StandardError);

                string lastErrorLine = result.StandardError
                    .Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault();

                throw new ExternalToolFailedException(string.IsNullOrEmpty(lastErrorLine) ? "ffmpeg could not trim the audio." : lastErrorLine, result.ExitCode);
            }

            return true;
        }

        private static string FormatSeconds(TimeSpan value)
        {
            return value.TotalSeconds.ToString(SecondsFormat, CultureInfo.InvariantCulture);
        }
    }
}
