using System;
using System.Windows;

namespace PharmacyInventorySystem.Services
{
    public enum AppTheme
    {
        Dark,
        Light,
        Blue
    }

    public static class ThemeManager
    {
        public static AppTheme Current { get; private set; } = AppTheme.Dark;

        public static event EventHandler? ThemeChanged;

        public static int GetSelectorIndex(AppTheme theme) => theme switch
        {
            AppTheme.Dark => 0,
            AppTheme.Light => 1,
            AppTheme.Blue => 2,
            _ => 2
        };

        public static int GetCurrentSelectorIndex() => GetSelectorIndex(Current);

        public static void ApplyTheme(AppTheme theme)
        {
            var themeFile = theme switch
            {
                AppTheme.Light => "LightTheme.xaml",
                AppTheme.Blue => "BlueTheme.xaml",
                _ => "DarkTheme.xaml"
            };

            var app = Application.Current;
            if (app == null)
                return;

            var dictionaries = app.Resources.MergedDictionaries;
            var newTheme = new ResourceDictionary
            {
                Source = new Uri($"Themes/{themeFile}", UriKind.Relative)
            };

            // Keep the theme dictionary in the same slot instead of removing it
            // and inserting it again. This avoids transient resource-order changes
            // that can leave already-created controls visually out of sync.
            int themeIndex = -1;
            for (int i = 0; i < dictionaries.Count; i++)
            {
                var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
                if (source.Contains("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)
                    || source.Contains("LightTheme.xaml", StringComparison.OrdinalIgnoreCase)
                    || source.Contains("BlueTheme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    themeIndex = i;
                    break;
                }
            }

            if (themeIndex >= 0)
                dictionaries[themeIndex] = newTheme;
            else
                dictionaries.Insert(0, newTheme);

            Current = theme;

            foreach (Window window in app.Windows)
                window.InvalidateVisual();

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
