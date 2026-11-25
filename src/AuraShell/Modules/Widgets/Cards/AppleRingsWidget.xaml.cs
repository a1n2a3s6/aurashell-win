using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AuraShell.Services;

namespace AuraShell.Modules.Widgets.Cards
{
    public partial class AppleRingsWidget : UserControl, IAppleWidgetCard
    {
        public string WidgetId => "rings";
        public bool UsesCustomSkyBackdrop => false;

        public AppleRingsWidget()
        {
            InitializeComponent();
        }

        public void UpdateTelemetry(TelemetrySnapshot snap)
        {
            CpuPctText.Text = $"{snap.CpuPercent}%";
            RamPctText.Text = $"{snap.RamPercent}%";
            BatPctText.Text = snap.HasBattery
                ? (snap.IsCharging ? $"⚡{snap.BatteryPercent}" : $"{snap.BatteryPercent}%")
                : "AC";
            DiskPctText.Text = $"{snap.DiskPercent}%";

            CpuArc.Data = BuildArcGeometry(snap.CpuPercent, 64, 6);
            RamArc.Data = BuildArcGeometry(snap.RamPercent, 64, 6);
            BatArc.Data = BuildArcGeometry(snap.BatteryPercent, 64, 6);
            DiskArc.Data = BuildArcGeometry(snap.DiskPercent, 64, 6);
        }

        private static Geometry BuildArcGeometry(double percent, double size, double strokeThickness)
        {
            double clamped = Math.Clamp(percent, 1.0, 99.9);
            double radius = (size - strokeThickness) / 2.0;
            double cx = size / 2.0;
            double cy = size / 2.0;

            double startAngle = -90.0;
            double sweep = clamped / 100.0 * 360.0;
            double endAngleRad = (startAngle + sweep) * Math.PI / 180.0;

            Point startPt = new Point(cx, cy - radius);
            Point endPt = new Point(cx + radius * Math.Cos(endAngleRad), cy + radius * Math.Sin(endAngleRad));

            var fig = new PathFigure { StartPoint = startPt, IsClosed = false };
            fig.Segments.Add(new ArcSegment
            {
                Point = endPt,
                Size = new Size(radius, radius),
                IsLargeArc = sweep > 180.0,
                SweepDirection = SweepDirection.Clockwise
            });

            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            geo.Freeze();
            return geo;
        }

        public void ApplySize(AppleWidgetSize size)
        {
            if (size == AppleWidgetSize.Small)
            {
                RingsGrid.Columns = 2;
                RingsGrid.Rows = 2;
                HeaderTitle.Visibility = Visibility.Collapsed;
            }
            else if (size == AppleWidgetSize.Large)
            {
                RingsGrid.Columns = 2;
                RingsGrid.Rows = 2;
                HeaderTitle.Visibility = Visibility.Visible;
            }
            else
            {
                RingsGrid.Columns = 4;
                RingsGrid.Rows = 1;
                HeaderTitle.Visibility = Visibility.Visible;
            }
        }

        public void ApplyTheme(bool isDark)
        {
            var fg = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(28, 28, 30));
            CpuPctText.Foreground = fg;
            RamPctText.Foreground = fg;
            BatPctText.Foreground = fg;
            DiskPctText.Foreground = fg;
        }
    }
}
