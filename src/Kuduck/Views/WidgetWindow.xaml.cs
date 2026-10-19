using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kuduck.Controls;
using Kuduck.Services;

namespace Kuduck.Views
{
    public partial class WidgetWindow : Window
    {
        private readonly HomePage _home;
        private bool _positioned;

        public event EventHandler QuitRequested;

        public WidgetWindow()
        {
            InitializeComponent();
            LogoHost.Content = Ui.Logo(26);
            BackButton.Content = Ui.Icon("IcoBack", "TextBrush", 18);
            ThemeButton.Content = Ui.Icon("IcoMoon", "MutedBrush", 16);
            SettingsButton.Content = Ui.Icon("IcoGear", "MutedBrush", 16);
            HideButton.Content = Ui.Icon("IcoMinus", "MutedBrush", 16);
            UpdatePin();
            _home = new HomePage((key, files) => OpenTool(key, files, null));
            ShowHome();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    if (BackButton.Visibility == Visibility.Visible) ShowHome();
                    else Hide();
                    e.Handled = true;
                }
            };
        }

        public HomePage Home => _home;

        private void SetPage(UIElement page, string title, bool isHome)
        {
            Host.Content = page;
            TitleText.Text = title;
            TitleText.FontSize = isHome ? 17 : 15;
            BackButton.Visibility = isHome ? Visibility.Collapsed : Visibility.Visible;
            LogoHost.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ShowHome() => SetPage(_home, "KUDUCK", true);

        public void OpenTool(string key, IReadOnlyList<string> files, Dictionary<string, string> preset)
        {
            ToolInfo info = ToolRegistry.Find(key);
            if (info == null) { ShowHome(); return; }
            IToolView view = info.Create();
            if (preset != null) view.ApplyPreset(preset);
            if (files != null && files.Count > 0) view.AddFiles(files);
            SetPage((UIElement)view, view.Title, false);
        }

        public void ShowSettings()
        {
            SettingsPage page = new SettingsPage(
                () => Theme.Apply(SettingsStore.Current.DarkMode),
                UpdatePin,
                ShowAbout,
                () => QuitRequested?.Invoke(this, EventArgs.Empty));
            SetPage(page, page.Title, false);
        }

        public void ShowAbout()
        {
            AboutPage page = new AboutPage();
            SetPage(page, page.Title, false);
        }

        private void UpdatePin()
        {
            bool pinned = SettingsStore.Current.PinOnTop;
            Topmost = pinned;
            PinButton.Content = Ui.Icon("IcoPin", pinned ? "TextBrush" : "MutedBrush", 16);
            PinButton.SetResourceReference(Control.BackgroundProperty, pinned ? "BrandTintBrush" : "SurfaceBrush");
            PinButton.ToolTip = pinned ? "Kept on top (click to release)" : "Keep on top";
        }

        /// <summary>Shows the widget next to the duck, kept inside the screen.</summary>
        public void ShowNear(Rect duck)
        {
            if (!_positioned || !IsVisible)
            {
                double left = duck.Right + 8;
                double top = duck.Bottom - Height;
                double screenLeft = SystemParameters.VirtualScreenLeft;
                double screenTop = SystemParameters.VirtualScreenTop;
                double screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
                double screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
                Rect work = SystemParameters.WorkArea;
                if (duck.Left >= work.Left && duck.Right <= work.Right && duck.Top >= work.Top && duck.Bottom <= work.Bottom)
                {
                    screenLeft = work.Left; screenTop = work.Top; screenRight = work.Right; screenBottom = work.Bottom;
                }
                if (left + Width > screenRight) left = duck.Left - Width - 8;
                if (left < screenLeft) left = screenLeft + 8;
                if (top < screenTop) top = screenTop + 8;
                if (top + Height > screenBottom) top = screenBottom - Height - 8;
                Left = left;
                Top = top;
                _positioned = true;
            }
            Show();
            Activate();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Back_Click(object sender, RoutedEventArgs e) => ShowHome();

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            SettingsStore.Current.DarkMode = !SettingsStore.Current.DarkMode;
            SettingsStore.Save();
            Theme.Apply(SettingsStore.Current.DarkMode);
            UpdatePin();
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            SettingsStore.Current.PinOnTop = !SettingsStore.Current.PinOnTop;
            SettingsStore.Save();
            UpdatePin();
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

        private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Closing the widget (e.g. Alt+F4) just hides it; quitting is done from Settings or the tray.
            if (!App.IsQuitting)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }
    }
}
