namespace Dopamine.Services.YouTube
{
    public interface IExternalToolLocator
    {
        /// <summary>
        /// The preferred folder for bundled tools: a "Tools" folder next to the executable.
        /// </summary>
        string ToolsFolder { get; }

        bool TryLocate(ExternalTool tool, out string path);

        /// <summary>
        /// Returns the full path of the tool, or throws <see cref="ExternalToolMissingException"/>.
        /// </summary>
        string GetRequiredPath(ExternalTool tool);

        string GetExecutableName(ExternalTool tool);
    }
}
