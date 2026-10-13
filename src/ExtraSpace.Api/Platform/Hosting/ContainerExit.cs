namespace ExtraSpace.Api.Platform.Hosting;

internal static class ContainerExit
{
    /// <summary>
    /// In a container the app is process 1, and the kernel ignores the abort signal the runtime raises for an unhandled
    /// exception. A start-up failure would then leave a spinning process that ECS never replaces, so we exit ourselves.
    /// Outside a container this does nothing: tests, <c>dotnet run</c> and <c>dotnet ef</c> keep the default behaviour.
    /// </summary>
    public static void ExitOnUnhandledException()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
            return;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            // Type, message and stack only; the logger may not exist yet, so this goes straight to stderr.
            Console.Error.WriteLine($"Unhandled exception, exiting. {(e.ExceptionObject as Exception)?.ToString() ?? "Unknown error."}");
        }
        finally
        {
            Environment.Exit(1);   // even when stderr is closed
        }
    }
}
