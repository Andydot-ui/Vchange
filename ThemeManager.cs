using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Vchange
{
    /// <summary>
    /// 界面主题管理：跟随系统深色/浅色自动切换配色（应用界面主题）。
    /// 所有颜色通过 Theme.* 动态资源引用，切换时实时生效。
    /// </summary>
    internal static class ThemeManager
    {
        public static bool IsLight { get; private set; }

        private static readonly Dictionary<string, string> DarkTheme = new()
        {
            ["Theme.WindowTint"] = "#8C202020",
            ["Theme.TitleBar"] = "#592B2B2B",
            ["Theme.Surface"] = "#B32A2A2A",
            ["Theme.SurfaceBorder"] = "#26FFFFFF",
            ["Theme.Control"] = "#C2333333",
            ["Theme.ControlBorder"] = "#3C3C3C",
            ["Theme.ControlBorderHover"] = "#5C5C5C",
            ["Theme.ControlPressed"] = "#2F2F2F",
            ["Theme.ControlDisabled"] = "#232323",
            ["Theme.TextPrimary"] = "#FFFFFF",
            ["Theme.TextSecondary"] = "#A0A0A0",
            ["Theme.Hover"] = "#3A3A3A",
            ["Theme.Popup"] = "#2B2B2B",
            ["Theme.PopupBorder"] = "#3C3C3C",
            ["Theme.ItemHover"] = "#383838",
            ["Theme.ItemSelected"] = "#404040",
            ["Theme.ProgressTrack"] = "#3A3A3A",
            ["Theme.ToggleBorder"] = "#8A8A8A",
            ["Theme.ToggleBorderHover"] = "#B0B0B0",
            ["Theme.ToggleKnob"] = "#D6D6D6",
            ["Theme.IconForeground"] = "#E0E0E0",
            ["Theme.Chevron"] = "#5C5C5C",
            ["Theme.StepDotInactive"] = "#3A3A3A",
            ["Theme.Dialog"] = "#F0202020",
        };

        private static readonly Dictionary<string, string> LightTheme = new()
        {
            ["Theme.WindowTint"] = "#B3F3F3F3",
            ["Theme.TitleBar"] = "#59FFFFFF",
            ["Theme.Surface"] = "#E8FFFFFF",
            ["Theme.SurfaceBorder"] = "#22000000",
            ["Theme.Control"] = "#F2FFFFFF",
            ["Theme.ControlBorder"] = "#D6D6D6",
            ["Theme.ControlBorderHover"] = "#9A9A9A",
            ["Theme.ControlPressed"] = "#E6E6E6",
            ["Theme.ControlDisabled"] = "#EDEDED",
            ["Theme.TextPrimary"] = "#1A1A1A",
            ["Theme.TextSecondary"] = "#6A6A6A",
            ["Theme.Hover"] = "#E8E8E8",
            ["Theme.Popup"] = "#F7F7F7",
            ["Theme.PopupBorder"] = "#D6D6D6",
            ["Theme.ItemHover"] = "#EDEDED",
            ["Theme.ItemSelected"] = "#E2E2E2",
            ["Theme.ProgressTrack"] = "#DCDCDC",
            ["Theme.ToggleBorder"] = "#8A8A8A",
            ["Theme.ToggleBorderHover"] = "#5A5A5A",
            ["Theme.ToggleKnob"] = "#5A5A5A",
            ["Theme.IconForeground"] = "#404040",
            ["Theme.Chevron"] = "#9A9A9A",
            ["Theme.StepDotInactive"] = "#DEDEDE",
            ["Theme.Dialog"] = "#F7F7F7",
        };

        /// <summary>应用主题（light = true 用浅色，false 用深色）。</summary>
        public static void Apply(bool light)
        {
            IsLight = light;
            var res = Application.Current.Resources;
            var palette = light ? LightTheme : DarkTheme;

            foreach (var kv in palette)
            {
                var color = (Color)ColorConverter.ConvertFromString(kv.Value)!;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                res[kv.Key] = brush;
            }
        }

        /// <summary>读取系统是否处于浅色模式（任务栏/系统 UI）。</summary>
        public static bool SystemIsLight()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("SystemUsesLightTheme") is int v)
                    return v == 1;
            }
            catch { }
            return false;
        }
    }
}
