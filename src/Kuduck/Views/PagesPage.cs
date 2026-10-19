using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuduck.Controls;
using Kuduck.Services;
using Kuduck.Tools;

namespace Kuduck.Views
{
    /// <summary>Rotate, reorder and delete pages of one PDF, with thumbnails.</summary>
    public class PagesPage : UserControl, IToolView
    {
        private class PageItem
        {
            public int Index;
            public int Rotate;
            public bool Selected;
            public BitmapImage Thumb;
        }

        private readonly FileListControl _file;
        private readonly PasswordBox _password;
        private readonly StackPanel _passwordPanel;
        private readonly UniformGrid _grid = new UniformGrid { Columns = 3 };
        private readonly StackPanel _toolbar;
        private readonly Button _save;
        private readonly TextBlock _status;
        private readonly Button _show;
        private List<PageItem> _pages = new List<PageItem>();
        private string _path;
        private string _lastOutput;

        public string Title => "Organise pages";

        public PagesPage()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel body = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
            _file = new FileListControl("a PDF", FileHelpers.PdfFilter, FileHelpers.IsPdf, false, false, true);
            _file.Changed += async (s, e) => await LoadAsync();
            body.Children.Add(_file);

            _password = new PasswordBox();
            _passwordPanel = Ui.Field("This PDF has a password — enter it to open", _password);
            Button open = new Button { Content = "Open", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += async (s, e) => await LoadAsync();
            _passwordPanel.Children.Add(open);
            _passwordPanel.Visibility = Visibility.Collapsed;
            body.Children.Add(_passwordPanel);

            _toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 6), Visibility = Visibility.Collapsed };
            _toolbar.Children.Add(ToolButton("IcoRotate", "Rotate selected right", (s, e) => RotateSelected(90)));
            _toolbar.Children.Add(ToolButton("IcoUp", "Move selected earlier", (s, e) => MoveSelected(-1)));
            _toolbar.Children.Add(ToolButton("IcoDown", "Move selected later", (s, e) => MoveSelected(1)));
            _toolbar.Children.Add(ToolButton("IcoTrash", "Delete selected", (s, e) => DeleteSelected()));
            Button all = new Button { Style = Ui.GetStyle("LinkButton"), Content = "Select all", Margin = new Thickness(8, 0, 0, 0), FontSize = 12 };
            all.Click += (s, e) =>
            {
                bool any = _pages.Any(p => !p.Selected);
                foreach (PageItem p in _pages) p.Selected = any;
                RenderGrid();
            };
            _toolbar.Children.Add(all);
            body.Children.Add(_toolbar);
            body.Children.Add(Ui.Hint("Click pages to select them, then rotate, move or delete."));
            body.Children.Add(_grid);

            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
            root.Children.Add(scroll);

