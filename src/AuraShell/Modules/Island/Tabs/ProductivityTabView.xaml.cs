using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AuraShell.Localization;
using AuraShell.Native;
using AuraShell.Services;

namespace AuraShell.Modules.Island.Tabs
{
    public partial class ProductivityTabView : UserControl
    {
        private enum TimerMode { Pomodoro, Countdown, Stopwatch }

        private ConfigStore? _configStore;
        private Action<string>? _showToast;
        private readonly DispatcherTimer _tickTimer;

        private TimerMode _mode = TimerMode.Pomodoro;
        private TimeSpan _remaining = TimeSpan.FromMinutes(25);
        private TimeSpan _elapsed = TimeSpan.Zero;
        private bool _timerRunning;
        private string _lastCalcResult = string.Empty;

        public bool IsEditingText => TodoInput.IsKeyboardFocused || CalcInput.IsKeyboardFocused || CustomMinBox.IsKeyboardFocused;
        public bool IsTimerRunning => _timerRunning;
        public string ActiveTimerBadge => _timerRunning
            ? (_mode == TimerMode.Stopwatch ? FormatSpan(_elapsed) : FormatSpan(_remaining))
            : string.Empty;

        public event EventHandler<bool>? DndChanged;

        public ProductivityTabView()
        {
            InitializeComponent();

            _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tickTimer.Tick += OnSecondTick;
            _tickTimer.Start();

            UpdateClock();
        }

        public void Initialize(ConfigStore configStore, Action<string> showToast)
        {
            _configStore = configStore;
            _showToast = showToast;

            if (configStore.Current.KeepAwakeEnabled)
                Win32Shell.SetKeepAwake(true);

            UpdateToggleVisuals();
            RefreshTodosUi();
        }

        private void OnSecondTick(object? sender, EventArgs e)
        {
            UpdateClock();

            if (!_timerRunning) return;

            if (_mode == TimerMode.Stopwatch)
            {
                _elapsed = _elapsed.Add(TimeSpan.FromSeconds(1));
                TimerDisplayText.Text = FormatSpan(_elapsed);
            }
            else
            {
                if (_remaining.TotalSeconds > 1)
                {
                    _remaining = _remaining.Subtract(TimeSpan.FromSeconds(1));
                    TimerDisplayText.Text = FormatSpan(_remaining);
                }
                else
                {
                    _remaining = TimeSpan.Zero;
                    _timerRunning = false;
                    TimerDisplayText.Text = "00:00";
                    TimerToggleBtn.Content = LocaleManager.Get("ProdStart", "Start");
                    System.Media.SystemSounds.Exclamation.Play();
                    _showToast?.Invoke(_mode == TimerMode.Pomodoro ? "🍅 Pomodoro Complete!" : "⏱️ Timer Finished!");
                }
            }
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            LiveClockText.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            LiveDateText.Text = now.ToString("ddd, dd MMM yyyy", CultureInfo.CurrentCulture);
        }

        private static string FormatSpan(TimeSpan ts)
        {
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes:00}:{ts.Seconds:00}";
        }

        private void PomodoroBtn_Click(object sender, RoutedEventArgs e)
        {
            _mode = TimerMode.Pomodoro;
            _remaining = TimeSpan.FromMinutes(25);
            _timerRunning = true;
            TimerDisplayText.Text = FormatSpan(_remaining);
            TimerToggleBtn.Content = LocaleManager.Get("ProdPause", "Pause");
        }

