using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Kuduck.Controls;
using Kuduck.Services;

namespace Kuduck.Views
{
    public class HomePage : UserControl
    {
        private readonly Action<string, IReadOnlyList<string>> _open;
        private readonly StackPanel _recent = new StackPanel();
        private readonly Border _updateBanner;
        private readonly TextBlock _dropHint;

        public HomePage(Action<string, IReadOnlyList<string>> openTool)
        {
            _open = openTool;
            StackPanel body = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };

            _updateBanner = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
            _updateBanner.SetResourceReference(Border.BackgroundProperty, "BrandTintBrush");
            body.Children.Add(_updateBanner);

            // Drop zone
            Grid drop = new Grid { MinHeight = 64, AllowDrop = true, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            Rectangle dash = new Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } };
            dash.Stroke = new SolidColorBrush(Color.FromRgb(0xD9, 0xB4, 0x4A));
            dash.SetResourceReference(Shape.FillProperty, "SurfaceBrush");
            drop.Children.Add(dash);
            StackPanel dropRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center };
            Border iconBg = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Child = Ui.Icon("IcoUpload", "TextBrush", 18) };
            iconBg.SetResourceReference(Border.BackgroundProperty, "BrandTintBrush");
            dropRow.Children.Add(iconBg);
            StackPanel dropTexts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            dropTexts.Children.Add(Ui.Text("Drop files here", 14, FontWeights.SemiBold));
            _dropHint = Ui.Text("Images, PDFs or Word files — KUDUCK picks the tool", 12, null, "MutedBrush");
            dropTexts.Children.Add(_dropHint);
            dropRow.Children.Add(dropTexts);
            drop.Children.Add(dropRow);
            drop.Drop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) Route(paths);
            };
            drop.MouseLeftButtonUp += (s, e) => BrowseAndRoute();
            body.Children.Add(drop);

            body.Children.Add(Ui.Section("Image tools", "ImageAccentBrush"));
            body.Children.Add(Tiles(ToolRegistry.Image, "ImageAccentBrush"));
            body.Children.Add(Ui.Section("PDF tools", "PdfAccentBrush"));
            body.Children.Add(Tiles(ToolRegistry.Pdf, "PdfAccentBrush"));
            body.Children.Add(_recent);

            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
            RefreshRecent();
            History.Changed += (s, e) => Dispatcher.BeginInvoke(new Action(RefreshRecent));
        }

        private UIElement Tiles(List<ToolInfo> tools, string accent)
        {
            UniformGrid grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -6, 0) };
            foreach (ToolInfo t in tools)
            {
                StackPanel content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                FrameworkElement icon = Ui.Icon(t.Icon, accent, 20);
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                content.Children.Add(icon);
                TextBlock label = Ui.Text(t.Label, 12, FontWeights.SemiBold);
                label.TextAlignment = TextAlignment.Center;
                label.Margin = new Thickness(0, 5, 0, 0);
                content.Children.Add(label);
                Button b = new Button
                {
                    Content = content,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(4, 10, 4, 9),
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
                b.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
                string key = t.Key;
                b.Click += (s, e) => _open(key, null);
                grid.Children.Add(b);
            }
            return grid;
        }

        private void BrowseAndRoute()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Images, PDFs and Word files|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.heic;*.pdf;*.doc;*.docx|All files|*.*"
            };
            Window owner = Window.GetWindow(this);
            bool? ok = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (ok == true) Route(dlg.FileNames);
        }

        private void Route(IEnumerable<string> paths)
        {
            List<string> files = FileHelpers.ExpandDropped(paths).ToList();
            string tool = ToolRegistry.RouteFor(files);
            if (tool == null)
            {
                _dropHint.Text = "KUDUCK can't open that type of file.";
                return;
            }
            _open(tool, files);
        }

        public void ShowUpdate(UpdateInfo info)
        {
            if (info == null) return;
            StackPanel panel = new StackPanel();
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(Ui.Text($"KUDUCK {info.Version} is ready", 13, FontWeights.SemiBold));
            Button notes = new Button { Style = Ui.GetStyle("LinkButton"), Content = "What's new", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0) };
            notes.Click += (s, e) => AppPaths.OpenUrl(info.Url);
            texts.Children.Add(notes);
            g.Children.Add(texts);
            Button update = new Button { Style = Ui.GetStyle("InkButton"), Content = "Update now", Padding = new Thickness(14, 0, 14, 0) };
            Grid.SetColumn(update, 1);
            g.Children.Add(update);
            panel.Children.Add(g);
            TextBlock status = Ui.Text("", 12, null, "MutedBrush");
            status.Visibility = Visibility.Collapsed;
            panel.Children.Add(status);
            update.Click += async (s, e) => await RunUpdate(update, status);
            _updateBanner.Child = panel;
            _updateBanner.Visibility = Visibility.Visible;
        }

        /// <summary>Downloads and starts the update; used by the banner and by the duck's Update button.</summary>
        public static async System.Threading.Tasks.Task RunUpdate(Button button, TextBlock status)
        {
            if (button != null) button.IsEnabled = false;
            if (status != null)
            {
                status.Visibility = Visibility.Visible;
                status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            }
            Progress<string> progress = new Progress<string>(t => { if (status != null) status.Text = t; });
            try
            {
                await UpdateService.InstallAsync(progress);
            }
            catch (Exception ex)
            {
                if (status != null)
                {
                    status.Text = "Update failed: " + FileHelpers.Friendly(ex) + " You can download it from the release page instead.";
                    status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
                }
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        private void RefreshRecent()
        {
            _recent.Children.Clear();
            List<string> recent = History.ExistingRecent().Take(3).ToList();
            if (recent.Count == 0) return;
            _recent.Children.Add(Ui.Section("Recent"));
            foreach (string f in recent)
            {
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock name = Ui.Text(System.IO.Path.GetFileName(f), 13);
                name.TextWrapping = TextWrapping.NoWrap;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.VerticalAlignment = VerticalAlignment.Center;
                name.ToolTip = f;
                row.Children.Add(name);
                Button show = new Button { Style = Ui.GetStyle("LinkButton"), Content = "Show", FontSize = 12 };
                string file = f;
                show.Click += (s, e) => AppPaths.ShowInExplorer(file);
                Grid.SetColumn(show, 1);
                row.Children.Add(show);
                Border card = new Border { Style = Ui.GetStyle("Card"), Padding = new Thickness(10, 4, 4, 4), Margin = new Thickness(0, 0, 0, 6), Child = row };
                _recent.Children.Add(card);
            }
        }
    }
}
