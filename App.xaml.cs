using System;
using System.Threading;
using System.Windows;

namespace XinputG84HE;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "XinputG84HE-9F2B6C1E-3B0E-4C7B-8B32-6E7B1A1B5D77-SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Global exception logging / user notification
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nStack trace:\n{args.Exception.StackTrace}",
                "XinputG84HE Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Fatal application error:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                    "XinputG84HE Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "XinputG84HE is already running in the background or system tray.\n\nPlease check your system tray icons or close the existing process in Task Manager before launching again.",
                "XinputG84HE Already Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch { }
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
