using System;
using System.Linq;
using System.Windows;

namespace TasksApp.Services
{
    public static class ThemeManager
    {
        public const string DefaultTheme = "Default";
        public const string Win95Theme = "Win95";
        public const string RetroTheme = "Retro";
        public const string TerminalTheme = "Terminal";
        public const string CocoTheme = "Coco";

        public static string CurrentTheme { get; private set; } = DefaultTheme;

        public static bool IsWin95 => string.Equals(CurrentTheme, Win95Theme, StringComparison.OrdinalIgnoreCase);
        public static bool IsRetro => string.Equals(CurrentTheme, RetroTheme, StringComparison.OrdinalIgnoreCase);
        public static bool IsTerminal => string.Equals(CurrentTheme, TerminalTheme, StringComparison.OrdinalIgnoreCase);
        public static bool IsCoco => string.Equals(CurrentTheme, CocoTheme, StringComparison.OrdinalIgnoreCase);

        public static event Action<string>? ThemeChanged;

        /// <summary>
        /// Applies the requested theme ("Default", "Win95", "Retro", "Terminal", or "Coco") to the entire application.
        /// </summary>
        public static void ApplyTheme(string? themeName)
        {
            string targetTheme;
            if (string.Equals(themeName, Win95Theme, StringComparison.OrdinalIgnoreCase))
            {
                targetTheme = Win95Theme;
            }
            else if (string.Equals(themeName, RetroTheme, StringComparison.OrdinalIgnoreCase))
            {
                targetTheme = RetroTheme;
            }
            else if (string.Equals(themeName, TerminalTheme, StringComparison.OrdinalIgnoreCase))
            {
                targetTheme = TerminalTheme;
            }
            else if (string.Equals(themeName, CocoTheme, StringComparison.OrdinalIgnoreCase))
            {
                targetTheme = CocoTheme;
            }
            else
            {
                targetTheme = DefaultTheme;
            }

            CurrentTheme = targetTheme;

            if (Application.Current != null)
            {
                string uriString = targetTheme switch
                {
                    Win95Theme => "/Themes/Win95Theme.xaml",
                    RetroTheme => "/Themes/RetroTheme.xaml",
                    TerminalTheme => "/Themes/TerminalTheme.xaml",
                    CocoTheme => "/Themes/CocoTheme.xaml",
                    _ => "/Themes/DefaultTheme.xaml"
                };

                try
                {
                    var newDict = new ResourceDictionary
                    {
                        Source = new Uri(uriString, UriKind.RelativeOrAbsolute)
                    };

                    var appResources = Application.Current.Resources;

                    // Remove existing theme dictionaries
                    var existingThemeDicts = appResources.MergedDictionaries
                        .Where(d => d.Source != null && (
                            d.Source.OriginalString.Contains("DefaultTheme.xaml") || 
                            d.Source.OriginalString.Contains("Win95Theme.xaml") ||
                            d.Source.OriginalString.Contains("RetroTheme.xaml") ||
                            d.Source.OriginalString.Contains("TerminalTheme.xaml") ||
                            d.Source.OriginalString.Contains("CocoTheme.xaml")))
                        .ToList();

                    foreach (var oldDict in existingThemeDicts)
                    {
                        appResources.MergedDictionaries.Remove(oldDict);
                    }

                    // Add the new theme dictionary
                    appResources.MergedDictionaries.Add(newDict);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load theme {targetTheme}: {ex.Message}");
                }
            }

            ThemeChanged?.Invoke(CurrentTheme);
        }
    }
}
