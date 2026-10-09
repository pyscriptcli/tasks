using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TasksApp.Helpers;

namespace TasksApp.Services
{
    public class TrayIconManager : IDisposable
    {
        private readonly Window _mainWindow;
        private HwndSource? _hwndSource;
        private IntPtr _hIcon = IntPtr.Zero;
        private bool _isCreated;
        private readonly int _iconId = 1001;

        public event Action? OpenRequested;
        public event Action? ExitRequested;
        public event Action? NewTaskRequested;
        public event Action? ToggleRemindersRequested;

        private ContextMenu? _contextMenu;

        public TrayIconManager(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void Initialize()
        {
            // Create hidden message sink window for Shell_NotifyIcon
            var parameters = new HwndSourceParameters("TasksTraySink")
            {
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0
            };
            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(WndProc);

            LoadIcon();
            CreateTrayIcon();
            BuildContextMenu();
        }

        private void LoadIcon()
        {
            try
            {
                // Extract embedded sticky note icon directly from the running executable
                string? processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    uint count = NativeMethods.ExtractIconEx(processPath, 0, out _, out IntPtr hSmall, 1);
                    if (count > 0 && hSmall != IntPtr.Zero)
                    {
                        _hIcon = hSmall;
                        return;
                    }
                }
            }
            catch { }

            try
            {
                // Fallback to standard application icon
                _hIcon = NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IDI_APPLICATION);
            }
            catch { }
        }

        private void CreateTrayIcon()
        {
            if (_hwndSource == null) return;

            var nid = new NativeMethods.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = _hwndSource.Handle,
                uID = _iconId,
                uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
                uCallbackMessage = NativeMethods.WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "Tasks - To-Do & Daily Reminders"
            };

            _isCreated = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref nid);
        }

        public void ShowNotification(string title, string message)
        {
            if (_hwndSource == null || !_isCreated) return;

            var nid = new NativeMethods.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = _hwndSource.Handle,
                uID = _iconId,
                uFlags = NativeMethods.NIF_INFO,
                szInfoTitle = title,
                szInfo = message,
                dwInfoFlags = NativeMethods.NIIF_INFO
            };

            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref nid);
        }

        private void BuildContextMenu()
        {
            _contextMenu = new ContextMenu
            {
                BorderThickness = new Thickness(1),
                BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#003366")!,
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FFFCFB")!
            };

            var openItem = new MenuItem { Header = "Open Tasks", FontWeight = FontWeights.SemiBold };
            openItem.Click += (s, e) => OpenRequested?.Invoke();

            var newItem = new MenuItem { Header = "New Task" };
            newItem.Click += (s, e) => NewTaskRequested?.Invoke();

            var toggleRemindersItem = new MenuItem { Header = "Toggle Reminders" };
            toggleRemindersItem.Click += (s, e) => ToggleRemindersRequested?.Invoke();

            var exitItem = new MenuItem { Header = "Exit Tasks" };
            exitItem.Click += (s, e) => ExitRequested?.Invoke();

            _contextMenu.Items.Add(openItem);
            _contextMenu.Items.Add(newItem);
            _contextMenu.Items.Add(new Separator());
            _contextMenu.Items.Add(toggleRemindersItem);
            _contextMenu.Items.Add(new Separator());
            _contextMenu.Items.Add(exitItem);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_TRAYICON)
            {
                int mouseEvent = lParam.ToInt32();
                if (mouseEvent == NativeMethods.WM_LBUTTONUP || mouseEvent == NativeMethods.WM_LBUTTONDBLCLK)
                {
                    OpenRequested?.Invoke();
                    handled = true;
                }
                else if (mouseEvent == NativeMethods.WM_RBUTTONUP)
                {
                    if (_contextMenu != null)
                    {
                        _contextMenu.IsOpen = true;
                    }
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_isCreated && _hwndSource != null)
            {
                var nid = new NativeMethods.NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                    hWnd = _hwndSource.Handle,
                    uID = _iconId
                };
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref nid);
                _isCreated = false;
            }

            _hwndSource?.Dispose();
            _hwndSource = null;
        }
    }
}
