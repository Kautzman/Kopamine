using Digimezzo.Foundation.Core.Utils;
using Dopamine.Services.YouTube;
using System;

namespace Dopamine.Utils
{
    /// <summary>
    /// Turns exceptions from the YouTube services into user-facing text.
    /// </summary>
    public static class YouTubeErrorMessages
    {
        public static string Describe(Exception exception)
        {
            ExternalToolMissingException missingTool = exception as ExternalToolMissingException;

            if (missingTool != null)
            {
                return ResourceUtils.GetString("Language_YouTube_Tool_Missing")
                    .Replace("{file}", missingTool.ExecutableName)
                    .Replace("{folder}", missingTool.ToolsFolder);
            }

            return exception != null ? exception.Message : string.Empty;
        }
    }
}
