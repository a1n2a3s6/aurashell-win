using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AuraShell.Localization;
using AuraShell.Native;

namespace AuraShell.Modules.Island
{
    public partial class EyedropperOverlayWindow : Window
    {
        private string _currentHex = "#000000";

        public event EventHandler<string>? ColorPicked;

        public EyedropperOverlayWindow()
        {
            InitializeComponent();
            FlowDirection = LocaleManager.CurrentFlowDirection;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            var (color, hex) = Win32Shell.GetCursorPixelColor();
            _currentHex = hex;
            HexLabel.Text = hex;
            ColorSwatch.Background = new SolidColorBrush(color);

            Point pos = e.GetPosition(OverlayCanvas);
            Canvas.SetLeft(SwatchBadge, Math.Min(pos.X + 20, ActualWidth - 260));
            Canvas.SetTop(SwatchBadge, Math.Min(pos.Y + 20, ActualHeight - 70));
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var (_, hex) = Win32Shell.GetCursorPixelColor();
            _currentHex = hex;
            ColorPicked?.Invoke(this, _currentHex);
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }
    }
}
