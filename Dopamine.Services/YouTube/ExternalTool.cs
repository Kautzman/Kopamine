using System;

namespace Dopamine.Services.YouTube
{
    public enum ExternalTool
    {
        /// <summary>yt-dlp.exe: searches YouTube and downloads audio. Required.</summary>
        YtDlp = 0,

        /// <summary>ffmpeg.exe: converts to MP3 and trims. Required.</summary>
        Ffmpeg = 1,

        /// <summary>deno.exe: JavaScript runtime yt-dlp uses to solve YouTube challenges. Strongly recommended.</summary>
        Deno = 2
    }

    public class ExternalToolMissingException : Exception
    {
        public ExternalToolMissingException(ExternalTool tool, string executableName, string toolsFolder)
            : base(string.Format("{0} could not be found. Place it in '{1}' or add it to the PATH.", executableName, toolsFolder))
        {
            this.Tool = tool;
            this.ExecutableName = executableName;
            this.ToolsFolder = toolsFolder;
        }

        public ExternalTool Tool { get; }

        public string ExecutableName { get; }

        public string ToolsFolder { get; }
    }

    public class ExternalToolFailedException : Exception
    {
        public ExternalToolFailedException(string message, int exitCode)
            : base(message)
        {
            this.ExitCode = exitCode;
        }

        public int ExitCode { get; }
    }
}
