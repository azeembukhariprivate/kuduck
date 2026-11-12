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
        private System.Windows.Forms.ToolStripMenuItem _duckItem;
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
            // An error behind the window can't be recovered from: say so, instead of just vanishing.
            AppDomain.CurrentDomain.UnhandledException += (s, x) =>
            {
                if (!x.IsTerminating) return;
                try
                {
                    MessageBox.Show(Lang.T("{0} ran into a problem it couldn't get past, and has to close.", "KUDUCK") + "\n\n" + (x.ExceptionObject as Exception)?.Message,
                        "KUDUCK", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception) { /* nothing more can be done */ }
            };
            SettingsStore.Load();
            LangUi.Start(); // KUDUCK in the chosen language
            Theme.Apply(SettingsStore.Current.DarkMode);

            _widget = new WidgetWindow();
            _widget.QuitRequested += (s, a) => Quit();
            _duck = new DuckWindow(_widget);
            // KUDUCK lives in the icon by the clock. The duck on the desktop is for those who switch it on.
            if (SettingsStore.Current.ShowDuck) _duck.Show();
            CreateTray();
            ListenForSecondInstance();

            bool startedByWindows = Array.IndexOf(e.Args, "--startup") >= 0;
            if (!startedByWindows) Dispatcher.BeginInvoke(new Action(ShowWidget), DispatcherPriority.ApplicationIdle);
            Dispatcher.BeginInvoke(new Action(ShowWelcome), DispatcherPriority.ApplicationIdle);

            UpdateService.AvailableChanged += (s, a) => OnUpdateAvailable();
            UpdateService.Start();
        }

        private void OnUpdateAvailable()
        {
            UpdateInfo info = UpdateService.Available;
            if (info == null) return;
            _widget.Home.ShowUpdate(info);
            if (_duck.IsVisible) _duck.ShowUpdate();
            AppSettings s = SettingsStore.Current;
            if (s.NotifiedVersion != info.Version)
            {
                s.NotifiedVersion = info.Version;
                SettingsStore.Save();
                try
                {
                    _tray.ShowBalloonTip(6000, Lang.T("KUDUCK {0} is ready", info.Version),
                        Lang.T("Click here to update."), System.Windows.Forms.ToolTipIcon.Info);
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
            // The menu by the clock is a Windows Forms one, so its words are put into the language here.
            menu.Items.Add(Lang.T("Open KUDUCK"), null, (s, a) => ShowWidget());
            _duckItem = new System.Windows.Forms.ToolStripMenuItem(Lang.T("Show the duck on the desktop")) { CheckOnClick = false };
            _duckItem.Click += (s, a) => SetDuckShown(!SettingsStore.Current.ShowDuck);
            menu.Items.Add(_duckItem);
            menu.Opening += (s, a) => _duckItem.Checked = SettingsStore.Current.ShowDuck;
            menu.Items.Add(Lang.T("Settings"), null, (s, a) => { _widget.ShowSettings(); ShowWidget(); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(Lang.T("Quit KUDUCK"), null, (s, a) => Quit());
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
                _tray.ShowBalloonTip(4000, Lang.T("KUDUCK is still here"),
                    Lang.T("Click the duck icon by the clock to open KUDUCK. The duck can be switched back on in Settings."),
                    System.Windows.Forms.ToolTipIcon.None);
            }
            catch { }
        }

        /// <summary>Once, the first time this version runs: says where KUDUCK lives now.</summary>
        private void ShowWelcome()
        {
            AppSettings s = SettingsStore.Current;
            if (s.WelcomeShown) return;
            s.WelcomeShown = true;
            SettingsStore.Save();
            // Someone who has used KUDUCK before had the duck on the desktop until now.
            bool usedBefore = s.LastAction != null || !double.IsNaN(s.DuckLeft);
            try
            {
                _tray.ShowBalloonTip(8000, usedBefore ? "The duck is now optional" : "KUDUCK is here, by the clock",
                    usedBefore
                        ? "KUDUCK now lives in this icon by the clock: click it to open. To have the duck on the desktop again, switch it on in Settings."
                        : "Click this icon to open the tools. If you'd like a duck on your desktop, switch it on in Settings.",
                    System.Windows.Forms.ToolTipIcon.None);
            }
            catch { }
        }

        /// <summary>Shows or hides the duck on the desktop, and remembers the choice.</summary>
        public void SetDuckShown(bool shown)
        {
            SettingsStore.Current.ShowDuck = shown;
            SettingsStore.Save();
            if (shown) _duck.Show();
            else _duck.Hide();
        }

        /// <summary>The duck's size was changed in Settings.</summary>
        public void ApplyDuckSize() => _duck.ApplySize();

        /// <summary>Opens the tools: beside the duck when it's on the desktop, otherwise in the corner by the clock.</summary>
        private void ShowWidget()
        {
            if (_duck.IsVisible) { _duck.OpenWidget(); return; }
            Rect work = SystemParameters.WorkArea;
            _widget.ShowNear(new Rect(work.Right - 8, work.Bottom - 8, 0, 0));
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
