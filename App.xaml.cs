using System.Threading;
using System.Windows;

namespace XinputG84HE;

public partial class App : Application
{
    // Named mutex purely to detect "is another copy of this exe already
    // running" - the name just needs to be globally unique to this app, it's
    // not guarding any actual shared resource. Kept alive for the process
    // lifetime (field, not a local) so it isn't garbage collected/released
    // early.
    private const string SingleInstanceMutexName = "XinputG84HE-9F2B6C1E-3B0E-4C7B-8B32-6E7B1A1B5D77-SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance already owns the mutex - most likely sitting
            // in the tray, since ShutdownMode.OnExplicitShutdown keeps it
            // alive after the window closes. Bail out before the StartupUri
            // window gets created rather than calling Application.Shutdown(),
            // since that only requests a graceful shutdown and the pending
            // StartupUri window creation in Run() would still go ahead.
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        // Emulation depends on raw HID access and a global keyboard hook,
        // both of which behave more reliably elevated - if launched without
        // admin rights, the app still opens, but device/hook errors surfacing
        // in the status log almost always mean "restart as Administrator".
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Release before disposing so the name is immediately free for the
        // next launch instead of waiting on finalization.
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
