using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Kuduck.Services;

namespace Kuduck.Controls
{
    /// <summary>Drop zone + list of chosen files, with optional reordering.</summary>
    public class FileListControl : StackPanel
    {
        private readonly List<string> _files = new List<string>();
        private readonly StackPanel _list = new StackPanel();
        private readonly Grid _dropZone;
        private readonly Button _addMore;
        private readonly string _filter;
        private readonly bool _multiple;
        private readonly bool _reorder;
        private readonly Func<string, bool> _accept;
        private readonly string _accentKey;
        private readonly string _tintKey;
        private readonly string _what;

        public event EventHandler Changed;

        public IReadOnlyList<string> Files => _files;

        public FileListControl(string what, string filter, Func<string, bool> accept, bool multiple, bool reorder, bool pdfTheme)
        {
            _what = what;
            _filter = filter;
            _accept = accept;
            _multiple = multiple;
            _reorder = reorder;
            _accentKey = pdfTheme ? "PdfAccentBrush" : "ImageAccentBrush";
            _tintKey = pdfTheme ? "PdfTintBrush" : "ImageTintBrush";
            AllowDrop = true;
            Drop += OnDrop;
            DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };

            _dropZone = BuildDropZone();
            Children.Add(_dropZone);
            _list.Margin = new Thickness(0, 0, 0, 0);
            Children.Add(_list);

            _addMore = new Button
            {
                Height = 40,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Content = multiple ? "+ Add more" : "Choose a different file"
            };
            _addMore.SetResourceReference(Control.ForegroundProperty, _accentKey);
            _addMore.Click += (s, e) => Browse();
            Children.Add(_addMore);
            Refresh();
        }

        private Grid BuildDropZone()
        {
            Grid g = new Grid { Cursor = Cursors.Hand, MinHeight = 76, Background = Brushes.Transparent };
            Rectangle dash = new Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } };
            dash.SetResourceReference(Shape.StrokeProperty, _accentKey);
            dash.SetResourceReference(Shape.FillProperty, "SurfaceBrush");
            g.Children.Add(dash);

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14) , VerticalAlignment = VerticalAlignment.Center };
            Border iconBg = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Child = Ui.Icon("IcoUpload", _accentKey, 20) };
            iconBg.SetResourceReference(Border.BackgroundProperty, _tintKey);
            row.Children.Add(iconBg);
            StackPanel texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(Ui.Text("Drop " + _what + " here", 14, FontWeights.SemiBold));
            texts.Children.Add(Ui.Text("or click to browse", 12, null, "MutedBrush"));
            row.Children.Add(texts);
            g.Children.Add(row);
            g.MouseLeftButtonUp += (s, e) => Browse();
            return g;
        }

        public void Browse()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog { Filter = _filter, Multiselect = _multiple };
            Window owner = Window.GetWindow(this);
            bool? ok = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (ok == true) AddFiles(dlg.FileNames);
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
            e.Handled = true;
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            List<string> ok = FileHelpers.ExpandDropped(paths).Where(p => _accept == null || _accept(p)).ToList();
            if (ok.Count == 0) return;
            if (!_multiple)
            {
                _files.Clear();
                _files.Add(ok[0]);
            }
            else
            {
                foreach (string p in ok)
                    if (!_files.Contains(p, StringComparer.OrdinalIgnoreCase)) _files.Add(p);
            }
            Refresh();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            _files.Clear();
            Refresh();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Refresh()
        {
            _dropZone.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _addMore.Visibility = _files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            _list.Children.Clear();
            for (int i = 0; i < _files.Count; i++) _list.Children.Add(BuildRow(i));
        }

        private UIElement BuildRow(int index)
        {
            string path = _files[index];
            Border card = new Border { Style = Ui.GetStyle("Card"), Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 6, 4, 6) };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string ext = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            if (ext.Length > 4) ext = ext.Substring(0, 4);
            Border badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0) };
            badge.SetResourceReference(Border.BackgroundProperty, _tintKey);
            TextBlock bt = new TextBlock { Text = ext, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            bt.SetResourceReference(TextBlock.ForegroundProperty, _accentKey);
            badge.Child = bt;
            g.Children.Add(badge);

            StackPanel info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock name = Ui.Text(System.IO.Path.GetFileName(path), 13, FontWeights.SemiBold);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = path;
            info.Children.Add(name);
            long size = 0;
            try { size = new FileInfo(path).Length; } catch { }
            info.Children.Add(Ui.Text(FileHelpers.SizeText(size), 11.5, null, "MutedBrush"));
            Grid.SetColumn(info, 1);
            g.Children.Add(info);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (_reorder && _files.Count > 1)
            {
                Button up = Ui.IconButton("IcoUp", "Move up", (s, e) => Move(index, -1));
                up.IsEnabled = index > 0;
                Button down = Ui.IconButton("IcoDown", "Move down", (s, e) => Move(index, 1));
                down.IsEnabled = index < _files.Count - 1;
                buttons.Children.Add(up);
                buttons.Children.Add(down);
            }
            buttons.Children.Add(Ui.IconButton("IcoClose", "Remove " + System.IO.Path.GetFileName(path), (s, e) =>
            {
                _files.RemoveAt(index);
                Refresh();
                Changed?.Invoke(this, EventArgs.Empty);
            }));
            Grid.SetColumn(buttons, 2);
            g.Children.Add(buttons);
            card.Child = g;
            return card;
        }

        private void Move(int index, int delta)
        {
            int target = index + delta;
            if (target < 0 || target >= _files.Count) return;
            string item = _files[index];
            _files.RemoveAt(index);
            _files.Insert(target, item);
            Refresh();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
