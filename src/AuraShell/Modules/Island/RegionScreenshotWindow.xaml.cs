using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AuraShell.Localization;

namespace AuraShell.Modules.Island
{
    public partial class RegionScreenshotWindow : Window
    {
        private System.Windows.Point _startPoint;
        private bool _isDragging;

        public event EventHandler<string>? RegionCaptured;

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public RegionScreenshotWindow()
        {
            InitializeComponent();
            FlowDirection = LocaleManager.CurrentFlowDirection;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(SelectionCanvas);
            _isDragging = true;
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _startPoint.X);
            Canvas.SetTop(SelectionRect, _startPoint.Y);
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var current = e.GetPosition(SelectionCanvas);
            double x = Math.Min(_startPoint.X, current.X);
            double y = Math.Min(_startPoint.Y, current.Y);
            double w = Math.Abs(current.X - _startPoint.X);
            double h = Math.Abs(current.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            ReleaseMouseCapture();

            double x = Canvas.GetLeft(SelectionRect);
            double y = Canvas.GetTop(SelectionRect);
            double w = SelectionRect.Width;
            double h = SelectionRect.Height;

            if (w < 8 || h < 8)
            {
                Close();
                return;
            }

            // Convert DIPs to physical screen pixels
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int px = (int)Math.Round((Left + x) * dpiX);
            int py = (int)Math.Round((Top + y) * dpiY);
            int pw = Math.Max(1, (int)Math.Round(w * dpiX));
            int ph = Math.Max(1, (int)Math.Round(h * dpiY));

            Hide();
            System.Threading.Thread.Sleep(100);

            try
            {
                using var bmp = new Bitmap(pw, ph, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(px, py, 0, 0, new System.Drawing.Size(pw, ph), CopyPixelOperation.SourceCopy);
                }

                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AuraShell");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"AuraRegion_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bmp.Save(file, ImageFormat.Png);

                IntPtr hBitmap = bmp.GetHbitmap();
                try
                {
                    var bmpSrc = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bmpSrc.Freeze();
                    Clipboard.SetImage(bmpSrc);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }

                RegionCaptured?.Invoke(this, file);
            }
            catch { }
            finally
            {
                Close();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }
    }
}
