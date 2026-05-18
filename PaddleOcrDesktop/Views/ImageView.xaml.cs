// Views/ImageView.xaml.cs
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PaddleOcrDesktop.Models;
using PaddleOcrDesktop.ViewModels;

namespace PaddleOcrDesktop.Views;

public partial class ImageView : UserControl
{
    private Point _startPoint;
    private bool _isDrawing;
    private Rectangle? _roiRect;

    private double _sourcePixelWidth;
    private double _sourcePixelHeight;

    /// <summary>
    /// Viewbox 的缩放比：显示坐标 = 原始坐标 * Scale
    /// 由于 OverlayCanvas 和 Image 在同一个 Grid 中，坐标空间就是原始像素
    /// 但 Viewbox 会缩放整个 Grid，所以 OverlayCanvas 上的绘制也会被缩放
    /// </summary>
    private double _viewboxScale = 1.0;

    public event EventHandler<Rect>? RoiSelected;

    public ImageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;

        // Viewbox 尺寸变化时重新计算 scale
        ImageViewbox.SizeChanged += OnViewboxSizeChanged;
    }

    private void OnViewboxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            RecalculateScale();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
        {
            vm.ImageViewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ImageViewModel.CurrentImagePath))
                {
                    Dispatcher.Invoke(() => LoadImage(vm.ImageViewModel.CurrentImagePath));
                }
            };
        }
    }

    private void LoadImage(string path)
    {
        OverlayCanvas.Children.Clear();
        ClearRecognitionRegions();
        _roiRect = null;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            DisplayImage.Source = null;
            _sourcePixelWidth = 0;
            _sourcePixelHeight = 0;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            // Image 不设置 Stretch，让它保持原始像素尺寸
            // Viewbox 会负责缩放
            DisplayImage.Stretch = Stretch.None;
            DisplayImage.Source = bitmap;
            _sourcePixelWidth = bitmap.PixelWidth;
            _sourcePixelHeight = bitmap.PixelHeight;

            // 设置 ImageHostGrid 尺寸为图片原始像素尺寸
            ImageHostGrid.Width = bitmap.PixelWidth;
            ImageHostGrid.Height = bitmap.PixelHeight;

            // OverlayCanvas 也设置为相同尺寸
            OverlayCanvas.Width = bitmap.PixelWidth;
            OverlayCanvas.Height = bitmap.PixelHeight;

            System.Diagnostics.Debug.WriteLine($"[IMG] 加载: {System.IO.Path.GetFileName(path)} ({bitmap.PixelWidth}x{bitmap.PixelHeight})");

            // 等布局完成后计算 scale
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RecalculateScale();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            DisplayImage.Source = null;
            _sourcePixelWidth = 0;
            _sourcePixelHeight = 0;
            System.Diagnostics.Debug.WriteLine($"[IMG] 加载失败: {ex.Message}");
        }
    }

    private void RecalculateScale()
    {
        if (_sourcePixelWidth <= 0 || _sourcePixelHeight <= 0) return;
        if (ImageViewbox.ActualWidth <= 0 || ImageViewbox.ActualHeight <= 0) return;

        double scaleX = ImageViewbox.ActualWidth / _sourcePixelWidth;
        double scaleY = ImageViewbox.ActualHeight / _sourcePixelHeight;
        _viewboxScale = Math.Min(scaleX, scaleY);

        System.Diagnostics.Debug.WriteLine($"[IMG] ViewboxScale={_viewboxScale:F4} Viewbox={ImageViewbox.ActualWidth:F0}x{ImageViewbox.ActualHeight:F0}");
    }

    // ─── ROI 鼠标交互 ───────────────────────────────────────────

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = true;
        _startPoint = e.GetPosition(OverlayCanvas);

        _roiRect = new Rectangle
        {
            Stroke = Brushes.Red,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)),
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };

        Canvas.SetLeft(_roiRect, _startPoint.X);
        Canvas.SetTop(_roiRect, _startPoint.Y);
        OverlayCanvas.Children.Add(_roiRect);
        OverlayCanvas.CaptureMouse();
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _roiRect == null) return;

        var currentPoint = e.GetPosition(OverlayCanvas);
        var x = Math.Min(_startPoint.X, currentPoint.X);
        var y = Math.Min(_startPoint.Y, currentPoint.Y);
        var w = Math.Abs(currentPoint.X - _startPoint.X);
        var h = Math.Abs(currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(_roiRect, x);
        Canvas.SetTop(_roiRect, y);
        _roiRect.Width = w;
        _roiRect.Height = h;
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        OverlayCanvas.ReleaseMouseCapture();

        if (_roiRect != null && _roiRect.Width > 5 && _roiRect.Height > 5)
        {
            // OverlayCanvas 的坐标空间就是原始像素，不需要换算
            var x = Canvas.GetLeft(_roiRect);
            var y = Canvas.GetTop(_roiRect);
            var w = _roiRect.Width;
            var h = _roiRect.Height;

            RoiSelected?.Invoke(this, new Rect(x, y, w, h));
            System.Diagnostics.Debug.WriteLine($"[ROI] 选择区域: ({x:F0},{y:F0},{w:F0},{h:F0})");
        }
        else if (_roiRect != null)
        {
            OverlayCanvas.Children.Remove(_roiRect);
            _roiRect = null;
        }
    }

    // ─── 拖拽支持 ───────────────────────────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif" };
        var imageFiles = files.Where(f => extensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToList();

        if (imageFiles.Count > 0 && DataContext is MainViewModel vm)
        {
            vm.ImageFiles.Clear();
            foreach (var file in imageFiles)
                vm.ImageFiles.Add(file);
            vm.SelectedImageFile = imageFiles[0];
        }

        e.Handled = true;
    }

    // ─── 识别结果绘制 ───────────────────────────────────────────

    /// <summary>
    /// 在图片上绘制检测框和识别文本
    /// OverlayCanvas 的坐标空间 = 原始像素坐标，Viewbox 会自动缩放
    /// </summary>
    public void DrawRecognitionRegions(List<TextRegion> regions)
    {
        ClearRecognitionRegions();

        if (_sourcePixelWidth <= 0 || _sourcePixelHeight <= 0)
        {
            System.Diagnostics.Debug.WriteLine("[DRAW] 跳过: 图片未加载");
            return;
        }

        // 确保 Canvas 尺寸正确
        OverlayCanvas.Width = _sourcePixelWidth;
        OverlayCanvas.Height = _sourcePixelHeight;

        System.Diagnostics.Debug.WriteLine($"[DRAW] 绘制 {regions.Count} 个区域, Canvas={OverlayCanvas.Width:F0}x{OverlayCanvas.Height:F0}");

        int drawn = 0;
        foreach (var region in regions)
        {
            if (region.Points == null || region.Points.Length < 3) continue;

            // 原始坐标直接用于 Canvas，Viewbox 会统一缩放
            var points = region.Points;

            var polygon = new Polygon
            {
                Points = new PointCollection(points),
                Stroke = region.IsValid ? Brushes.LimeGreen : Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0)),
                Tag = "ocr"
            };

            OverlayCanvas.Children.Add(polygon);

            // 文本标签
            var minX = points.Min(p => p.X);
            var minY = points.Min(p => p.Y);

            var label = new TextBlock
            {
                Text = $"{region.Text} ({region.Confidence:P0})",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                FontSize = 12,
                Padding = new Thickness(3, 1, 3, 1),
                Tag = "ocr"
            };

            Canvas.SetLeft(label, minX);
            Canvas.SetTop(label, minY - 18);
            OverlayCanvas.Children.Add(label);

            drawn++;
            System.Diagnostics.Debug.WriteLine($"[DRAW] #{drawn}: \"{region.Text}\" conf={region.Confidence:F3} pos=({minX:F0},{minY:F0})");
        }

        System.Diagnostics.Debug.WriteLine($"[DRAW] 完成: {drawn}/{regions.Count} 个区域");
    }

    /// <summary>
    /// 清除识别框
    /// </summary>
    public void ClearRecognitionRegions()
    {
        var oldItems = OverlayCanvas.Children.OfType<FrameworkElement>()
            .Where(e => e.Tag?.ToString() == "ocr")
            .ToList();
        foreach (var item in oldItems)
            OverlayCanvas.Children.Remove(item);
    }
}
