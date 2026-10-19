using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Kuduck.Controls
{
    /// <summary>A row of mutually exclusive choice chips (segmented control).</summary>
    public class ChipGroup : UniformGrid
    {
        private static int _groupCounter;
        private readonly string _groupName;
        private readonly Dictionary<string, RadioButton> _chips = new Dictionary<string, RadioButton>();
        private bool _silent;

        public event EventHandler Changed;

        public ChipGroup(int columns)
        {
            Columns = columns;
            Margin = new Thickness(0, 0, -6, 0);
            _groupName = "chips" + (++_groupCounter);
        }

        public ChipGroup Add(string value, string title, string subtitle = null)
        {
            object content;
            if (subtitle == null)
            {
                content = title;
            }
            else
            {
                StackPanel sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
                TextBlock sub = Ui.Text(subtitle, 11, null, "MutedBrush");
                sub.TextWrapping = TextWrapping.NoWrap;
                sub.TextTrimming = TextTrimming.CharacterEllipsis;
                sp.Children.Add(sub);
                content = sp;
            }
            RadioButton rb = new RadioButton { Style = Ui.GetStyle("Chip"), Content = content, GroupName = _groupName, Tag = value };
            rb.Checked += (s, e) => { if (!_silent) Changed?.Invoke(this, EventArgs.Empty); };
            _chips[value] = rb;
            Children.Add(rb);
            if (_chips.Count == 1) { _silent = true; rb.IsChecked = true; _silent = false; }
            return this;
        }

        public string Value
        {
            get
            {
                foreach (KeyValuePair<string, RadioButton> kv in _chips)
                    if (kv.Value.IsChecked == true) return kv.Key;
                return null;
            }
            set
            {
                if (value != null && _chips.TryGetValue(value, out RadioButton rb)) rb.IsChecked = true;
            }
        }
    }
}
