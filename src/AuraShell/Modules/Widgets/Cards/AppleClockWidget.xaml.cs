using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AuraShell.Localization;

namespace AuraShell.Modules.Widgets.Cards
{
    public partial class AppleClockWidget : UserControl, IAppleWidgetCard
    {
        private bool _isDark = true;

        public string WidgetId => "clock";
        public bool UsesCustomSkyBackdrop => false;

        public AppleClockWidget()
        {
            InitializeComponent();
            DrawDialTicks();
            CompositionTarget.Rendering += OnRenderFrame;
            Unloaded += (_, _) => CompositionTarget.Rendering -= OnRenderFrame;
            LocaleManager.LocaleChanged += (_, _) => Dispatcher.Invoke(UpdateDateLabels);
            UpdateDateLabels();
        }

        private void DrawDialTicks()
        {
            TicksLayer.Children.Clear();
            Brush majorBrush = _isDark
                ? new SolidColorBrush(Color.FromRgb(245, 245, 247))
                : new SolidColorBrush(Color.FromRgb(28, 28, 30));
            Brush minorBrush = _isDark
                ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x44, 0x1C, 0x1C, 0x1E));

            for (int i = 0; i < 60; i++)
            {
                bool isHour = i % 5 == 0;
                double angle = i * 6.0;
                var tick = new Rectangle
                {
                    Width = isHour ? 2.4 : 1.0,
                    Height = isHour ? 8.5 : 4.0,
                    RadiusX = 1,
                    RadiusY = 1,
                    Fill = isHour ? majorBrush : minorBrush
                };
                Canvas.SetLeft(tick, 80 - tick.Width / 2.0);
                Canvas.SetTop(tick, 8);
                tick.RenderTransform = new RotateTransform(angle, tick.Width / 2.0, 72);
                TicksLayer.Children.Add(tick);
            }

            // Crisp bold Apple hour numerals (12, 3, 6, 9)
            (string Text, double X, double Y)[] numerals =
            {
                ("12", 71.5, 19),
                ("3", 131, 71),
                ("6", 75.5, 123),
                ("9", 21, 71)
            };

            foreach (var n in numerals)
            {
                var tb = new TextBlock
                {
                    Text = n.Text,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = majorBrush
                };
                Canvas.SetLeft(tb, n.X);
                Canvas.SetTop(tb, n.Y);
                TicksLayer.Children.Add(tb);
            }
        }

        private void OnRenderFrame(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            double smoothSeconds = now.Second + now.Millisecond / 1000.0;
            double smoothMinutes = now.Minute + smoothSeconds / 60.0;
            double smoothHours = (now.Hour % 12) + smoothMinutes / 60.0;

            SecondRotate.Angle = smoothSeconds * 6.0;
            MinuteRotate.Angle = smoothMinutes * 6.0;
            HourRotate.Angle = smoothHours * 30.0;

            DigitalTimeText.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);

            if (now.Millisecond < 40)
                UpdateDateLabels();
        }

        private void UpdateDateLabels()
        {
            var now = DateTime.Now;
            DialDateText.Text = now.ToString("ddd dd", CultureInfo.CurrentCulture).ToUpperInvariant();
            WideDateText.Text = now.ToString("dddd, MMM d", CultureInfo.CurrentCulture);
        }

        public void ApplySize(AppleWidgetSize size)
        {
            bool showDigital = size != AppleWidgetSize.Small;
            MediumRightCol.Width = showDigital ? new GridLength(1.15, GridUnitType.Star) : new GridLength(0);
            DigitalInfoPanel.Visibility = showDigital ? Visibility.Visible : Visibility.Collapsed;
            DialDatePill.Visibility = showDigital ? Visibility.Collapsed : Visibility.Visible;
        }

        public void ApplyTheme(bool isDark)
        {
            _isDark = isDark;
            var primary = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(28, 28, 30));
            var pillBg = isDark
                ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));

            DialFace.Fill = isDark
                ? new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0x00, 0x00));
            HourHand.Fill = primary;
            MinuteHand.Fill = primary;
            DigitalTimeText.Foreground = primary;
            WideDateText.Foreground = isDark
                ? new SolidColorBrush(Color.FromRgb(229, 229, 234))
                : new SolidColorBrush(Color.FromRgb(58, 58, 60));
            DialDatePill.Background = pillBg;
            WideDatePill.Background = pillBg;

            DrawDialTicks();
        }
    }
}
