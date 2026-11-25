using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AuraShell.Services
{
    public sealed class TodoItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Text { get; set; } = string.Empty;
        public bool IsDone { get; set; }
    }

    public sealed class WidgetPlacement
    {
        public string Id { get; set; } = string.Empty;
        public double X { get; set; } = 32;
        public double Y { get; set; } = 64;
        public string Size { get; set; } = "Small"; // "Small", "Medium", "Large"
        public bool IsLocked { get; set; }
        public bool IsVisible { get; set; } = true;
        public double Opacity { get; set; } = 0.92;
        public string Theme { get; set; } = "Dark"; // "Dark", "Light", "Auto"
    }

    public sealed class DockPinItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string FallbackEmoji { get; set; } = "🚀";
    }

    public sealed class AuraConfig
    {
        public string Language { get; set; } = "en";
        public string ActivePreset { get; set; } = "SonomaDark"; // "SonomaDark", "VisionLight", "ObsidianPro"

        // Phase 1: AuraIsland
        public bool IslandEnabled { get; set; } = true;
        public bool IslandHideInFullscreen { get; set; } = true;
        public string IslandDefaultPillMode { get; set; } = "Clock"; // "Clock", "Media", "Telemetry"

        public string AiProvider { get; set; } = "Gemini"; // "Gemini" or "Groq"
        public string GeminiApiKey { get; set; } = string.Empty;
        public string GeminiModel { get; set; } = "gemini-2.5-flash";
        public string GroqApiKey { get; set; } = string.Empty;
        public string GroqModel { get; set; } = "llama-3.3-70b-versatile";

        public string WeatherCity { get; set; } = "Kuwait City";
        public double WeatherLatitude { get; set; } = 29.3759;
        public double WeatherLongitude { get; set; } = 47.9774;

        public bool KeepAwakeEnabled { get; set; }
        public bool FocusDndEnabled { get; set; }

        public string PinnedNote { get; set; } = string.Empty;
        public string ScratchpadText { get; set; } = string.Empty;
        public List<TodoItem> Todos { get; set; } = new();

        // Phase 2: AuraWidgets
        public bool WidgetsEnabled { get; set; } = true;
        public string WidgetTheme { get; set; } = "Dark"; // "Dark", "Light", "Auto"
        public double WidgetOpacity { get; set; } = 0.92;
        public bool WidgetSnapToGrid { get; set; } = true;
        public List<WidgetPlacement> Widgets { get; set; } = new();

        // Phase 3: AeroDock
        public bool DockEnabled { get; set; } = true;
        public double DockIconSize { get; set; } = 44.0;
        public double DockMagnification { get; set; } = 1.5; // 44 -> 66 at 1.5x
        public bool DockAutoHide { get; set; } = true;
        public bool HideWindowsTaskbar { get; set; } = false;
        public List<DockPinItem> DockPins { get; set; } = new();
    }

    /// <summary>
    /// Thread-safe JSON configuration store saved under %AppData%\AuraShell\config.json.
    /// </summary>
    public sealed class ConfigStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly object _sync = new();
        private readonly string _configPath;

        public AuraConfig Current { get; private set; }
        public event EventHandler<AuraConfig>? ConfigChanged;

        public ConfigStore()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "AuraShell");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "config.json");
            Current = LoadInternal();

            if (Current.DockPins.Count == 0)
            {
                Current.DockPins = CreateDefaultDockPins();
                Save();
            }
        }

        private AuraConfig LoadInternal()
        {
            lock (_sync)
            {
                try
                {
                    if (File.Exists(_configPath))
                    {
                        string json = File.ReadAllText(_configPath);
                        var loaded = JsonSerializer.Deserialize<AuraConfig>(json, JsonOptions);
                        if (loaded != null)
                        {
                            loaded.Todos ??= new List<TodoItem>();
                            loaded.Widgets ??= new List<WidgetPlacement>();
                            loaded.DockPins ??= new List<DockPinItem>();
                            return loaded;
                        }
                    }
                }
                catch { }
                return new AuraConfig();
            }
        }

        public static List<DockPinItem> CreateDefaultDockPins()
        {
            var pins = new List<DockPinItem>
            {
                new() { Name = "File Explorer", TargetPath = "explorer.exe", ProcessName = "explorer", FallbackEmoji = "📁" },
                new() { Name = "Browser", TargetPath = FindDefaultBrowserPath(), ProcessName = FindDefaultBrowserProcess(), FallbackEmoji = "🌐" },
                new() { Name = "Terminal", TargetPath = FindTerminalPath(), ProcessName = "WindowsTerminal", FallbackEmoji = "💻" }
            };

            string? vsCode = FindVsCodePath();
            if (!string.IsNullOrEmpty(vsCode))
                pins.Add(new DockPinItem { Name = "VS Code", TargetPath = vsCode, ProcessName = "Code", FallbackEmoji = "🧩" });

            pins.Add(new DockPinItem { Name = "Notepad", TargetPath = "notepad.exe", ProcessName = "notepad", FallbackEmoji = "📝" });
            pins.Add(new DockPinItem { Name = "Settings", TargetPath = "ms-settings:", ProcessName = "SystemSettings", FallbackEmoji = "⚙️" });

            return pins;
        }

        private static string FindDefaultBrowserPath()
        {
            string chrome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(chrome)) return chrome;
            string edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(edge)) return edge;
            return "msedge.exe";
        }

        private static string FindDefaultBrowserProcess()
        {
            string path = FindDefaultBrowserPath();
            return Path.GetFileNameWithoutExtension(path);
        }

        private static string FindTerminalPath()
        {
            string wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
            return File.Exists(wt) ? wt : "powershell.exe";
        }

        private static string? FindVsCodePath()
        {
            string userInstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe");
            if (File.Exists(userInstall)) return userInstall;
            string sysInstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe");
            if (File.Exists(sysInstall)) return sysInstall;
            return null;
        }

        public WidgetPlacement GetOrCreateWidgetPlacement(string id, double defaultX, double defaultY, string defaultSize)
        {
            lock (_sync)
            {
                var existing = Current.Widgets.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing;

                var created = new WidgetPlacement
                {
                    Id = id,
                    X = defaultX,
                    Y = defaultY,
                    Size = defaultSize,
                    IsVisible = true,
                    Opacity = Current.WidgetOpacity,
                    Theme = Current.WidgetTheme
                };
                Current.Widgets.Add(created);
                return created;
            }
        }

        public void Save()
        {
            lock (_sync)
            {
                try
                {
                    string json = JsonSerializer.Serialize(Current, JsonOptions);
                    File.WriteAllText(_configPath, json);
                }
                catch { }
            }
            ConfigChanged?.Invoke(this, Current);
        }

        public void Update(Action<AuraConfig> mutator)
        {
            lock (_sync)
            {
                mutator(Current);
            }
            Save();
        }
    }
}