        private void CustomCountdownBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(CustomMinBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double mins) || mins <= 0)
                mins = 10;
            _mode = TimerMode.Countdown;
            _remaining = TimeSpan.FromMinutes(Math.Clamp(mins, 0.5, 600));
            _timerRunning = true;
            TimerDisplayText.Text = FormatSpan(_remaining);
            TimerToggleBtn.Content = LocaleManager.Get("ProdPause", "Pause");
        }

        private void StopwatchModeBtn_Click(object sender, RoutedEventArgs e)
        {
            _mode = TimerMode.Stopwatch;
            _elapsed = TimeSpan.Zero;
            _timerRunning = true;
            TimerDisplayText.Text = "00:00";
            TimerToggleBtn.Content = LocaleManager.Get("ProdPause", "Pause");
        }

        private void TimerToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            _timerRunning = !_timerRunning;
            TimerToggleBtn.Content = _timerRunning
                ? LocaleManager.Get("ProdPause", "Pause")
                : LocaleManager.Get("ProdStart", "Start");
        }

        private void TimerResetBtn_Click(object sender, RoutedEventArgs e)
        {
            _timerRunning = false;
            TimerToggleBtn.Content = LocaleManager.Get("ProdStart", "Start");
            if (_mode == TimerMode.Stopwatch)
            {
                _elapsed = TimeSpan.Zero;
                TimerDisplayText.Text = "00:00";
            }
            else if (_mode == TimerMode.Pomodoro)
            {
                _remaining = TimeSpan.FromMinutes(25);
                TimerDisplayText.Text = "25:00";
            }
            else
            {
                if (!double.TryParse(CustomMinBox.Text.Trim(), out double mins) || mins <= 0) mins = 10;
                _remaining = TimeSpan.FromMinutes(mins);
                TimerDisplayText.Text = FormatSpan(_remaining);
            }
        }

        private void CaffeineBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_configStore == null) return;
            bool next = !_configStore.Current.KeepAwakeEnabled;
            _configStore.Update(c => c.KeepAwakeEnabled = next);
            Win32Shell.SetKeepAwake(next);
            UpdateToggleVisuals();
            _showToast?.Invoke(next ? "☕ Keep-Awake ON" : "☕ Keep-Awake OFF");
        }

        private void DndBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_configStore == null) return;
            bool next = !_configStore.Current.FocusDndEnabled;
            _configStore.Update(c => c.FocusDndEnabled = next);
            UpdateToggleVisuals();
            DndChanged?.Invoke(this, next);
            _showToast?.Invoke(next ? "🌙 Focus / DND ON" : "🌙 Focus / DND OFF");
        }

        private void UpdateToggleVisuals()
        {
            if (_configStore == null) return;
            bool caf = _configStore.Current.KeepAwakeEnabled;
            bool dnd = _configStore.Current.FocusDndEnabled;

            CaffeineBtn.BorderBrush = caf
                ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
                : new SolidColorBrush(Color.FromRgb(38, 46, 66));
            DndBtn.BorderBrush = dnd
                ? new SolidColorBrush(Color.FromRgb(167, 139, 250))
                : new SolidColorBrush(Color.FromRgb(38, 46, 66));
        }

        #region Quick To-Do Counter

        private void AddTodoBtn_Click(object sender, RoutedEventArgs e) => AddTodo();

        private void TodoInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AddTodo();
            }
        }

        private void AddTodo()
        {
            if (_configStore == null) return;
            string text = TodoInput.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _configStore.Update(c => c.Todos.Insert(0, new TodoItem { Text = text, IsDone = false }));
            TodoInput.Text = string.Empty;
            RefreshTodosUi();
        }

        private void RefreshTodosUi()
        {
            if (_configStore == null) return;
            TodoListPanel.Children.Clear();
            var list = _configStore.Current.Todos;
            int done = list.Count(t => t.IsDone);
            TodoCountText.Text = $"{done}/{list.Count}";

            foreach (var item in list.ToList())
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cb = new CheckBox
                {
                    IsChecked = item.IsDone,
                    Content = new TextBlock
                    {
                        Text = item.Text,
                        FontSize = 11,
                        Foreground = item.IsDone
                            ? new SolidColorBrush(Color.FromRgb(100, 116, 139))
                            : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                        TextDecorations = item.IsDone ? TextDecorations.Strikethrough : null,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    VerticalAlignment = VerticalAlignment.Center
                };

                string capturedId = item.Id;
                cb.Checked += (_, _) => ToggleTodo(capturedId, true);
                cb.Unchecked += (_, _) => ToggleTodo(capturedId, false);
                Grid.SetColumn(cb, 0);

                var delBtn = new Button
                {
                    Content = "×",
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 12,
                    Padding = new Thickness(4, 0, 4, 0),
                    Cursor = Cursors.Hand
                };
                delBtn.Click += (_, _) => DeleteTodo(capturedId);
                Grid.SetColumn(delBtn, 1);

                row.Children.Add(cb);
                row.Children.Add(delBtn);
                TodoListPanel.Children.Add(row);
            }
        }

        private void ToggleTodo(string id, bool isDone)
        {
            _configStore?.Update(c =>
            {
                var t = c.Todos.FirstOrDefault(x => x.Id == id);
                if (t != null) t.IsDone = isDone;
            });
            RefreshTodosUi();
        }

        private void DeleteTodo(string id)
        {
            _configStore?.Update(c => c.Todos.RemoveAll(x => x.Id == id));
            RefreshTodosUi();
        }

        #endregion

        #region Quick Calculator (Recursive-Descent Expression Parser)

        private void CalcInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string raw = CalcInput.Text.Trim();
            CalcPlaceholder.Visibility = string.IsNullOrEmpty(raw) ? Visibility.Visible : Visibility.Collapsed;

            if (raw.StartsWith("=")) raw = raw.Substring(1).Trim();
            if (string.IsNullOrEmpty(raw))
            {
                _lastCalcResult = string.Empty;
                CalcResultBtn.Content = "= 0";
                return;
            }

            if (TryEvaluateExpression(raw, out double val) && !double.IsNaN(val) && !double.IsInfinity(val))
            {
                _lastCalcResult = Math.Round(val, 6).ToString(CultureInfo.InvariantCulture);
                CalcResultBtn.Content = $"= {_lastCalcResult}";
            }
            else
            {
                _lastCalcResult = string.Empty;
                CalcResultBtn.Content = "= ?";
            }
        }

        private void CalcResultBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastCalcResult)) return;
            try
            {
                Clipboard.SetText(_lastCalcResult);
                _showToast?.Invoke($"Copied {_lastCalcResult}");
            }
            catch { }
        }

        private static bool TryEvaluateExpression(string expr, out double result)
        {
            result = 0;
            try
            {
                var parser = new ExpressionParser(expr);
                result = parser.Parse();
                return parser.IsAtEnd;
            }
            catch
            {
                return false;
            }
        }

        private sealed class ExpressionParser
        {
            private readonly string _s;
            private int _pos;

            public ExpressionParser(string s) => _s = s;
            public bool IsAtEnd { get { SkipWs(); return _pos >= _s.Length; } }

            public double Parse() => ParseAddSub();

            private double ParseAddSub()
            {
                double val = ParseMulDiv();
                while (true)
                {
                    SkipWs();
                    if (Match('+')) val += ParseMulDiv();
                    else if (Match('-')) val -= ParseMulDiv();
                    else break;
                }
                return val;
            }

            private double ParseMulDiv()
            {
                double val = ParsePower();
                while (true)
                {
                    SkipWs();
                    if (Match('*')) val *= ParsePower();
                    else if (Match('/')) val /= ParsePower();
                    else if (Match('%')) val %= ParsePower();
                    else break;
                }
                return val;
            }

            private double ParsePower()
            {
                double val = ParseUnary();
                SkipWs();
                if (Match('^')) val = Math.Pow(val, ParsePower());
                return val;
            }

            private double ParseUnary()
            {
                SkipWs();
                if (Match('+')) return ParseUnary();
                if (Match('-')) return -ParseUnary();
                return ParsePrimary();
            }

            private double ParsePrimary()
            {
                SkipWs();
                if (Match('('))
                {
                    double inner = ParseAddSub();
                    SkipWs();
                    if (!Match(')')) throw new FormatException();
                    return inner;
                }

                int start = _pos;
                while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.'))
                    _pos++;
                if (_pos == start) throw new FormatException();
                return double.Parse(_s.Substring(start, _pos - start), CultureInfo.InvariantCulture);
            }

            private void SkipWs()
            {
                while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
            }

            private bool Match(char c)
            {
                if (_pos < _s.Length && _s[_pos] == c) { _pos++; return true; }
                return false;
            }
        }

        #endregion
    }
}
