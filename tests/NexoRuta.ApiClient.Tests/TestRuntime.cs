using System.Runtime.CompilerServices;

namespace NexoRuta.ApiClient.Tests;

internal static class TestRuntime
{
    [ModuleInitializer]
    internal static void ConfigureFileWatching()
    {
        // Many isolated test hosts share a Linux user; polling avoids exhausting its inotify quota.
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "1");
    }
}
