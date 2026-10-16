namespace SqlAnywhereMcp.Config;

/// <summary>Provides the platform-specific default path to the configuration file.</summary>
internal static class ConfigPaths
{
    /// <summary>Returns the default configuration file path based on the current platform.</summary>
    public static string ConfigPath
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = string.IsNullOrEmpty(xdg)
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : xdg;
            return Path.Combine(baseDir, "sqlanywhere-mcp", "config.json");
        }
    }
}
