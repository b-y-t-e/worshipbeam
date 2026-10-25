using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Uwielbienia.App.Services;

/// <summary>Ends the application process.</summary>
public interface IApplicationShutdown
{
    void ShutDown();

    /// <summary>Zamyka aplikację i uruchamia ją od nowa (np. po zmianie języka).</summary>
    void Restart();
}

/// <summary>Closes through the desktop lifetime, so windows close and services dispose cleanly.</summary>
public sealed class DesktopApplicationShutdown : IApplicationShutdown
{
    public void ShutDown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    public void Restart()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        // nowa kopia rusza dopiero po zamknięciu tej — plan i ustawienia są już zapisane, usługi zwolnione
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) => System.Diagnostics.Process.Start(exe, args);
            desktop.Shutdown();
        }
        else
        {
            System.Diagnostics.Process.Start(exe, args);
            Environment.Exit(0);
        }
    }
}
