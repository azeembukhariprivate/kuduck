using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Kuduck.Controls;
using Kuduck.Services;

namespace Kuduck.Views
{
    public class SettingsPage : UserControl, IToolView
    {
        public string Title => "Settings";

        public SettingsPage(Action onThemeChanged, Action onPinChanged, Action showAbout, Action quit)
        {
            AppSettings s = SettingsStore.Current;
            StackPanel body = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };

            body.Children.Add(Ui.Section("Look"));
            CheckBox dark = new CheckBox { Content = "Dark mode", IsChecked = s.DarkMode, Margin = new Thickness(0, 0, 0, 8) };
            dark.Click += (o, e) => { s.DarkMode = dark.IsChecked == true; SettingsStore.Save(); onThemeChanged(); };
            body.Children.Add(dark);
            CheckBox pin = new CheckBox { Content = "Keep KUDUCK on top of other windows", IsChecked = s.PinOnTop, Margin = new Thickness(0, 0, 0, 8) };
            pin.Click += (o, e) => { s.PinOnTop = pin.IsChecked == true; SettingsStore.Save(); onPinChanged(); };
            body.Children.Add(pin);

            body.Children.Add(Ui.Section("Windows"));
            CheckBox startup = new CheckBox { Content = "Start KUDUCK when Windows starts", IsChecked = StartupManager.IsEnabled, Margin = new Thickness(0, 0, 0, 8) };
            TextBlock startupError = Ui.Text("", 12, null, "ErrorBrush");
            startup.Click += (o, e) =>
            {
                if (!StartupManager.SetEnabled(startup.IsChecked == true))
                {
                    startup.IsChecked = StartupManager.IsEnabled;
                    startupError.Text = "Couldn't change this setting.";
                }
            };
            body.Children.Add(startup);
            body.Children.Add(startupError);

            body.Children.Add(Ui.Section("Where to save results"));
            ChipGroup where = new ChipGroup(2).Add("next", "Next to the original").Add("folder", "A folder I choose");
            where.Value = s.SaveNextToOriginal ? "next" : "folder";
            body.Children.Add(where);
            Grid folderRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock folderText = Ui.Text(string.IsNullOrEmpty(s.OutputFolder) ? "No folder chosen" : s.OutputFolder, 12, null, "MutedBrush");
            folderText.VerticalAlignment = VerticalAlignment.Center;
            folderText.TextTrimming = TextTrimming.CharacterEllipsis;
            folderText.TextWrapping = TextWrapping.NoWrap;
            folderRow.Children.Add(folderText);
            Button browse = new Button { Content = "Choose…", Margin = new Thickness(8, 0, 0, 0) };
            browse.Click += (o, e) =>
            {
                Microsoft.Win32.OpenFolderDialog dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Save KUDUCK results to" };
                if (dlg.ShowDialog() == true)
                {
                    s.OutputFolder = dlg.FolderName;
                    s.SaveNextToOriginal = false;
                    where.Value = "folder";
                    folderText.Text = dlg.FolderName;
                    SettingsStore.Save();
                }
            };
            Grid.SetColumn(browse, 1);
            folderRow.Children.Add(browse);
            body.Children.Add(folderRow);
            where.Changed += (o, e) =>
            {
                s.SaveNextToOriginal = where.Value == "next";
                SettingsStore.Save();
            };
            body.Children.Add(Ui.Hint("If KUDUCK can't save in a folder (for example Program Files), choose another folder or run KUDUCK as administrator."));

            body.Children.Add(Ui.Section("Updates"));
            CheckBox updates = new CheckBox { Content = "Tell me when a new version is out", IsChecked = s.CheckForUpdates, Margin = new Thickness(0, 0, 0, 8) };
            updates.Click += (o, e) => { s.CheckForUpdates = updates.IsChecked == true; SettingsStore.Save(); };
            body.Children.Add(updates);

            body.Children.Add(Ui.Section("KUDUCK"));
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal };
            Button about = new Button { Content = "About", Margin = new Thickness(0, 0, 8, 0) };
            about.Click += (o, e) => showAbout();
            buttons.Children.Add(about);
            Button quitButton = new Button { Content = "Quit KUDUCK" };
            quitButton.Click += (o, e) => quit();
            buttons.Children.Add(quitButton);
            body.Children.Add(buttons);

            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        }

        public void AddFiles(IEnumerable<string> files) { }
        public void ApplyPreset(Dictionary<string, string> settings) { }
    }

    public class AboutPage : UserControl, IToolView
    {
        public string Title => "About";

        public AboutPage()
        {
            StackPanel body = new StackPanel { Margin = new Thickness(24, 28, 24, 24) };
            FrameworkElement logo = Ui.Logo(96);
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.Add(logo);
            TextBlock name = Ui.Text("KUDUCK", 30, FontWeights.Black);
            name.SetResourceReference(TextBlock.FontFamilyProperty, "BrandFont");
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(name);
            TextBlock tagline = Ui.Text("Image & PDF tools, one duck away.", 13, null, "MutedBrush");
            tagline.HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.Add(tagline);

            Border card = new Border { Style = Ui.GetStyle("Card"), Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(14) };
            StackPanel info = new StackPanel();
            info.Children.Add(Line("Version", AppPaths.Version));
            info.Children.Add(Line("Created by", "Azeem with Claude"));
            card.Child = info;
            body.Children.Add(card);

            Button site = new Button { Style = Ui.GetStyle("LinkButton"), Content = "Releases & source code", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            site.Click += (s, e) => AppPaths.OpenUrl($"https://github.com/{AppPaths.RepoOwner}/{AppPaths.RepoName}");
            body.Children.Add(site);

            TextBlock credits = Ui.Hint("Built with open-source parts: Magick.NET, PDFsharp, ONNX Runtime, the U²-Net model, " +
                                        "pdfplumber, python-docx and openpyxl. Details are in the licence files installed with KUDUCK.");
            credits.TextAlignment = TextAlignment.Center;
            credits.Margin = new Thickness(0, 18, 0, 0);
            body.Children.Add(credits);

            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        }

        private static UIElement Line(string label, string value)
        {
            Grid g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.Children.Add(Ui.Text(label, 13, null, "MutedBrush"));
            TextBlock v = Ui.Text(value, 13, FontWeights.SemiBold);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(v);
            return g;
        }

        public void AddFiles(IEnumerable<string> files) { }
        public void ApplyPreset(Dictionary<string, string> settings) { }
    }
}
