using System.Windows;
using System.Windows.Media;

namespace Kuduck.Services
{
    /// <summary>Light and dark colour sets. Every view uses these brushes as DynamicResource.</summary>
    public static class Theme
    {
        public static void Apply(bool dark)
        {
            ResourceDictionary r = Application.Current.Resources;
            Set(r, "BgBrush", dark ? "#16171B" : "#FBF8F0");
            Set(r, "SurfaceBrush", dark ? "#22242A" : "#FFFFFF");
            Set(r, "SurfaceHoverBrush", dark ? "#2C2F36" : "#F4F1E8");
            Set(r, "BorderBrush", dark ? "#3A3D45" : "#E3E1DA");
            Set(r, "InputBorderBrush", dark ? "#4A4E57" : "#CDD1D8");
            Set(r, "TextBrush", dark ? "#F2F1EC" : "#17181C");
            Set(r, "MutedBrush", dark ? "#A9ADB6" : "#5B616B");
            Set(r, "ImageAccentBrush", dark ? "#7DA2FF" : "#2357D9");
            Set(r, "ImageButtonBrush", "#2357D9");
            Set(r, "ImageTintBrush", dark ? "#25304A" : "#E8EEFC");
            Set(r, "PdfAccentBrush", dark ? "#FF9A66" : "#B23A0A");
            Set(r, "PdfButtonBrush", "#B23A0A");
            Set(r, "PdfTintBrush", dark ? "#3D2A21" : "#FDEEE5");
            Set(r, "BrandBrush", "#F7C531");
            Set(r, "BrandTintBrush", dark ? "#3B331A" : "#FCEFC4");
            Set(r, "InkBrush", "#17181C");
            Set(r, "BeakBrush", "#EE7A2B");
            Set(r, "SuccessBrush", dark ? "#5FD08A" : "#1E7A44");
            Set(r, "ErrorBrush", dark ? "#FF8A80" : "#B42318");
            Set(r, "TrackBrush", dark ? "#3A3D45" : "#E1E4E9");
        }

        private static void Set(ResourceDictionary r, string key, string hex)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            r[key] = brush;
        }
    }
}
