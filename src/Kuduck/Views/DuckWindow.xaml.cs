using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Kuduck.Controls;
using Kuduck.Services;

namespace Kuduck.Views
{
    /// <summary>The little duck that sits on the desktop. Click = open KUDUCK, drag = move, drop files = open the right tool.</summary>
    public partial class DuckWindow : Window
    {
        private readonly WidgetWindow _widget;
        private readonly DispatcherTimer _hideBubble = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        private Point _pressPoint;
        private bool _pressed;
        private bool _dragged;

        public DuckWindow(WidgetWindow widget)
        {
            InitializeComponent();
            _widget = widget;
            DismissButton.Content = Ui.Icon("IcoClose", "MutedBrush", 12, 2.5);
            _hideBubble.Tick += (s, e) => { _hideBubble.Stop(); Bubble.Visibility = Visibility.Collapsed; };

            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) Bounce(); };
            Drop += OnDrop;
            ContextMenu = BuildMenu();

            History.Changed += (s, e) => Dispatcher.BeginInvoke(new Action(() => ShowBubbleFor(TimeSpan.FromSeconds(8))));
            Loaded += (s, e) =>
            {
                PlaceOnScreen();
                if (SettingsStore.Current.LastAction != null && !SettingsStore.Current.SuggestionDismissed)
                    ShowBubbleFor(TimeSpan.FromSeconds(8));
            };
            LocationChanged += (s, e) =>
            {
                SettingsStore.Current.DuckLeft = Left;
                SettingsStore.Current.DuckTop = Top;
            };
        }

        private void PlaceOnScreen()
        {
            Rect work = SystemParameters.WorkArea;
            double left = SettingsStore.Current.DuckLeft;
            double top = SettingsStore.Current.DuckTop;
            bool valid = !double.IsNaN(left) && !double.IsNaN(top)
                         && left > SystemParameters.VirtualScreenLeft - Width / 2
                         && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width / 2
                         && top > SystemParameters.VirtualScreenTop - Height / 2
                         && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height / 2;
            if (!valid)
            {
                // Default: bottom-right corner, above the taskbar.
                left = work.Right - Width - 24;
                top = work.Bottom - Height - 12;
            }
            Left = left;
            Top = top;
        }

        /// <summary>The duck's own area on screen (used to place the widget next to it).</summary>
        public Rect DuckBounds
        {
            get
            {
                Point topLeft = DuckArea.TranslatePoint(new Point(0, 0), this);
                return new Rect(Left + topLeft.X, Top + topLeft.Y, DuckArea.ActualWidth, DuckArea.ActualHeight);
            }
        }

        // ------------------------------------------------------------------ bubble

        private void UpdateBubbleText()
        {
            LastAction last = SettingsStore.Current.LastAction;
            if (last != null && ToolRegistry.Find(last.Tool) != null)
            {
                BubbleLabel.Text = "LAST TIME";
                BubbleText.Text = (last.Summary ?? "You used KUDUCK.") + " Do it again?";
                RepeatButton.Visibility = Visibility.Visible;
            }
            else
            {
                BubbleLabel.Text = "KUDUCK";
                BubbleText.Text = "Hi! Click me for image and PDF tools, or drop files on me.";
                RepeatButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowBubble()
        {
            if (_widget.IsVisible) return;
            UpdateBubbleText();
            Bubble.Visibility = Visibility.Visible;
            _hideBubble.Stop();
        }

        private void ShowBubbleFor(TimeSpan time)
        {
            ShowBubble();
            _hideBubble.Interval = time;
            _hideBubble.Start();
        }

        private void Duck_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!SettingsStore.Current.SuggestionDismissed || SettingsStore.Current.LastAction == null) ShowBubble();
        }

        private void Bubble_MouseEnter(object sender, MouseEventArgs e) => _hideBubble.Stop();

        private void Root_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Bubble.Visibility == Visibility.Visible)
            {
                _hideBubble.Interval = TimeSpan.FromSeconds(1.5);
                _hideBubble.Start();
            }
        }

        private void Dismiss_Click(object sender, RoutedEventArgs e)
        {
            SettingsStore.Current.SuggestionDismissed = true;
            SettingsStore.Save();
            Bubble.Visibility = Visibility.Collapsed;
        }

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            LastAction last = SettingsStore.Current.LastAction;
            Bubble.Visibility = Visibility.Collapsed;
            if (last == null) { OpenWidget(); return; }
            _widget.OpenTool(last.Tool, null, last.Settings);
            _widget.ShowNear(DuckBounds);
        }

        private void AllTools_Click(object sender, RoutedEventArgs e)
        {
            Bubble.Visibility = Visibility.Collapsed;
            _widget.ShowHome();
            _widget.ShowNear(DuckBounds);
        }

        // ------------------------------------------------------------------ click vs drag

        private void Duck_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressed = true;
            _dragged = false;
            _pressPoint = e.GetPosition(this);
            DuckArea.CaptureMouse();
        }

        private void Duck_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
            Vector moved = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(moved.X) > 4 || Math.Abs(moved.Y) > 4)
            {
                _dragged = true;
                _pressed = false;
                DuckArea.ReleaseMouseCapture();
                Bubble.Visibility = Visibility.Collapsed;
                try { DragMove(); } catch (InvalidOperationException) { }
                SettingsStore.Save();
            }
        }

        private void Duck_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DuckArea.ReleaseMouseCapture();
            if (_pressed && !_dragged) ToggleWidget();
            _pressed = false;
        }

        private void ToggleWidget()
        {
            if (_widget.IsVisible) { _widget.Hide(); return; }
            OpenWidget();
        }

        public void OpenWidget()
        {
            Bubble.Visibility = Visibility.Collapsed;
            _widget.ShowNear(DuckBounds);
        }

        // ------------------------------------------------------------------ drop files on the duck

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] paths)) return;
            List<string> files = FileHelpers.ExpandDropped(paths).ToList();
            string tool = ToolRegistry.RouteFor(files);
            if (tool == null) return;
            Bubble.Visibility = Visibility.Collapsed;
            _widget.OpenTool(tool, files, null);
            _widget.ShowNear(DuckBounds);
        }

        private void Bounce()
        {
            DoubleAnimation up = new DoubleAnimation(1, 1.12, TimeSpan.FromMilliseconds(140)) { AutoReverse = true };
            DuckScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, up);
            DuckScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.06, TimeSpan.FromMilliseconds(140)) { AutoReverse = true });
        }

        // ------------------------------------------------------------------ right-click menu

        private ContextMenu BuildMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(Item("Open KUDUCK", () => OpenWidget()));
            menu.Items.Add(Item("Settings", () => { _widget.ShowSettings(); _widget.ShowNear(DuckBounds); }));
            menu.Items.Add(Item("Hide the duck (open from the tray icon)", () => { Hide(); App.Current.ShowTrayHint(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Quit KUDUCK", () => App.Current.Quit()));
            return menu;
        }

        private static MenuItem Item(string header, Action action)
        {
            MenuItem item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            return item;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!App.IsQuitting)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }
    }
}
