using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Kuduck.Controls
{
    /// <summary>Small factory helpers so pages can be built in code with the shared look.</summary>
    public static class Ui
    {
        public static Style GetStyle(string key) => (Style)Application.Current.FindResource(key);

        public static Geometry Geo(string key) => (Geometry)Application.Current.FindResource(key);

        /// <summary>A 24x24 line icon scaled to <paramref name="size"/>, coloured by a theme brush key.</summary>
        public static FrameworkElement Icon(string geometryKey, string brushKey, double size = 20, double stroke = 2)
        {
            Path path = new Path
            {
                Data = Geo(geometryKey),
                StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent
            };
            path.SetResourceReference(Shape.StrokeProperty, brushKey);
            Canvas canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
        }

        /// <summary>The duck logo. <paramref name="square"/> = yellow rounded square with an ink duck.</summary>
        public static FrameworkElement Logo(double size, bool square = true)
        {
            Canvas c = new Canvas { Width = 64, Height = 64 };
            if (square)
            {
                Rectangle bg = new Rectangle { Width = 64, Height = 64, RadiusX = 16, RadiusY = 16 };
                bg.SetResourceReference(Shape.FillProperty, "BrandBrush");
                c.Children.Add(bg);
            }
            string duck = square ? "InkBrush" : "BrandBrush";
            string eye = square ? "BrandBrush" : "InkBrush";
            c.Children.Add(DuckPart("DuckBody", duck, square ? null : "InkBrush"));
            c.Children.Add(DuckPart("DuckHead", duck, square ? null : "InkBrush"));
            c.Children.Add(DuckPart("DuckBeak", "BeakBrush", square ? null : "InkBrush"));
            c.Children.Add(DuckPart("DuckEye", eye, null));
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        private static Path DuckPart(string geo, string fillKey, string strokeKey)
        {
            Path p = new Path { Data = Geo(geo), StrokeLineJoin = PenLineJoin.Round };
            p.SetResourceReference(Shape.FillProperty, fillKey);
            if (strokeKey != null)
            {
                p.SetResourceReference(Shape.StrokeProperty, strokeKey);
                p.StrokeThickness = 1.2;
            }
            return p;
        }

        public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, string brushKey = "TextBrush")
        {
            TextBlock tb = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            if (weight.HasValue) tb.FontWeight = weight.Value;
            tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return tb;
        }

        public static TextBlock Label(string text) => new TextBlock { Text = text, Style = GetStyle("FieldLabel") };

        public static TextBlock Section(string text, string brushKey = "MutedBrush")
        {
            TextBlock tb = new TextBlock { Text = text.ToUpperInvariant(), Style = GetStyle("SectionLabel") };
            tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return tb;
        }

        public static TextBlock Hint(string text) => new TextBlock { Text = text, Style = GetStyle("Hint") };

        public static Button IconButton(string geometryKey, string tooltip, RoutedEventHandler click, string brushKey = "MutedBrush")
        {
            Button b = new Button { Style = GetStyle("IconButton"), Content = Icon(geometryKey, brushKey, 16), ToolTip = tooltip };
            System.Windows.Automation.AutomationProperties.SetName(b, tooltip);
            if (click != null) b.Click += click;
            return b;
        }

        public static TextBox NumberBox(int value, double width = double.NaN)
        {
            TextBox tb = new TextBox { Text = value.ToString(), Width = width };
            tb.PreviewTextInput += (s, e) =>
            {
                foreach (char ch in e.Text)
                    if (!char.IsDigit(ch)) { e.Handled = true; return; }
            };
            return tb;
        }

        public static int ReadInt(TextBox tb, int fallback, int min = 1, int max = 100000)
        {
            if (int.TryParse(tb.Text.Trim(), out int v)) return Math.Max(min, Math.Min(max, v));
            return fallback;
        }

        public static Grid Row(params UIElement[] children)
        {
            Grid g = new Grid();
            for (int i = 0; i < children.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                FrameworkElement fe = children[i] as FrameworkElement;
                if (fe != null && i < children.Length - 1) fe.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(children[i], i);
                g.Children.Add(children[i]);
            }
            return g;
        }

        public static StackPanel Field(string label, UIElement control)
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(Label(label));
            sp.Children.Add(control);
            return sp;
        }
    }
}
