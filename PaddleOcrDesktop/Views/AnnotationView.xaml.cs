// Views/AnnotationView.xaml.cs
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

public partial class AnnotationView : UserControl
{
    private AnnotationViewModel? _viewModel;
    private bool _isDrawing;
    private Polygon? _currentPolygon;
    private readonly List<Ellipse> _vertexMarkers = new();
    private double _sourcePixelWidth;
    private double _sourcePixelHeight;

    public AnnotationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// 键盘快捷键：左右箭头切换图片，Delete 删除选中标注
    /// </summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null) return;

        if (e.Key == Key.Right && _viewModel.NextImageCommand.CanExecute(null))
        {
            _viewModel.NextImageCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && _viewModel.PrevImageCommand.CanExecute(null))
        {
            _viewModel.PrevImageCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _viewModel.DeleteSelectedCommand.CanExecute(null))
        {
            _viewModel.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 图片列表点击切换
    /// </summary>
    private void ImageList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is AnnotationViewModel vm && vm.CurrentImage != null)
        {
            LoadImage(vm.CurrentImage);
            RedrawAllAnnotations();
        }
    }

    /// <summary>
    /// 标注列表单元格编辑结束时重绘叠加层（文本/忽略状态变更）
    /// </summary>
    private void AnnotationDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            RedrawAllAnnotations();
        }
    }

    /// <summary>
    /// 标注列表选中项变化时同步 IsSelected 并重绘叠加层
    /// </summary>
    private void AnnotationDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel?.CurrentImage == null) return;

        // 同步所有区域的 IsSelected 状态
        var selected = _viewModel.SelectedRegion;
        foreach (var region in _viewModel.CurrentImage.Regions)
        {
            region.IsSelected = region == selected;
        }

        RedrawAllAnnotations();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is AnnotationViewModel vm)
        {
            _viewModel = vm;
            _viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(AnnotationViewModel.CurrentImage))
                {
                    Dispatcher.Invoke(() =>
                    {
                        LoadImage(vm.CurrentImage);
                        RedrawAllAnnotations();
                    });
                }
                else if (args.PropertyName == nameof(AnnotationViewModel.SelectedRegion))
                {
                    // 选中项变化时重绘（高亮/取消高亮）
                    Dispatcher.Invoke(RedrawAllAnnotations);
                }
            };
        }
    }

    private void LoadImage(AnnotationImage? image)
    {
        OverlayCanvas.Children.Clear();
        _currentPolygon = null;
        _vertexMarkers.Clear();

        if (image == null || string.IsNullOrEmpty(image.ImagePath) || !File.Exists(image.ImagePath))
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
            bitmap.UriSource = new Uri(image.ImagePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            DisplayImage.Source = bitmap;
            DisplayImage.Stretch = Stretch.None;
            _sourcePixelWidth = bitmap.PixelWidth;
            _sourcePixelHeight = bitmap.PixelHeight;

            ImageHostGrid.Width = bitmap.PixelWidth;
            ImageHostGrid.Height = bitmap.PixelHeight;
            OverlayCanvas.Width = bitmap.PixelWidth;
            OverlayCanvas.Height = bitmap.PixelHeight;

            System.Diagnostics.Debug.WriteLine($"[ANN] 加载: {System.IO.Path.GetFileName(image.ImagePath)} ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
        }
        catch (Exception ex)
        {
            DisplayImage.Source = null;
            _sourcePixelWidth = 0;
            _sourcePixelHeight = 0;
            System.Diagnostics.Debug.WriteLine($"[ANN] 加载失败: {ex.Message}");
        }
    }

    // ─── 鼠标绘制多边形 ──────────────────────────────────────

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel == null) return;

        var pos = e.GetPosition(OverlayCanvas);

        if (!_viewModel.IsDrawingMode)
        {
            // 非绘制模式：检查是否点击了已有标注区域
            SelectRegionAtPoint(pos);
            return;
        }

        // 绘制模式
        if (!_isDrawing)
        {
            // 开始新标注
            _isDrawing = true;
            _viewModel.StartNewRegion();

            _currentPolygon = new Polygon
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0)),
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            OverlayCanvas.Children.Add(_currentPolygon);
        }

        // 添加顶点
        _viewModel.AddPointToCurrentRegion(new System.Windows.Point(pos.X, pos.Y));

        // 添加顶点标记
        var marker = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Brushes.Yellow,
            Stroke = Brushes.Black,
            StrokeThickness = 1
        };
        Canvas.SetLeft(marker, pos.X - 4);
        Canvas.SetTop(marker, pos.Y - 4);
        OverlayCanvas.Children.Add(marker);
        _vertexMarkers.Add(marker);

        UpdateCurrentPolygonPoints();
        OverlayCanvas.CaptureMouse();

        // 双击完成标注
        if (e.ClickCount == 2 && _viewModel.CurrentRegion != null && _viewModel.CurrentRegion.Points.Count >= 3)
        {
            FinishCurrentRegion();
        }
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _currentPolygon == null || _viewModel?.CurrentRegion == null) return;

        var pos = e.GetPosition(OverlayCanvas);

        // 实时更新多边形（最后一个点跟随鼠标）
        var points = new PointCollection();
        for (int i = 0; i < _viewModel.CurrentRegion.Points.Count; i++)
        {
            points.Add(_viewModel.CurrentRegion.Points[i]);
        }
        points.Add(pos);
        _currentPolygon.Points = points;
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 不在这里结束绘制，双击完成
    }

    private void OverlayCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawing)
        {
            CancelCurrentDrawing();
        }
    }

    private void FinishCurrentRegion()
    {
        if (_viewModel?.CurrentRegion == null) return;
        if (_viewModel.CurrentRegion.Points.Count < 3)
        {
            CancelCurrentDrawing();
            return;
        }

        // 弹出文本输入对话框
        var dialog = new TextInputDialog("请输入标注文本:", _viewModel.CurrentRegion.Text ?? "");
        if (dialog.ShowDialog() == true)
        {
            _viewModel.FinishCurrentRegion(dialog.InputText);
        }
        else
        {
            CancelCurrentDrawing();
            return;
        }

        ClearCurrentDrawingVisuals();
        _isDrawing = false;
        RedrawAllAnnotations();
    }

    private void CancelCurrentDrawing()
    {
        _viewModel?.CancelCurrentRegion();
        ClearCurrentDrawingVisuals();
        _isDrawing = false;
        OverlayCanvas.ReleaseMouseCapture();
    }

    private void ClearCurrentDrawingVisuals()
    {
        if (_currentPolygon != null)
        {
            OverlayCanvas.Children.Remove(_currentPolygon);
            _currentPolygon = null;
        }
        foreach (var marker in _vertexMarkers)
            OverlayCanvas.Children.Remove(marker);
        _vertexMarkers.Clear();
    }

    private void UpdateCurrentPolygonPoints()
    {
        if (_currentPolygon == null || _viewModel?.CurrentRegion == null) return;
        var points = new PointCollection();
        foreach (var p in _viewModel.CurrentRegion.Points)
            points.Add(p);
        _currentPolygon.Points = points;
    }

    // ─── 选择标注区域 ────────────────────────────────────────

    private void SelectRegionAtPoint(Point point)
    {
        if (_viewModel?.CurrentImage == null) return;

        // 取消所有选中
        foreach (var region in _viewModel.CurrentImage.Regions)
            region.IsSelected = false;

        foreach (var region in _viewModel.CurrentImage.Regions)
        {
            if (region.Points.Count >= 3 && IsPointInPolygon(point, region.Points))
            {
                region.IsSelected = true;
                _viewModel.SelectedRegion = region;
                RedrawAllAnnotations();
                return;
            }
        }

        // 没有点击任何区域，取消选中
        _viewModel.SelectedRegion = null;
        RedrawAllAnnotations();
    }

    private static bool IsPointInPolygon(Point point, System.Collections.ObjectModel.ObservableCollection<System.Windows.Point> polygon)
    {
        if (polygon.Count < 3) return false;

        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    // ─── 重绘所有标注 ────────────────────────────────────────

    public void RedrawAllAnnotations()
    {
        if (_viewModel?.CurrentImage == null) return;

        // 清除旧的标注绘制（保留当前正在绘制的）
        var toRemove = OverlayCanvas.Children.OfType<FrameworkElement>()
            .Where(e => e.Tag?.ToString() == "annotation")
            .ToList();
        foreach (var item in toRemove)
            OverlayCanvas.Children.Remove(item);

        foreach (var region in _viewModel.CurrentImage.Regions)
        {
            if (region.Points.Count < 3) continue;

            var brush = region.IsSelected
                ? new SolidColorBrush(Color.FromArgb(60, 0, 255, 255))
                : new SolidColorBrush(Color.FromArgb(40, 0, 255, 0));

            var strokeBrush = region.IsIgnored
                ? Brushes.Gray
                : (region.IsSelected ? Brushes.Cyan : Brushes.LimeGreen);

            var polygon = new Polygon
            {
                Points = new PointCollection(region.Points),
                Stroke = strokeBrush,
                StrokeThickness = 2,
                Fill = brush,
                Tag = "annotation"
            };
            OverlayCanvas.Children.Add(polygon);

            // 文本标签
            var minX = region.Points.Min(p => p.X);
            var minY = region.Points.Min(p => p.Y);
            var label = new TextBlock
            {
                Text = $"#{region.Id} {region.Text}",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                FontSize = 11,
                Padding = new Thickness(2, 0, 2, 0),
                Tag = "annotation"
            };
            Canvas.SetLeft(label, minX);
            Canvas.SetTop(label, minY - 16);
            OverlayCanvas.Children.Add(label);
        }

        // 确保 OverlayCanvas 尺寸正确
        OverlayCanvas.Width = _sourcePixelWidth;
        OverlayCanvas.Height = _sourcePixelHeight;
    }
}
