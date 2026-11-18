using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kuduck.Controls;
using Kuduck.Services;
using Kuduck.Tools;

namespace Kuduck.Views
{
    /// <summary>Fill in a PDF form: every box, tick and choice of the form, then a filled-in copy is saved.</summary>
    public class FillFormPage : ToolPage
    {
        private readonly PasswordBox _password;
        private readonly StackPanel _fields = new StackPanel();
        private readonly TextBlock _note;
        private readonly Dictionary<string, Func<string>> _values = new Dictionary<string, Func<string>>();
        private string _loaded;

        public FillFormPage()
            : base("fillform", "Fill in a form", true, "a PDF form", FileHelpers.PdfFilter, FileHelpers.IsPdf, false, false, "Save the filled-in form")
        {
            _password = new PasswordBox();
            StackPanel passwordField = Ui.Field("PDF password (only if it has one)", _password);
            Button open = new Button { Content = "Open", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += async (s, e) => await LoadAsync(true);
            passwordField.Children.Add(open);
            Options.Children.Add(passwordField);
            _note = Ui.Hint("Add a PDF form to see its boxes here.");
            Options.Children.Add(_note);
            Options.Children.Add(_fields);
            Options.Children.Add(Ui.Hint("The filled-in form is saved next to it as a new file; the original isn't changed. It stays a form, so it can still be changed later."));
        }

        protected override async void OnFilesChanged() => await LoadAsync(false);

        private async Task LoadAsync(bool again)
        {
            if (_note == null) return; // called by the base page before this one is set up
            string file = Files.FirstOrDefault();
            if (file == null)
            {
                _fields.Children.Clear();
                _values.Clear();
                _loaded = null;
                _note.Text = Lang.T("Add a PDF form to see its boxes here.");
                return;
            }
            if (file == _loaded && !again) return;
            _loaded = file;
            string pw = _password.Password;
            List<FormField> fields;
            try
            {
                fields = await Task.Run(() => PdfForms.Read(file, pw));
            }
            catch (Exception ex)
            {
                _fields.Children.Clear();
                _values.Clear();
                _note.Text = FileHelpers.Friendly(ex);
                return;
            }
            _fields.Children.Clear();
            _values.Clear();
            _note.Text = fields.Count == 0
                ? Lang.T("This PDF has no boxes to fill in. To put your signature on it, use Sign.")
                : Lang.T("{0} to fill in:", Lang.T(fields.Count == 1 ? "{0} box" : "{0} boxes", fields.Count));
            foreach (FormField f in fields) _fields.Children.Add(Control(f));
            UpdateButton();
        }

        /// <summary>A box to type in, a tick, or a list, as the form has it.</summary>
        private UIElement Control(FormField f)
        {
            FrameworkElement control;
            switch (f.Kind)
            {
                case FormFieldKind.Tick:
                    CheckBox tick = new CheckBox { Content = f.Label, IsChecked = f.Value != "Off" && !string.IsNullOrEmpty(f.Value), Margin = new Thickness(0, 8, 0, 0), IsEnabled = !f.ReadOnly };
                    _values[f.Name] = () => tick.IsChecked == true ? "Yes" : "Off";
                    return tick;
                case FormFieldKind.Option:
                case FormFieldKind.Choice:
                    ComboBox list = new ComboBox { IsEnabled = !f.ReadOnly };
                    foreach (string c in f.Choices) list.Items.Add(c);
                    list.SelectedItem = f.Choices.Contains(f.Value) ? f.Value : null;
                    _values[f.Name] = () => list.SelectedItem as string ?? f.Value ?? string.Empty;
                    control = list;
                    break;
                default:
                    TextBox box = new TextBox { Text = f.Value ?? string.Empty, IsReadOnly = f.ReadOnly };
                    if (f.MultiLine)
                    {
                        box.AcceptsReturn = true;
                        box.TextWrapping = TextWrapping.Wrap;
                        box.Height = 70;
                        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    }
                    _values[f.Name] = () => box.Text;
                    control = box;
                    break;
            }
            StackPanel field = Ui.Field(f.Label, control);
            field.Margin = new Thickness(0, 6, 0, 0);
            return field;
        }

        protected override string Validate() =>
            _values.Count == 0 ? Lang.T("This PDF has no boxes to fill in.") : null;

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            string file = Files[0];
            string pw = _password.Password;
            Dictionary<string, string> values = _values.ToDictionary(p => p.Key, p => p.Value());
            progress.Report(Lang.T("Saving…"));
            string output = await Task.Run(() => PdfForms.Fill(file, pw, values));
            return new ToolResult
            {
                Outputs = new List<string> { output },
                Message = Lang.T("Saved {0}.", Path.GetFileName(output)),
                Summary = Lang.T("You filled in {0}.", Path.GetFileName(file))
            };
        }
    }

