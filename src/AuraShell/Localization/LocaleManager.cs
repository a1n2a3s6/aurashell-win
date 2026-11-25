using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;

namespace AuraShell.Localization
{
    /// <summary>
    /// Manages live Arabic (RTL) & English (LTR) switching across every open WPF Window,
    /// pill, widget, and settings panel without restarting the app.
    /// </summary>
    public static class LocaleManager
    {
        private const string EnUri = "Localization/Strings.en.xaml";
        private const string ArUri = "Localization/Strings.ar.xaml";

        public static string CurrentLanguage { get; private set; } = "en";
        public static bool IsArabic => string.Equals(CurrentLanguage, "ar", StringComparison.OrdinalIgnoreCase);
        public static FlowDirection CurrentFlowDirection => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public static event EventHandler<string>? LocaleChanged;

        public static void ApplyLanguage(string langCode)
        {
            string normalized = string.Equals(langCode, "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
            CurrentLanguage = normalized;

            var culture = new CultureInfo(normalized == "ar" ? "ar-KW" : "en-US");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            var app = Application.Current;
            if (app != null)
            {
                var merged = app.Resources.MergedDictionaries;
                var existing = merged.FirstOrDefault(d =>
                    d.Source != null &&
                    (d.Source.OriginalString.EndsWith(EnUri, StringComparison.OrdinalIgnoreCase) ||
                     d.Source.OriginalString.EndsWith(ArUri, StringComparison.OrdinalIgnoreCase)));

                var nextDict = new ResourceDictionary
                {
                    Source = new Uri(normalized == "ar" ? ArUri : EnUri, UriKind.Relative)
                };

                if (existing != null)
                {
                    int idx = merged.IndexOf(existing);
                    merged[idx] = nextDict;
                }
                else
                {
                    merged.Add(nextDict);
                }

                // Update FlowDirection on every open Window live
                FlowDirection flow = CurrentFlowDirection;
                foreach (Window win in app.Windows)
                {
                    win.FlowDirection = flow;
                }
            }

            LocaleChanged?.Invoke(null, normalized);
        }

        public static void ToggleLanguage()
        {
            ApplyLanguage(IsArabic ? "en" : "ar");
        }

        public static string Get(string key, string fallback = "")
        {
            if (Application.Current?.TryFindResource(key) is string value)
                return value;
            return string.IsNullOrEmpty(fallback) ? key : fallback;
        }
    }
}
