using Dopamine.Core.IO;
using System;
using System.Collections.Generic;
using System.IO;

namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Finds yt-dlp, ffmpeg and deno. Search order: [ExecutionFolder]\Tools, [ExecutionFolder], then every folder on the PATH.
    /// Found paths are cached, but missing tools are re-checked on every call so a user can drop the
    /// executable in place and retry without restarting the application.
    /// </summary>
    public class ExternalToolLocator : IExternalToolLocator
    {
        private const string ToolsFolderName = "Tools";

        private readonly object cacheLock = new object();
        private readonly Dictionary<ExternalTool, string> cachedPaths = new Dictionary<ExternalTool, string>();

        public ExternalToolLocator()
        {
            this.ToolsFolder = Path.Combine(ApplicationPaths.ExecutionFolder, ToolsFolderName);
        }

        public string ToolsFolder { get; }

        public string GetExecutableName(ExternalTool tool)
        {
            switch (tool)
            {
                case ExternalTool.YtDlp:
                    return "yt-dlp.exe";
                case ExternalTool.Ffmpeg:
                    return "ffmpeg.exe";
                case ExternalTool.Deno:
                    return "deno.exe";
                default:
                    throw new ArgumentOutOfRangeException(nameof(tool));
            }
        }

        public string GetRequiredPath(ExternalTool tool)
        {
            string path;

            if (!this.TryLocate(tool, out path))
            {
                throw new ExternalToolMissingException(tool, this.GetExecutableName(tool), this.ToolsFolder);
            }

            return path;
        }

        public bool TryLocate(ExternalTool tool, out string path)
        {
            lock (this.cacheLock)
            {
                string cachedPath;

                if (this.cachedPaths.TryGetValue(tool, out cachedPath) && System.IO.File.Exists(cachedPath))
                {
                    path = cachedPath;
                    return true;
                }
            }

            string executableName = this.GetExecutableName(tool);

            foreach (string folder in this.GetCandidateFolders())
            {
                string candidate;

                try
                {
                    candidate = Path.Combine(folder, executableName);
                }
                catch (ArgumentException)
                {
                    // Malformed PATH entry
                    continue;
                }

                if (System.IO.File.Exists(candidate))
                {
                    lock (this.cacheLock)
                    {
                        this.cachedPaths[tool] = candidate;
                    }

                    path = candidate;
                    return true;
                }
            }

            path = null;
            return false;
        }

        private IEnumerable<string> GetCandidateFolders()
        {
            yield return this.ToolsFolder;
            yield return ApplicationPaths.ExecutionFolder;

            string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            foreach (string entry in pathVariable.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = entry.Trim().Trim('"');

                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }
    }
}