            Border footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 10, 14, 12) };
            footer.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            footer.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            StackPanel fs = new StackPanel();
            _save = new Button { Style = Ui.GetStyle("PdfPrimaryButton"), Content = "Save as new PDF", IsEnabled = false };
            _save.Click += async (s, e) => await SaveAsync();
            fs.Children.Add(_save);
            Grid statusRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            _status = Ui.Text("", 12, null, "MutedBrush");
            statusRow.Children.Add(_status);
            _show = new Button { Style = Ui.GetStyle("LinkButton"), Content = "Show in folder", Visibility = Visibility.Collapsed, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
            _show.Click += (s, e) => { if (_lastOutput != null) AppPaths.ShowInExplorer(_lastOutput); };
            statusRow.Children.Add(_show);
            fs.Children.Add(statusRow);
            footer.Child = fs;
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);
            Content = root;
        }

        private static Button ToolButton(string icon, string tip, RoutedEventHandler click)
        {
            Button b = new Button { Width = 40, Height = 36, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
            b.Content = Ui.Icon(icon, "PdfAccentBrush", 18);
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.Click += click;
            return b;
        }

        public void AddFiles(IEnumerable<string> files) => _file.AddFiles(files);

        public void ApplyPreset(Dictionary<string, string> settings) { }

        private void SetStatus(string text, string brush = "MutedBrush")
        {
            _status.Text = text;
            _status.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }

        private async Task LoadAsync()
        {
            _pages = new List<PageItem>();
            _grid.Children.Clear();
            _toolbar.Visibility = Visibility.Collapsed;
            _save.IsEnabled = false;
            _show.Visibility = Visibility.Collapsed;
            if (_file.Files.Count == 0) { SetStatus(""); _passwordPanel.Visibility = Visibility.Collapsed; return; }
            string path = _file.Files[0];
            if (path != _path) { _password.Clear(); _passwordPanel.Visibility = Visibility.Collapsed; }
            _path = path;
            string pw = _password.Password;
            SetStatus("Loading pages…");
            try
            {
                global::Windows.Data.Pdf.PdfDocument doc = await WinRt.OpenPdfAsync(path, pw);
                _passwordPanel.Visibility = Visibility.Collapsed;
                uint count = doc.PageCount;
                for (uint i = 0; i < count; i++)
                {
                    byte[] png = await WinRt.RenderPagePngAsync(doc, i, 180);
                    _pages.Add(new PageItem { Index = (int)i, Thumb = ToBitmap(png) });
                    if (i % 6 == 5) { RenderGrid(); SetStatus($"Loading pages… {i + 1} of {count}"); }
                }
                RenderGrid();
                _toolbar.Visibility = Visibility.Visible;
                _save.IsEnabled = _pages.Count > 0;
                SetStatus($"{_pages.Count} pages.");
            }
            catch (Exception ex)
            {
                bool passwordProblem = ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                                       || (uint)ex.HResult == 0x8007052B; // ERROR_WRONG_PASSWORD
                if (passwordProblem)
                {
                    _passwordPanel.Visibility = Visibility.Visible;
                    SetStatus(string.IsNullOrEmpty(pw) ? "Enter the PDF's password." : "That password isn't right.", "ErrorBrush");
                }
                else
                {
                    SetStatus("Couldn't open this PDF: " + FileHelpers.Friendly(ex), "ErrorBrush");
                }
            }
        }

        private static BitmapImage ToBitmap(byte[] png)
        {
            BitmapImage img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(png);
            img.EndInit();
            img.Freeze();
            return img;
        }

        private void RenderGrid()
        {
            _grid.Children.Clear();
            for (int pos = 0; pos < _pages.Count; pos++)
            {
                PageItem item = _pages[pos];
                Image thumb = new Image { Source = item.Thumb, Stretch = Stretch.Uniform, Height = 120 };
                thumb.LayoutTransform = new RotateTransform(item.Rotate);
                StackPanel content = new StackPanel();
                Border paper = new Border { Height = 132, Padding = new Thickness(4), Child = thumb, Background = Brushes.White, CornerRadius = new CornerRadius(4) };
                content.Children.Add(paper);
                TextBlock num = Ui.Text((pos + 1).ToString(), 11.5, FontWeights.SemiBold, "MutedBrush");
                num.HorizontalAlignment = HorizontalAlignment.Center;
                num.Margin = new Thickness(0, 4, 0, 0);
                content.Children.Add(num);

                ToggleButton tile = new ToggleButton
                {
                    Content = content,
                    IsChecked = item.Selected,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(4),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Template = TileTemplate()
                };
                System.Windows.Automation.AutomationProperties.SetName(tile, $"Page {pos + 1}");
                tile.Checked += (s, e) => item.Selected = true;
                tile.Unchecked += (s, e) => item.Selected = false;
                _grid.Children.Add(tile);
            }
        }

        private static ControlTemplate _tileTemplate;

        private static ControlTemplate TileTemplate()
        {
            if (_tileTemplate != null) return _tileTemplate;
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            border.SetValue(Border.PaddingProperty, new Thickness(4));
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            ControlTemplate t = new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };
            Trigger on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("PdfAccentBrush"), "Bd"));
            on.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("PdfTintBrush"), "Bd"));
            t.Triggers.Add(on);
            _tileTemplate = t;
            return t;
        }

        private void RotateSelected(int degrees)
        {
            foreach (PageItem p in _pages.Where(p => p.Selected)) p.Rotate = (p.Rotate + degrees) % 360;
            RenderGrid();
        }

        private void MoveSelected(int delta)
        {
            if (delta < 0)
            {
                for (int i = 1; i < _pages.Count; i++)
                    if (_pages[i].Selected && !_pages[i - 1].Selected) Swap(i, i - 1);
            }
            else
            {
                for (int i = _pages.Count - 2; i >= 0; i--)
                    if (_pages[i].Selected && !_pages[i + 1].Selected) Swap(i, i + 1);
            }
            RenderGrid();
        }

        private void Swap(int a, int b)
        {
            PageItem t = _pages[a];
            _pages[a] = _pages[b];
            _pages[b] = t;
        }

        private void DeleteSelected()
        {
            int selected = _pages.Count(p => p.Selected);
            if (selected == 0) return;
            if (selected == _pages.Count) { SetStatus("A PDF needs at least one page.", "ErrorBrush"); return; }
            _pages = _pages.Where(p => !p.Selected).ToList();
            RenderGrid();
            SetStatus($"{_pages.Count} pages left. Nothing is changed until you save.");
        }

        private async Task SaveAsync()
        {
            if (_path == null || _pages.Count == 0) return;
            _save.IsEnabled = false;
            SetStatus("Saving…");
            try
            {
                string path = _path, pw = _password.Password;
                List<PageEdit> edits = _pages.Select(p => new PageEdit { Index = p.Index, Rotate = p.Rotate }).ToList();
                string output = await Task.Run(() => PdfEngine.Rearrange(path, pw, edits));
                _lastOutput = output;
                SetStatus($"Saved {Path.GetFileName(output)}.", "SuccessBrush");
                _show.Visibility = Visibility.Visible;
                History.Record("pages", "You reorganised the pages of a PDF.", null, new[] { output });
            }
            catch (Exception ex)
            {
                SetStatus(FileHelpers.Friendly(ex), "ErrorBrush");
            }
            finally
            {
                _save.IsEnabled = true;
            }
        }
    }
}