    /// <summary>
    /// Put your signature on a PDF: drawn with the mouse or a pen, typed in a handwriting font, or a picture of it. It's
    /// remembered for next time. Click on the page to place it.
    /// </summary>
    public class SignPage : ToolPage
    {
        private readonly ChipGroup _how;
        private readonly InkCanvas _ink;
        private readonly Border _inkFrame;
        private readonly TextBox _typed;
        private readonly TextBlock _typedLook;
        private readonly StackPanel _typedPanel;
        private readonly StackPanel _picturePanel;
        private readonly TextBlock _pictureName;
        private readonly CheckBox _remember;
        private readonly PasswordBox _password;
        private readonly TextBox _pageNumber;
        private readonly Slider _size;
        private readonly Grid _preview;
        private readonly Image _pageImage;
        private readonly Image _mark;
        private readonly TextBlock _pageNote;
        private byte[] _picture;
        private Windows.Data.Pdf.PdfDocument _doc;
        private string _docPath;
        private double _left = 0.6, _top = 0.85;
        private double _aspect = 3; // the signature's width / height

        public static string SavedSignature => System.IO.Path.Combine(AppPaths.DataDir, "signature.png");

        public SignPage()
            : base("sign", "Sign a PDF", true, "a PDF", FileHelpers.PdfFilter, FileHelpers.IsPdf, false, false, "Sign")
        {
            Options.Children.Add(Ui.Section("Your signature", "PdfAccentBrush"));
            _how = new ChipGroup(File.Exists(SavedSignature) ? 4 : 3);
            if (File.Exists(SavedSignature)) _how.Add("saved", "Saved");
            _how.Add("draw", "Draw").Add("type", "Type").Add("picture", "Picture");
            Options.Children.Add(_how);

            _ink = new InkCanvas { Height = 120, Background = Brushes.White };
            _ink.DefaultDrawingAttributes = new DrawingAttributes { Color = Color.FromRgb(0x10, 0x2A, 0x6B), Width = 2.6, Height = 2.6, FitToCurve = true };
            _ink.StrokeCollected += (s, e) => ShowMark();
            Button clear = new Button { Style = Ui.GetStyle("LinkButton"), Content = "Clear", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
            clear.Click += (s, e) => { _ink.Strokes.Clear(); ShowMark(); };
            StackPanel drawPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            _inkFrame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = _ink, ClipToBounds = true };
            _inkFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            drawPanel.Children.Add(Ui.Hint("Sign in the box with the mouse, a pen or your finger."));
            drawPanel.Children.Add(_inkFrame);
            drawPanel.Children.Add(clear);

            _typed = new TextBox();
            _typedLook = new TextBlock { FontFamily = new FontFamily("Segoe Script, Ink Free, Segoe Print"), FontSize = 34, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x2A, 0x6B)), Margin = new Thickness(0, 8, 0, 0), MinHeight = 50 };
            _typed.TextChanged += (s, e) => { _typedLook.Text = _typed.Text; ShowMark(); };
            _typedPanel = Ui.Field("Your name", _typed);
            _typedPanel.Margin = new Thickness(0, 8, 0, 0);
            _typedPanel.Children.Add(_typedLook);

