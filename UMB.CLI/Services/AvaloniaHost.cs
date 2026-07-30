using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using UMB.CLI.Views;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace UMB.CLI.Services
{
    // Avalonia's AppBuilder.Setup can only run once per process. This host starts it once
    // on a dedicated background STA thread running Dispatcher.UIThread.MainLoop, so multiple
    // sequential windows (series order, track order, etc.) can be shown without re-initializing.
    internal static class AvaloniaHost
    {
        private static readonly object _initLock = new();
        private static volatile bool _started;

        public static TResult ShowWindow<TWindow, TResult>(Func<TWindow> windowFactory, Func<TWindow, TResult> resultExtractor)
            where TWindow : Window
        {
            EnsureStarted();

            var tcs = new TaskCompletionSource<TResult>();
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var window = windowFactory();
                    window.Closed += (_, _) =>
                    {
                        try { tcs.TrySetResult(resultExtractor(window)); }
                        catch (Exception ex) { tcs.TrySetException(ex); }
                    };
                    window.Show();
                    ForceForeground(window);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task.GetAwaiter().GetResult();
        }

        // Windows blocks foreground activation from non-foreground processes (anti focus-stealing),
        // so a plain Show()/Activate() from this CLI host often leaves the window behind whatever
        // app the user is currently looking at. Briefly toggling Topmost yanks the window to the
        // front regardless.
        private static void ForceForeground(Window window)
        {
            try
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                var wasTopmost = window.Topmost;
                window.Topmost = true;
                window.Activate();
                window.Focus();
                if (!wasTopmost)
                    window.Topmost = false;
            }
            catch { /* best-effort: focus is nice-to-have */ }
        }

        private static void EnsureStarted()
        {
            if (_started) return;
            lock (_initLock)
            {
                if (_started) return;

                var ready = new ManualResetEventSlim(false);
                Exception startupError = null;

                var uiThread = new Thread(() =>
                {
                    try
                    {
                        AppBuilder.Configure<SeriesOrderApp>()
                            .UsePlatformDetect()
                            .SetupWithoutStarting();
                        ready.Set();
                        Dispatcher.UIThread.MainLoop(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        startupError = ex;
                        ready.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "Avalonia UI"
                };
                if (OperatingSystem.IsWindows())
                    uiThread.SetApartmentState(ApartmentState.STA);
                uiThread.Start();
                ready.Wait();

                if (startupError != null)
                    throw new InvalidOperationException("Failed to initialize Avalonia.", startupError);

                _started = true;
            }
        }
    }
}
