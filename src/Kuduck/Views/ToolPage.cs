using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kuduck.Controls;
using Kuduck.Services;

namespace Kuduck.Views
{
    /// <summary>Anything the widget can show as a page.</summary>
    public interface IToolView
    {
        string Title { get; }
        void AddFiles(IEnumerable<string> files);
        void ApplyPreset(Dictionary<string, string> settings);
    }

    public class ToolResult
    {
        public List<string> Outputs { get; set; } = new List<string>();
        public string Message { get; set; }
        /// <summary>Short text for the duck, e.g. "You resized 3 photos to 1080 px (JPG)."</summary>
        public string Summary { get; set; }
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Standard tool layout: file list, options, and a footer with the main button and status.
    /// Subclasses add options to <see cref="Options"/> and implement <see cref="RunAsync"/>.
    /// </summary>
    public abstract class ToolPage : UserControl, IToolView
    {
        protected readonly string ToolKey;
        protected readonly FileListControl FileList;
        protected readonly StackPanel Options = new StackPanel();
        protected readonly Button RunButton;
        private readonly TextBlock _status;
        private readonly Button _showButton;
        private readonly int _minFiles;
        private readonly string _verb;
        private List<string> _lastOutputs = new List<string>();

        public string Title { get; }

        protected ToolPage(string toolKey, string title, bool pdfTheme, string what, string filter,
                           Func<string, bool> accept, bool multiple, bool reorder, string verb, int minFiles = 1)
        {
            ToolKey = toolKey;
            Title = title;
            _minFiles = minFiles;
            _verb = verb;

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel body = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
            FileList = new FileListControl(what, filter, accept, multiple, reorder, pdfTheme);
            FileList.Changed += (s, e) => UpdateButton();
            body.Children.Add(FileList);
            body.Children.Add(Options);
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
            root.Children.Add(scroll);

            Border footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 10, 14, 12) };
            footer.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            footer.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            StackPanel fs = new StackPanel();
            RunButton = new Button { Style = Ui.GetStyle(pdfTheme ? "PdfPrimaryButton" : "ImagePrimaryButton"), Content = verb };
            RunButton.Click += async (s, e) => await RunClicked();
            fs.Children.Add(RunButton);

            Grid statusRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _status = Ui.Text("", 12, null, "MutedBrush");
            _status.VerticalAlignment = VerticalAlignment.Center;
            statusRow.Children.Add(_status);
            _showButton = new Button { Style = Ui.GetStyle("LinkButton"), Content = "Show in folder", Visibility = Visibility.Collapsed, FontSize = 12 };
            _showButton.Click += (s, e) => { if (_lastOutputs.Count > 0) AppPaths.ShowInExplorer(_lastOutputs[0]); };
            Grid.SetColumn(_showButton, 1);
            statusRow.Children.Add(_showButton);
            fs.Children.Add(statusRow);
            footer.Child = fs;
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);

            Content = root;
            UpdateButton();
        }

        protected IReadOnlyList<string> Files => FileList.Files;

        public void AddFiles(IEnumerable<string> files) => FileList.AddFiles(files);

        public virtual void ApplyPreset(Dictionary<string, string> settings) { }

        protected virtual string ButtonText(int fileCount) =>
            fileCount > 1 ? $"{_verb} ({fileCount} files)" : _verb;

        protected void UpdateButton()
        {
            RunButton.Content = ButtonText(Files.Count);
            RunButton.IsEnabled = Files.Count >= _minFiles;
            OnFilesChanged();
        }

        protected virtual void OnFilesChanged() { }

        protected void SetStatus(string text, string brushKey = "MutedBrush")
        {
            _status.Text = text;
            _status.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }

        /// <summary>Checks the options before running. Return an error message, or null when fine.</summary>
        protected virtual string Validate() => null;

        protected abstract Task<ToolResult> RunAsync(IProgress<string> progress);

        private async Task RunClicked()
        {
            string problem = Validate();
            if (problem != null)
            {
                SetStatus(problem, "ErrorBrush");
                return;
            }
            RunButton.IsEnabled = false;
            _showButton.Visibility = Visibility.Collapsed;
            SetStatus("Working…");
            Progress<string> progress = new Progress<string>(t => SetStatus(t));
            try
            {
                ToolResult result = await RunAsync(progress);
                _lastOutputs = result.Outputs ?? new List<string>();
                SetStatus(result.Message ?? "Done.", "SuccessBrush");
                _showButton.Visibility = _lastOutputs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_lastOutputs.Count > 0)
                    History.Record(ToolKey, result.Summary ?? result.Message, result.Settings, _lastOutputs);
            }
            catch (Exception ex)
            {
                SetStatus(FileHelpers.Friendly(ex), "ErrorBrush");
            }
            finally
            {
                RunButton.IsEnabled = Files.Count >= _minFiles;
            }
        }

        protected static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

        protected static string Get(Dictionary<string, string> s, string key, string fallback) => History.Get(s, key, fallback);
    }
}
