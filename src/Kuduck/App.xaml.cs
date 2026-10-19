using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Kuduck.Services;
using Kuduck.Views;

namespace Kuduck
{
    public partial class App : Application
    {
        private const string MutexName = "KUDUCK_SingleInstance";
        private const string ShowEventName = "KUDUCK_ShowWidget";

        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private System.Windows.Forms.NotifyIcon _tray;
        private WidgetWindow _widget;
        private DuckWindow _duck;

        public static bool IsQuitting { get; private set; }

        public static new App Current => (App)Application.Current;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, MutexName, out bool firstInstance);
            if (!firstInstance)
            {
                // KUDUCK is already running: ask it to show itself, then leave.
                try
                {
                    using (EventWaitHandle other = EventWaitHandle.OpenExisting(ShowEventName)) other.Set();
                }
                catch { }
                Shutdown();
                return;
            }

            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandled;
            SettingsStore.Load();
            Theme.Apply(SettingsStore.Current.DarkMode);

            _widget = new WidgetWindow();
            _widget.QuitRequested += (s, a) => Quit();
            _duck = new DuckWindow(_widget);
            _duck.Show();
            CreateTray();
            ListenForSecondInstance();

            bool startedByWindows = Array.IndexOf(e.Args, "--startup") >= 0;
            if (!startedByWindows) _duck.Dispatcher.BeginInvoke(new Action(() => _duck.OpenWidget()), DispatcherPriority.ApplicationIdle);

            UpdateService.AvailableChanged += (s, a) => OnUpdateAvailable();
            UpdateService.Start();
        }

        private void OnUpdateAvailable()
        {
            UpdateInfo info = UpdateService.Available;
            if (info == null) return;
            _widget.Home.ShowUpdate(info);
            _duck.ShowUpdate();
            AppSettings s = SettingsStore.Current;
            if (s.NotifiedVersion != info.Version)
            {
                s.NotifiedVersion = info.Version;
                SettingsStore.Save();
                try
                {
                    _tray.ShowBalloonTip(6000, $"KUDUCK {info.Version} is ready",
                        "Click here or on the duck to update.", System.Windows.Forms.ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void CreateTray()
        {
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Text = "KUDUCK",
                Visible = true
            };
            try { _tray.Icon = new System.Drawing.Icon(AppPaths.IconPath); }
            catch { _tray.Icon = System.Drawing.SystemIcons.Application; }

            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open KUDUCK", null, (s, a) => ShowWidget());
            menu.Items.Add("Show the duck", null, (s, a) => _duck.Show());
            menu.Items.Add("Settings", null, (s, a) => { _widget.ShowSettings(); ShowWidget(); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quit KUDUCK", null, (s, a) => Quit());
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (s, a) =>
            {
                if (a.Button == System.Windows.Forms.MouseButtons.Left) ShowWidget();
            };
            // Clicking the "new version" notification opens the widget, where the Update button is.
            _tray.BalloonTipClicked += (s, a) => { _widget.ShowHome(); ShowWidget(); };
        }

        public void ShowTrayHint()
        {
            try
            {
                _tray.ShowBalloonTip(4000, "KUDUCK is still here",
                    "Click the duck icon in the taskbar corner to open KUDUCK or bring the duck back.",
                    System.Windows.Forms.ToolTipIcon.None);
            }
            catch { }
        }

        private void ShowWidget()
        {
            if (!_duck.IsVisible) _duck.Show();
            _duck.OpenWidget();
        }

        private void ListenForSecondInstance()
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            Thread t = new Thread(() =>
            {
                while (true)
                {
                    try { _showEvent.WaitOne(); }
                    catch { return; }
                    if (IsQuitting) return;
                    Dispatcher.BeginInvoke(new Action(ShowWidget));
                }
            })
            { IsBackground = true, Name = "KUDUCK show listener" };
            t.Start();
        }

        public void Quit()
        {
            IsQuitting = true;
            SettingsStore.Save();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            try { _duck?.Close(); } catch { }
            try { _widget?.Close(); } catch { }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            MessageBox.Show("Something went wrong:\n\n" + FileHelpers.Friendly(e.Exception), "KUDUCK",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