            _pictureName = Ui.Hint("A photo or scan of your signature, best on white paper.");
            Button choose = new Button { Content = "Choose a picture…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            choose.Click += (s, e) => ChoosePicture();
            _picturePanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            _picturePanel.Children.Add(_pictureName);
            _picturePanel.Children.Add(choose);

            Options.Children.Add(drawPanel);
            Options.Children.Add(_typedPanel);
            Options.Children.Add(_picturePanel);
            _remember = new CheckBox { Content = "Remember my signature on this PC", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
            Options.Children.Add(_remember);
            Options.Children.Add(Ui.Hint("This puts a picture of your signature on the page, like signing a printout. It isn't a digital signature with a certificate."));

            Options.Children.Add(Ui.Section("Where it goes", "PdfAccentBrush"));
            _password = new PasswordBox();
            _password.PasswordChanged += async (s, e) => { _docPath = null; await ShowPageAsync(); };
            Options.Children.Add(Ui.Field("PDF password (only if it has one)", _password));
            _pageNumber = Ui.NumberBox(1, 70);
            _pageNumber.TextChanged += async (s, e) => await ShowPageAsync();
            _size = new Slider { Minimum = 0.12, Maximum = 0.6, Value = 0.3, Width = 180, VerticalAlignment = VerticalAlignment.Center };
            _size.ValueChanged += (s, e) => ShowMark();
            StackPanel where = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            where.Children.Add(Ui.Field("Page", _pageNumber));
            StackPanel sizeField = Ui.Field("Size", _size);
            sizeField.Margin = new Thickness(18, 0, 0, 0);
            where.Children.Add(sizeField);
            Options.Children.Add(where);

            _pageImage = new Image { Stretch = Stretch.Uniform };
            _mark = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            _preview = new Grid { Margin = new Thickness(0, 8, 0, 0), MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Cross, Background = Brushes.Transparent };
            _preview.Children.Add(_pageImage);
            _preview.Children.Add(_mark);
            _preview.MouseLeftButtonDown += (s, e) =>
            {
                Point at = e.GetPosition(_pageImage);
                if (_pageImage.ActualWidth <= 0) return;
                double w = _size.Value, h = w * PageAspect() / _aspect;
                _left = Math.Max(0, Math.Min(1 - w, at.X / _pageImage.ActualWidth - w / 2));
                _top = Math.Max(0, Math.Min(1 - h, at.Y / _pageImage.ActualHeight - h / 2));
                ShowMark();
            };
            _pageNote = Ui.Hint("Add a PDF, then click on the page where your signature goes.");
            Options.Children.Add(_pageNote);
            Options.Children.Add(new Border { BorderThickness = new Thickness(1), Child = _preview, HorizontalAlignment = HorizontalAlignment.Left }
                .Also(b => b.SetResourceReference(Border.BorderBrushProperty, "BorderBrush")));

            _how.Changed += (s, e) => ShowHow();
            _how.Value = File.Exists(SavedSignature) ? "saved" : "draw";
            ShowHow();
        }

        private void ShowHow()
        {
            string how = _how.Value;
            ((FrameworkElement)_inkFrame.Parent).Visibility = how == "draw" ? Visibility.Visible : Visibility.Collapsed;
            _typedPanel.Visibility = how == "type" ? Visibility.Visible : Visibility.Collapsed;
            _picturePanel.Visibility = how == "picture" ? Visibility.Visible : Visibility.Collapsed;
            _remember.Visibility = how == "saved" ? Visibility.Collapsed : Visibility.Visible;
            ShowMark();
        }

        private void ChoosePicture()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Filter = Lang.T("Pictures") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                // White paper becomes see-through, so only the ink shows on the page.
                using (ImageMagick.MagickImage img = ImageEngine.Load(dialog.FileName))
                {
                    img.ColorFuzz = new ImageMagick.Percentage(18);
                    img.Transparent(ImageMagick.MagickColors.White);
                    img.Trim();
                    img.ResetPage();
                    _picture = img.ToByteArray(ImageMagick.MagickFormat.Png32);
                }
                _pictureName.Text = System.IO.Path.GetFileName(dialog.FileName);
                ShowMark();
            }
            catch (Exception ex)
            {
                SetStatus(FileHelpers.Friendly(ex), "ErrorBrush");
            }
        }

        /// <summary>The signature as a PNG picture with a see-through background, or null when there's none yet.</summary>
        private byte[] Signature()
        {
            switch (_how.Value)
            {
                case "saved": return File.Exists(SavedSignature) ? File.ReadAllBytes(SavedSignature) : null;
                case "picture": return _picture;
                case "type":
                    if (string.IsNullOrWhiteSpace(_typed.Text)) return null;
                    FormattedText text = new FormattedText(_typed.Text.Trim(), System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(_typedLook.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 96, _typedLook.Foreground, 1.0);
                    return Render(text.Width + 24, text.Height + 12, dc => dc.DrawText(text, new Point(12, 6)));
                default:
                    if (_ink.Strokes.Count == 0) return null;
                    Rect bounds = _ink.Strokes.GetBounds();
                    bounds.Inflate(6, 6);
                    // Drawn at three times the size on screen, so it stays sharp when printed.
                    return Render(bounds.Width * 3, bounds.Height * 3, dc =>
                    {
                        dc.PushTransform(new ScaleTransform(3, 3));
                        dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                        _ink.Strokes.Draw(dc);
                        dc.Pop();
                        dc.Pop();
                    });
            }
        }

        private static byte[] Render(double width, double height, Action<DrawingContext> draw)
        {
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen()) draw(dc);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, (int)Math.Ceiling(height)), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }

        private double PageAspect() =>
            _pageImage.Source is BitmapSource b && b.PixelHeight > 0 ? (double)b.PixelWidth / b.PixelHeight : 0.707;

        /// <summary>Shows the signature where it will go on the page preview.</summary>
        private void ShowMark()
        {
            if (_mark == null || _how == null) return;
            byte[] png = Signature();
            if (png == null || _pageImage.ActualWidth <= 0)
            {
                _mark.Source = null;
                return;
            }
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            _aspect = image.PixelHeight > 0 ? (double)image.PixelWidth / image.PixelHeight : 3;
            double w = _size.Value, h = w * PageAspect() / _aspect;
            _left = Math.Min(_left, Math.Max(0, 1 - w));
            _top = Math.Min(_top, Math.Max(0, 1 - h));
            _mark.Source = image;
            _mark.Width = w * _pageImage.ActualWidth;
            _mark.Height = h * _pageImage.ActualHeight;
            _mark.Margin = new Thickness(_left * _pageImage.ActualWidth, _top * _pageImage.ActualHeight, 0, 0);
        }

        protected override async void OnFilesChanged() => await ShowPageAsync();

        private async Task ShowPageAsync()
        {
            if (_pageImage == null || _pageNote == null) return; // called by the base page before this one is set up
            string file = Files.FirstOrDefault();
            if (file == null)
            {
                _pageImage.Source = null;
                _mark.Source = null;
                _doc = null;
                _docPath = null;
                _pageNote.Text = Lang.T("Add a PDF, then click on the page where your signature goes.");
                return;
            }
            try
            {
                if (_docPath != file)
                {
                    _doc = await WinRt.OpenPdfAsync(file, _password.Password);
                    _docPath = file;
                }
                int page = Ui.ReadInt(_pageNumber, 1, 1, (int)_doc.PageCount);
                byte[] png = await WinRt.RenderPagePngAsync(_doc, (uint)(page - 1), 760);
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(png);
                image.EndInit();
                _pageImage.Source = image;
                _pageNote.Text = Lang.T("Page {0} of {1}. Click where your signature goes.", page, _doc.PageCount);
                await Dispatcher.InvokeAsync(ShowMark, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                _doc = null;
                _docPath = null;
                _pageImage.Source = null;
                _pageNote.Text = ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                    ? Lang.T("This PDF has a password. Type it in the password box.")
                    : FileHelpers.Friendly(ex);
            }
        }

        protected override string Validate()
        {
            if (Signature() == null)
                return _how.Value == "draw" ? Lang.T("Sign in the box first.") : _how.Value == "type" ? Lang.T("Type your name first.") : Lang.T("Choose a picture of your signature first.");
            if (_doc == null) return Lang.T("The PDF couldn't be opened.");
            return null;
        }

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            byte[] png = Signature();
            if (_how.Value != "saved" && _remember.IsChecked == true)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.DataDir);
                    File.WriteAllBytes(SavedSignature, png);
                }
                catch (IOException) { /* remembering is a nicety */ }
                catch (UnauthorizedAccessException) { }
            }
            string file = Files[0];
            string pw = _password.Password;
            int page = Ui.ReadInt(_pageNumber, 1, 1, (int)_doc.PageCount) - 1;
            double w = _size.Value, h = w * PageAspect() / _aspect;
            double left = _left, top = _top;
            progress.Report(Lang.T("Signing…"));
            string output = await Task.Run(() => PdfStamp.AddPicture(file, pw, page, png, left, top, w, h));
            return new ToolResult
            {
                Outputs = new List<string> { output },
                Message = Lang.T("Saved {0}.", System.IO.Path.GetFileName(output)),
                Summary = Lang.T("You signed {0}.", System.IO.Path.GetFileName(file))
            };
        }
    }

    internal static class ElementExtensions
    {
        public static T Also<T>(this T element, Action<T> action)
        {
            action(element);
            return element;
        }
    }
}
