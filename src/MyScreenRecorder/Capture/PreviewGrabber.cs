using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MyScreenRecorder.Native;

namespace MyScreenRecorder.Capture;

public sealed class PreviewGrabber
{
    private const int MaxThumbWidth = 384;
    private const int MaxThumbHeight = 216;

    private readonly Action<BitmapSource> _onFrame;
    private readonly DispatcherTimer _timer;
    private System.Drawing.Bitmap? _source;
    private System.Drawing.Bitmap? _thumb;

    public PreviewGrabber(Action<BitmapSource> onFrame)
    {
        _onFrame = onFrame;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += OnTick;
    }

    public CaptureTarget? Target { get; set; }

    public void Start()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void Stop()
    {
        _timer.Stop();
        DisposeBitmaps();
    }

    private void DisposeBitmaps()
    {
        _source?.Dispose();
        _source = null;
        _thumb?.Dispose();
        _thumb = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var target = Target;
        if (target == null)
        {
            return;
        }

        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var bounds = target.Bounds;
            int w = bounds.Width;
            int h = bounds.Height;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            if (_source == null || _source.Width != w || _source.Height != h)
            {
                _source?.Dispose();
                _source = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }

            double scale = Math.Min(1.0, Math.Min((double)MaxThumbWidth / w, (double)MaxThumbHeight / h));
            int tw = Math.Max(1, (int)Math.Round(w * scale));
            int th = Math.Max(1, (int)Math.Round(h * scale));

            if (_thumb == null || _thumb.Width != tw || _thumb.Height != th)
            {
                _thumb?.Dispose();
                _thumb = new System.Drawing.Bitmap(tw, th, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }

            using (var g = System.Drawing.Graphics.FromImage(_source))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(w, h), System.Drawing.CopyPixelOperation.SourceCopy);
            }

            using (var g = System.Drawing.Graphics.FromImage(_thumb))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(_source, new System.Drawing.Rectangle(0, 0, tw, th), 0, 0, w, h, System.Drawing.GraphicsUnit.Pixel);
            }

            hBitmap = _thumb.GetHbitmap();
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            _onFrame(source);
        }
        catch (Exception)
        {
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                Win32.DeleteObject(hBitmap);
            }
        }
    }
}
