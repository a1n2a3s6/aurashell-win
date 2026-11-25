using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using AuraShell.Localization;

namespace AuraShell.Modules.Widgets.Cards
{
    public partial class AppleCalendarWidget : UserControl, IAppleWidgetCard
    {
        private readonly DispatcherTimer _dateTimer;
        private bool _isDark = true;
        private AppleWidgetSize _currentSize = AppleWidgetSize.Small;

        public string WidgetId => "calendar";
        public bool UsesCustomSkyBackdrop => false;

        public AppleCalendarWidget()
        {
            InitializeComponent();
            _dateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _dateTimer.Tick += (_, _) => RebuildCalendar();
            _dateTimer.Start();

            LocaleManager.LocaleChanged += (_, _) => Dispatcher.Invoke(RebuildCalendar);
            RebuildCalendar();
        }

        private void RebuildCalendar()
        {
            var now = DateTime.Now;
            var culture = CultureInfo.CurrentCulture;

            WeekdayText.Text = now.ToString("dddd", culture).ToUpper(culture);
            DayNumberText.Text = now.Day.ToString(CultureInfo.InvariantCulture);
            MonthHeaderText.Text = now.ToString("MMMM yyyy", culture).ToUpper(culture);

            UniformGrid targetGrid = _currentSize == AppleWidgetSize.Medium ? WideMonthGrid : SmallMonthGrid;
            SmallMonthGrid.Children.Clear();
            WideMonthGrid.Children.Clear();

            bool compactCells = _currentSize == AppleWidgetSize.Small;
            double cellSize = compactCells ? 16 : 22;
            double fontSize = compactCells ? 8.5 : 10.5;

            Brush dayFg = _isDark
                ? new SolidColorBrush(Color.FromRgb(235, 235, 240))
                : new SolidColorBrush(Color.FromRgb(28, 28, 30));
            Brush headerFg = new SolidColorBrush(Color.FromRgb(142, 142, 147));
            Brush appleRed = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));

            // Day-of-week single-letter headers (Sun..Sat)
            string[] dayNames = culture.DateTimeFormat.ShortestDayNames;
            for (int d = 0; d < 7; d++)
            {
                string lbl = dayNames[d];
                if (lbl.Length > 2) lbl = lbl.Substring(0, 1);
                targetGrid.Children.Add(new TextBlock
                {
                    Text = lbl.ToUpper(culture),
                    FontSize = fontSize - 1,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = headerFg,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }

            var firstDay = new DateTime(now.Year, now.Month, 1);
            int startOffset = (int)firstDay.DayOfWeek;
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);

            for (int i = 0; i < startOffset; i++)
                targetGrid.Children.Add(new Border { Width = cellSize, Height = cellSize });

            for (int day = 1; day <= daysInMonth; day++)
            {
                bool isToday = day == now.Day;
                var cell = new Border
                {
                    Width = cellSize,
                    Height = cellSize,
                    CornerRadius = new CornerRadius(cellSize / 2.0),
                    Background = isToday ? appleRed : Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var tb = new TextBlock
                {
                    Text = day.ToString(CultureInfo.InvariantCulture),
                    FontSize = fontSize,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Medium,
                    Foreground = isToday ? Brushes.White : dayFg,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
                cell.Child = tb;
                targetGrid.Children.Add(cell);
            }
        }

        public void ApplySize(AppleWidgetSize size)
        {
            _currentSize = size;
            bool isMedium = size == AppleWidgetSize.Medium;
            GridSpacerCol.Width = isMedium ? new GridLength(12) : new GridLength(0);
            MonthGridCol.Width = isMedium ? new GridLength(1.45, GridUnitType.Star) : new GridLength(0);
            MediumMonthPanel.Visibility = isMedium ? Visibility.Visible : Visibility.Collapsed;
            SmallMonthGrid.Visibility = isMedium ? Visibility.Collapsed : Visibility.Visible;
            DayNumberText.FontSize = size == AppleWidgetSize.Small ? 34 : 52;
            RebuildCalendar();
        }

        public void ApplyTheme(bool isDark)
        {
            _isDark = isDark;
            DayNumberText.Foreground = isDark
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(28, 28, 30));
            RebuildCalendar();
        }
    }
}
