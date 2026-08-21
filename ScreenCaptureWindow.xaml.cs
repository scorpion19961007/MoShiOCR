using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MoShiOCR;

public partial class ScreenCaptureWindow : Window
{
    private readonly Bitmap _screenBitmap;
    private System.Windows.Point _start;
    private bool _selecting;

    public byte[]? CapturedBytes { get; private set; }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public ScreenCaptureWindow()
    {
        InitializeComponent();
        var area = System.Windows.Forms.SystemInformation.VirtualScreen;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _screenBitmap = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(_screenBitmap))
            graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
        ScreenImage.Source = ToBitmapSource(_screenBitmap);

        Loaded += (_, _) =>
        {
            Focus();
            UpdateDimGeometry(null);
        };
        Closed += (_, _) => _screenBitmap.Dispose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Overlay);
        _selecting = true;
        SelectionBorder.Visibility = SizeBadge.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateSelection(_start);
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_selecting) UpdateSelection(e.GetPosition(Overlay));
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting) return;
        var end = e.GetPosition(Overlay);
        _selecting = false;
        ReleaseMouseCapture();

        var dipRect = Normalize(_start, end);
        if (dipRect.Width < 8 || dipRect.Height < 8)
        {
            SelectionBorder.Visibility = SizeBadge.Visibility = Visibility.Collapsed;
            UpdateDimGeometry(null);
            return;
        }

        var scaleX = _screenBitmap.Width / ActualWidth;
        var scaleY = _screenBitmap.Height / ActualHeight;
        var pixelRect = new System.Drawing.Rectangle(
            Math.Clamp((int)Math.Round(dipRect.X * scaleX), 0, _screenBitmap.Width - 1),
            Math.Clamp((int)Math.Round(dipRect.Y * scaleY), 0, _screenBitmap.Height - 1),
            Math.Clamp((int)Math.Round(dipRect.Width * scaleX), 1, _screenBitmap.Width),
            Math.Clamp((int)Math.Round(dipRect.Height * scaleY), 1, _screenBitmap.Height));
        pixelRect.Width = Math.Min(pixelRect.Width, _screenBitmap.Width - pixelRect.X);
        pixelRect.Height = Math.Min(pixelRect.Height, _screenBitmap.Height - pixelRect.Y);

        using var cropped = _screenBitmap.Clone(pixelRect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var stream = new MemoryStream();
        cropped.Save(stream, ImageFormat.Png);
        CapturedBytes = stream.ToArray();
        DialogResult = true;
    }

    private void UpdateSelection(System.Windows.Point current)
    {
        var rect = Normalize(_start, current);
        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);
        SelectionBorder.Width = rect.Width;
        SelectionBorder.Height = rect.Height;

        var scaleX = _screenBitmap.Width / Math.Max(1, ActualWidth);
        var scaleY = _screenBitmap.Height / Math.Max(1, ActualHeight);
        SizeText.Text = $"{Math.Round(rect.Width * scaleX)} × {Math.Round(rect.Height * scaleY)}";
        SizeBadge.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(SizeBadge, Math.Min(Math.Max(0, rect.X), Math.Max(0, ActualWidth - SizeBadge.DesiredSize.Width)));
        Canvas.SetTop(SizeBadge, rect.Y > 34 ? rect.Y - 31 : Math.Min(ActualHeight - 30, rect.Bottom + 6));
        UpdateDimGeometry(rect);
    }

    private void UpdateDimGeometry(System.Windows.Rect? selection)
    {
        var full = new RectangleGeometry(new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));
        if (selection is null || selection.Value.IsEmpty)
        {
            DimPath.Data = full;
            return;
        }
        var geometry = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(selection.Value));
        geometry.Freeze();
        DimPath.Data = geometry;
    }

    private static System.Windows.Rect Normalize(System.Windows.Point a, System.Windows.Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { DeleteObject(handle); }
    }
}
