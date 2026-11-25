using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AuraShell.Localization;
using AuraShell.Services;

namespace AuraShell.Modules.Island.Tabs
{
    public partial class FileShelfTabView : UserControl
    {
        private ConfigStore? _configStore;
        private Action<string>? _showToast;
        private readonly List<string> _shelfFiles = new();
        private readonly List<string> _clipboardHistory = new();
        private Point _dragStartPoint;
        private bool _suppressConfigSave;

        public bool IsEditingText => PinNoteBox.IsKeyboardFocused || ScratchpadBox.IsKeyboardFocused;
        public int ShelfFileCount => _shelfFiles.Count;

        public FileShelfTabView()
        {
            InitializeComponent();
        }

        public void Initialize(ConfigStore configStore, Action<string> showToast)
        {
            _configStore = configStore;
            _showToast = showToast;

            _suppressConfigSave = true;
            PinNoteBox.Text = configStore.Current.PinnedNote;
            ScratchpadBox.Text = configStore.Current.ScratchpadText;
            UpdatePlaceholders();
            _suppressConfigSave = false;

            RefreshShelfUi();
            RefreshClipboardUi();
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            bool changed = false;
            foreach (string p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (!File.Exists(p) && !Directory.Exists(p)) continue;
                if (!_shelfFiles.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    _shelfFiles.Add(p);
                    changed = true;
                }
            }
            if (changed) RefreshShelfUi();
        }

        public void RecordClipboardText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            string cleaned = text.Trim();
            _clipboardHistory.RemoveAll(x => string.Equals(x, cleaned, StringComparison.Ordinal));
            _clipboardHistory.Insert(0, cleaned);
            if (_clipboardHistory.Count > 5)
                _clipboardHistory.RemoveRange(5, _clipboardHistory.Count - 5);

            RefreshClipboardUi();
        }

        private void RefreshShelfUi()
        {
            ShelfFilesPanel.Children.Clear();
            string badgeLabel = LocaleManager.Get("ShelfFilesBadge", "files");
            FileCountBadge.Text = $"{_shelfFiles.Count} {badgeLabel}";
            DropHintText.Visibility = _shelfFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (string path in _shelfFiles)
            {
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) name = path;
                string icon = Directory.Exists(path) ? "📁" : "📄";

                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(23, 30, 46)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(45, 58, 86)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(7, 4, 7, 4),
                    Margin = new Thickness(0, 0, 5, 5),
                    Cursor = Cursors.Hand,
                    Tag = path,
                    ToolTip = path
                };

                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = icon, Margin = new Thickness(0, 0, 4, 0), FontSize = 11 });
                sp.Children.Add(new TextBlock
                {
                    Text = name.Length > 18 ? name.Substring(0, 15) + "..." : name,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240))
                });
                chip.Child = sp;

                chip.PreviewMouseLeftButtonDown += (_, e) => _dragStartPoint = e.GetPosition(null);
                chip.PreviewMouseMove += (s, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed) return;
                    Vector diff = _dragStartPoint - e.GetPosition(null);
                    if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        if (s is Border b && b.Tag is string filePath)
                        {
                            var data = new DataObject(DataFormats.FileDrop, new[] { filePath });
                            DragDrop.DoDragDrop(b, data, DragDropEffects.Copy | DragDropEffects.Move);
                        }
                    }
                };

                ShelfFilesPanel.Children.Add(chip);
            }
        }

        private void RefreshClipboardUi()
        {
            ClipboardPillsPanel.Children.Clear();
            if (_clipboardHistory.Count == 0)
            {
                ClipboardPillsPanel.Children.Add(new TextBlock
                {
                    Text = LocaleManager.Get("ShelfClipboardEmpty", "Copy text to populate history..."),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                });
                return;
            }

            foreach (string entry in _clipboardHistory)
            {
                string singleLine = entry.Replace("\r", " ").Replace("\n", " ");
                string preview = singleLine.Length > 20 ? singleLine.Substring(0, 18) + "…" : singleLine;

                var btn = new Button
                {
                    Content = preview,
                    Style = (Style)FindResource("IslandChipBtn"),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 4),
                    FontSize = 10.5,
                    ToolTip = entry
                };

                string captured = entry;
                btn.Click += (_, _) =>
                {
                    try
                    {
                        Clipboard.SetText(captured);
                        _showToast?.Invoke(LocaleManager.Get("AiCopied", "Copied!"));
                    }
                    catch { }
                };

                ClipboardPillsPanel.Children.Add(btn);
            }
        }

        private void Shelf_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Shelf_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                AddFiles(files);
                e.Handled = true;
            }
        }

        private void ClearShelfBtn_Click(object sender, RoutedEventArgs e)
        {
            _shelfFiles.Clear();
            RefreshShelfUi();
        }

        private void PinNoteBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaceholders();
            if (_suppressConfigSave || _configStore == null) return;
            string val = PinNoteBox.Text;
            _configStore.Update(c => c.PinnedNote = val);
        }

        private void ScratchpadBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaceholders();
            if (_suppressConfigSave || _configStore == null) return;
            string val = ScratchpadBox.Text;
            _configStore.Update(c => c.ScratchpadText = val);
        }

        private void UpdatePlaceholders()
        {
            PinNotePlaceholder.Visibility = string.IsNullOrEmpty(PinNoteBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ScratchpadPlaceholder.Visibility = string.IsNullOrEmpty(ScratchpadBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
