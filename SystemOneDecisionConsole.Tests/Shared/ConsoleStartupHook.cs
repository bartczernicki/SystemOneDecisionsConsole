using System.Text;

// The runtime finds this global type through the child process's DOTNET_STARTUP_HOOKS.
// It uses only runtime dependencies and makes redirected Unicode output deterministic.
public static class StartupHook
{
    public static void Initialize()
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
    }
}
