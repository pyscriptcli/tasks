using System;
using System.Threading;
using System.Windows;
using TasksApp.Helpers;

namespace TasksApp
{
    public partial class App : Application
    {
        private const string MutexName = "TasksApp_SingleInstance_Mutex_9988";
        private const string EventName = "TasksApp_Wakeup_Event_9988";

        private static Mutex? _instanceMutex;
        private static EventWaitHandle? _wakeupEvent;
        private static RegisteredWaitHandle? _registeredWait;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool isFirstInstance;
            _instanceMutex = new Mutex(true, MutexName, out isFirstInstance);

            if (!isFirstInstance)
            {
                // Another instance is already running -> signal it and exit (UR-049)
                try
                {
                    using var existingEvent = EventWaitHandle.OpenExisting(EventName);
                    existingEvent.Set();
                }
                catch
                {
                    // Fallback to Win32 window message/activation
                    IntPtr hwnd = NativeMethods.FindWindow(null, "Tasks");
                    if (hwnd != IntPtr.Zero)
                    {
                        if (NativeMethods.IsIconic(hwnd))
                        {
                            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                        }
                        NativeMethods.SetForegroundWindow(hwnd);
                    }
                }

                Shutdown();
                return;
            }

            try
            {
                _wakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                _registeredWait = ThreadPool.RegisterWaitForSingleObject(_wakeupEvent, (state, timedOut) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.RestoreAndBringToFront();
                        }
                    });
                }, null, -1, false);
            }
            catch { }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _registeredWait?.Unregister(null);
            _wakeupEvent?.Dispose();
            if (_instanceMutex != null)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch { }
                _instanceMutex.Dispose();
            }

            base.OnExit(e);
        }
    }
}
