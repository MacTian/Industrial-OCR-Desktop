# PP-OCRv5 标注训练功能实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 PaddleOcrDesktop WPF 应用中增加 OCR 标注与训练数据导出功能，支持手动标注文本区域、OCR 自动预标注、导出为 PaddleOCR 训练格式（det/rec），并提供 CPU/GPU 训练配置与启动能力。

**Architecture:** 新增 AnnotationView（标注视图）+ AnnotationViewModel（标注逻辑）+ AnnotationExportService（导出训练数据）。标注视图与现有 ImageView 集成，通过 Tab 或区域切换进入标注模式。标注数据模型存储多边形坐标和文本内容，导出时按 PaddleOCR 训练格式（det: JSON 行，rec: 裁剪图片+文本标签）生成。训练启动通过调用 PaddleOCR Python 脚本实现，支持 CPU/GPU 选择。

**Tech Stack:** WPF (现有)、CommunityToolkit.Mvvm (现有)、SkiaSharp (现有，用于裁剪)、System.Text.Json (标注序列化)、ClosedXML (现有)、Process.Start (启动训练)

---

## 文件结构总览

### 新建文件

| 文件 | 职责 |
|---|---|
| `Models/AnnotationData.cs` | 标注数据模型：AnnotationImage（一张图的所有标注）、AnnotationRegion（单个标注区域，含多边形点+文本+是否忽略） |
| `Models/TrainingConfig.cs` | 训练配置模型：训练目录、det/rec 模式、CPU/GPU、学习率、epochs、批大小等 |
| `Views/AnnotationView.xaml` | 标注视图：图片显示 + 多边形绘制 + 文本编辑 + 工具栏 |
| `Views/AnnotationView.xaml.cs` | 标注视图 code-behind：鼠标绘制多边形、编辑标注文本、删除/移动标注区域 |
| `ViewModels/AnnotationViewModel.cs` | 标注视图模型：标注增删改查、OCR 预标注、导入/导出标注、训练数据导出 |
| `Services/AnnotationExportService.cs` | 标注导出服务：导出 det 格式（JSON 行）、rec 格式（裁剪图片+标签文件）、生成训练目录结构 |
| `Services/TrainingService.cs` | 训练服务：生成训练配置 YAML、启动 PaddleOCR 训练进程、监控训练日志 |
| `Views/TrainingView.xaml` | 训练配置视图：选择训练目录、det/rec 模式、CPU/GPU、超参数、启动训练、查看日志 |
| `Views/TrainingView.xaml.cs` | 训练视图 code-behind |
| `ViewModels/TrainingViewModel.cs` | 训练视图模型：配置管理、训练启动/停止、日志输出 |

### 修改文件

| 文件 | 修改内容 |
|---|---|
| `MainWindow.xaml` | 工具栏增加"标注模式"和"训练"按钮，主内容区支持切换标注视图 |
| `MainWindow.xaml.cs` | 处理标注/训练模式切换 |
| `ViewModels/MainViewModel.cs` | 增加 OpenAnnotationCommand、OpenTrainingCommand |
| `Models/OcrResult.cs` | 可选：增加字段支持标注导入 |

---

## Task 1: 创建标注数据模型

**Files:**
- Create: `Models/AnnotationData.cs`
- Create: `Models/TrainingConfig.cs`

- [ ] **Step 1: 创建 AnnotationData.cs**

```csharp
// Models/AnnotationData.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace PaddleOcrDesktop.Models;

/// <summary>
/// 单个标注区域：多边形框 + 文本内容
/// </summary>
public class AnnotationRegion : INotifyPropertyChanged
{
    private int _id;
    private string _text = string.Empty;
    private bool _isIgnored;
    private ObservableCollection<Point> _points = new();
    private bool _isSelected;

    public int Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    /// <summary>
    /// 标注文本内容
    /// </summary>
    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    /// <summary>
    /// 是否忽略（对应 PaddleOCR 中的 ### 标记）
    /// </summary>
    public bool IsIgnored
    {
        get => _isIgnored;
        set => SetField(ref _isIgnored, value);
    }

    /// <summary>
    /// 多边形顶点（原始像素坐标）
    /// </summary>
    [JsonPropertyName("points")]
    public ObservableCollection<Point> Points
    {
        get => _points;
        set => SetField(ref _points, value);
    }

    /// <summary>
    /// 是否被选中（UI 状态，不序列化）
    /// </summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>
    /// 获取 PaddleOCR 格式的 points: [[x1,y1],[x2,y2],...]
    /// </summary>
    public int[][] GetPaddlePoints()
    {
        return Points.Select(p => new[] { (int)p.X, (int)p.Y }).ToArray();
    }

    /// <summary>
    /// 获取 PaddleOCR 格式的 transcription
    /// </summary>
    public string GetPaddleTranscription()
    {
        return IsIgnored ? "###" : Text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// 一张图片的所有标注
/// </summary>
public class AnnotationImage : INotifyPropertyChanged
{
    private string _imagePath = string.Empty;
    private int _nextId = 1;
    private ObservableCollection<AnnotationRegion> _regions = new();

    /// <summary>
    /// 图片路径（标注文件中存储的相对路径）
    /// </summary>
    public string ImagePath
    {
        get => _imagePath;
        set => SetField(ref _imagePath, value);
    }

    /// <summary>
    /// 图片宽度
    /// </summary>
    public int ImageWidth { get; set; }

    /// <summary>
    /// 图片高度
    /// </summary>
    public int ImageHeight { get; set; }

    /// <summary>
    /// 标注区域列表
    /// </summary>
    public ObservableCollection<AnnotationRegion> Regions
    {
        get => _regions;
        set => SetField(ref _regions, value);
    }

    public int NextId => _nextId++;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
```

- [ ] **Step 2: 创建 TrainingConfig.cs**

```csharp
// Models/TrainingConfig.cs
namespace PaddleOcrDesktop.Models;

/// <summary>
/// 训练类型
/// </summary>
public enum TrainingMode
{
    /// <summary>
    /// 检测模型训练
    /// </summary>
    Detection,
    /// <summary>
    /// 识别模型训练
    /// </summary>
    Recognition
}

/// <summary>
/// 训练设备
/// </summary>
public enum TrainingDevice
{
    CPU,
    GPU
}

/// <summary>
/// 训练配置
/// </summary>
public class TrainingConfig
{
    /// <summary>
    /// 训练数据目录
    /// </summary>
    public string DataDir { get; set; } = string.Empty;

    /// <summary>
    /// 标注文件路径（det 模式: 标签文件, rec 模式: 标签文件）
    /// </summary>
    public string LabelFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 训练类型
    /// </summary>
    public TrainingMode Mode { get; set; } = TrainingMode.Detection;

    /// <summary>
    /// 训练设备
    /// </summary>
    public TrainingDevice Device { get; set; } = TrainingDevice.CPU;

    /// <summary>
    /// GPU 设备 ID（多卡时指定）
    /// </summary>
    public int GpuDeviceId { get; set; } = 0;

    /// <summary>
    /// 学习率
    /// </summary>
    public double LearningRate { get; set; } = 0.001;

    /// <summary>
    /// 训练轮数
    /// </summary>
    public int Epochs { get; set; } = 100;

    /// <summary>
    /// 批大小
    /// </summary>
    public int BatchSize { get; set; } = 8;

    /// <summary>
    /// 输出目录
    /// </summary>
    public string OutputDir { get; set; } = "./output";

    /// <summary>
    /// 预训练模型目录（微调时使用）
    /// </summary>
    public string? PretrainedModelDir { get; set; }

    /// <summary>
    /// 字典文件路径（rec 模式必须）
    /// </summary>
    public string? DictFilePath { get; set; }

    /// <summary>
    /// 训练图片最短边（det 模式常用 640）
    /// </summary>
    public int ImageShapeShort { get; set; } = 640;

    /// <summary>
    /// 是否使用多进程数据加载
    /// </summary>
    public bool UseMultiprocess { get; set; } = false;

    /// <summary>
    /// 工作进程数
    /// </summary>
    public int NumWorkers { get; set; } = 2;
}
```

- [ ] **Step 3: 构建验证**

Run: `cd C:\Users\mtian\source\repos\PaddleOcrDesktop\PaddleOcrDesktop && dotnet build`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add Models/AnnotationData.cs Models/TrainingConfig.cs
git commit -m "feat: add annotation and training config models"
```

---

## Task 2: 创建标注导出服务

**Files:**
- Create: `Services/AnnotationExportService.cs`

- [ ] **Step 1: 创建 AnnotationExportService.cs**

```csharp
// Services/AnnotationExportService.cs
using System.IO;
using System.Text;
using System.Text.Json;
using PaddleOcrDesktop.Models;
using SkiaSharp;

namespace PaddleOcrDesktop.Services;

public class AnnotationExportService
{
    /// <summary>
    /// 导出检测训练数据
    /// 格式：每行 image_path\t[{"points":[[x,y],...],"transcription":"text"},...]
    /// </summary>
    public void ExportDetTrainingData(
        string labelFilePath,
        List<AnnotationImage> annotations,
        string outputImageDir)
    {
        var sb = new StringBuilder();

        foreach (var img in annotations)
        {
            if (img.Regions.Count == 0) continue;

            // 复制图片到输出目录
            var destPath = Path.Combine(outputImageDir, Path.GetFileName(img.ImagePath));
            if (!File.Exists(destPath))
            {
                File.Copy(img.ImagePath, destPath);
            }

            // 生成标注行
            var regions = img.Regions.Select(r => new
            {
                points = r.GetPaddlePoints(),
                transcription = r.GetPaddleTranscription()
            }).ToList();

            var json = JsonSerializer.Serialize(regions, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var relativePath = Path.GetFileName(img.ImagePath);
            sb.AppendLine($"{relativePath}\t{json}");
        }

        File.WriteAllText(labelFilePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 导出识别训练数据
    /// 为每张图的每个标注区域裁剪图片，生成 crop_img/ 目录和 rec_label.txt
    /// 格式：crop_img/img_001_00001.jpg\ttext
    /// </summary>
    public void ExportRecTrainingData(
        string labelFilePath,
        string outputImageDir,
        List<AnnotationImage> annotations)
    {
        var cropDir = Path.Combine(outputImageDir, "crop_img");
        Directory.CreateDirectory(cropDir);

        var sb = new StringBuilder();
        int globalIdx = 0;

        foreach (var img in annotations)
        {
            using var bmp = SKBitmap.Decode(img.ImagePath);
            if (bmp.IsEmpty) continue;

            foreach (var region in img.Regions)
            {
                if (region.IsIgnored || region.Points.Count < 3) continue;

                // 计算 bounding box
                var minX = (int)region.Points.Min(p => p.X);
                var minY = (int)region.Points.Min(p => p.Y);
                var maxX = (int)region.Points.Max(p => p.X);
                var maxY = (int)region.Points.Max(p => p.Y);

                // Clamp to image bounds
                minX = Math.Max(0, minX);
                minY = Math.Max(0, minY);
                maxX = Math.Min(bmp.Width, maxX);
                maxY = Math.Min(bmp.Height, maxY);

                var w = maxX - minX;
                var h = maxY - minY;
                if (w <= 0 || h <= 0) continue;

                // 裁剪
                using var cropBmp = new SKBitmap(w, h);
                using (var canvas = new SKCanvas(cropBmp))
                {
                    canvas.DrawBitmap(bmp,
                        new SKRectI(0, 0, w, h),
                        new SKRectI(minX, minY, maxX, maxY));
                }

                // 保存裁剪图片
                globalIdx++;
                var cropFileName = $"{Path.GetFileNameWithoutExtension(img.ImagePath)}_{globalIdx:D5}.jpg";
                var cropPath = Path.Combine(cropDir, cropFileName);

                using (var data = cropBmp.Encode(SKEncodedImageFormat.Jpeg, 90))
                using (var fs = File.Create(cropPath))
                {
                    data.SaveTo(fs);
                }

                // 标签行
                var relativeCropPath = $"crop_img/{cropFileName}";
                sb.AppendLine($"{relativeCropPath}\t{region.Text}");
            }
        }

        File.WriteAllText(labelFilePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 保存标注项目（JSON 格式，用于标注工具内部保存/加载）
    /// </summary>
    public void SaveAnnotationProject(string filePath, List<AnnotationImage> annotations)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(annotations, options);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// 加载标注项目
    /// </summary>
    public List<AnnotationImage> LoadAnnotationProject(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<AnnotationImage>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<AnnotationImage>();
    }

    /// <summary>
    /// 创建 PaddleOCR 训练目录结构
    /// </summary>
    public void CreateTrainingDirectoryStructure(string baseDir, TrainingMode mode)
    {
        if (mode == TrainingMode.Detection)
        {
            Directory.CreateDirectory(Path.Combine(baseDir, "train_data"));
            Directory.CreateDirectory(Path.Combine(baseDir, "eval_data"));
            // det 模式标签文件在根目录
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(baseDir, "train_data", "rec"));
            Directory.CreateDirectory(Path.Combine(baseDir, "eval_data", "rec"));
            Directory.CreateDirectory(Path.Combine(baseDir, "train_data", "rec", "crop_img"));
            Directory.CreateDirectory(Path.Combine(baseDir, "eval_data", "rec", "crop_img"));
        }
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `cd C:\Users\mtian\source\repos\PaddleOcrDesktop\PaddleOcrDesktop && dotnet build`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add Services/AnnotationExportService.cs
git commit -m "feat: add annotation export service for PaddleOCR training format"
```

---

## Task 3: 创建标注视图 (XAML)

**Files:**
- Create: `Views/AnnotationView.xaml`

- [ ] **Step 1: 创建 AnnotationView.xaml**

```xml
<!-- Views/AnnotationView.xaml -->
<UserControl x:Class="PaddleOcrDesktop.Views.AnnotationView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:PaddleOcrDesktop.ViewModels"
             d:DataContext="{d:DesignInstance Type=vm:AnnotationViewModel, IsDesignTimeCreatable=True}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- 工具栏 -->
        <ToolBarTray Grid.Row="0">
            <ToolBar>
                <Button Content="📂 打开图片" Command="{Binding OpenImageCommand}" Margin="4"/>
                <Button Content="📁 打开文件夹" Command="{Binding OpenFolderCommand}" Margin="4"/>
                <Separator/>
                <Button Content="✏️ 绘制标注" Command="{Binding StartDrawCommand}" Margin="4"
                        IsChecked="{Binding IsDrawingMode}"/>
                <Button Content="🔍 OCR预标注" Command="{Binding AutoAnnotateCommand}" Margin="4"/>
                <Button Content="🗑️ 删除选中" Command="{Binding DeleteSelectedCommand}" Margin="4"/>
                <Separator/>
                <Button Content="💾 保存标注" Command="{Binding SaveAnnotationsCommand}" Margin="4"/>
                <Button Content="📤 导出训练数据" Command="{Binding ExportTrainingDataCommand}" Margin="4"/>
            </ToolBar>
        </ToolBarTray>

        <!-- 主内容：图片标注区 + 标注列表 -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="250" MinWidth="200"/>
            </Grid.ColumnDefinitions>

            <!-- 左侧：图片标注画布 -->
            <Border Grid.Column="0" BorderBrush="Gray" BorderThickness="1" Margin="4">
                <ScrollViewer HorizontalScrollBarVisibility="Disabled"
                              VerticalScrollBarVisibility="Disabled"
                              x:Name="ImageScrollViewer">
                    <Viewbox x:Name="ImageViewbox"
                             Stretch="Uniform"
                             HorizontalAlignment="Center"
                             VerticalAlignment="Center"
                             StretchDirection="Both">
                        <Grid x:Name="ImageHostGrid">
                            <Image x:Name="DisplayImage"
                                   RenderOptions.BitmapScalingMode="HighQuality"/>
                            <Canvas x:Name="OverlayCanvas"
                                    Background="Transparent"
                                    MouseLeftButtonDown="OverlayCanvas_MouseLeftButtonDown"
                                    MouseMove="OverlayCanvas_MouseMove"
                                    MouseLeftButtonUp="OverlayCanvas_MouseLeftButtonUp"
                                    MouseRightButtonDown="OverlayCanvas_MouseRightButtonDown"/>
                        </Grid>
                    </Viewbox>
                </ScrollViewer>
            </Border>

            <GridSplitter Grid.Column="1" Width="4" HorizontalAlignment="Left"/>

            <!-- 右侧：标注列表 -->
            <GroupBox Grid.Column="1" Header="标注列表" Margin="4">
                <DataGrid ItemsSource="{Binding CurrentImage.Regions}"
                          SelectedItem="{Binding SelectedRegion}"
                          AutoGenerateColumns="False"
                          CanUserAddRows="False"
                          IsReadOnly="False"
                          SelectionMode="Single">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="ID" Binding="{Binding Id}" Width="40" IsReadOnly="True"/>
                        <DataGridTextColumn Header="文本" Binding="{Binding Text, UpdateSourceTrigger=PropertyChanged}" Width="*"/>
                        <DataGridCheckBoxColumn Header="忽略" Binding="{Binding IsIgnored}" Width="40"/>
                    </DataGrid.Columns>
                </DataGrid>
            </GroupBox>
        </Grid>

        <!-- 状态栏 -->
        <StatusBar Grid.Row="2">
            <StatusBarItem>
                <TextBlock Text="{Binding StatusMessage}"/>
            </StatusBarItem>
            <Separator/>
            <StatusBarItem>
                <TextBlock Text="{Binding AnnotationCountText}"/>
            </StatusBarItem>
        </StatusBar>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Commit**

```bash
git add Views/AnnotationView.xaml
git commit -m "feat: add annotation view XAML"
```

---

## Task 4: 创建标注视图 Code-Behind

**Files:**
- Create: `Views/AnnotationView.xaml.cs`

- [ ] **Step 1: 创建 AnnotationView.xaml.cs**

核心逻辑：
- 鼠标左键点击添加多边形顶点，双击完成一个标注区域
- 右键点击取消当前绘制
- 绘制过程中显示临时多边形和顶点
- 已有标注区域用不同颜色显示（选中/未选中）
- 点击已选中标注可选中

```csharp
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
    private Point _startPoint;
    private bool _isDrawing;
    private Polygon? _currentPolygon;
    private List<Ellipse> _vertexMarkers = new();
    private double _sourcePixelWidth;
    private double _sourcePixelHeight;
    private double _viewboxScale = 1.0;

    public AnnotationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ImageViewbox.SizeChanged += OnViewboxSizeChanged;
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
            };
        }
    }

    private void OnViewboxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecalculateScale();
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

        RecalculateScale();
    }

    private void RecalculateScale()
    {
        if (_sourcePixelWidth <= 0 || ImageViewbox.ActualWidth <= 0) return;
        double scaleX = ImageViewbox.ActualWidth / _sourcePixelWidth;
        double scaleY = ImageViewbox.ActualHeight / _sourcePixelHeight;
        _viewboxScale = Math.Min(scaleX, scaleY);
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

        // 绘制模式：添加顶点
        if (!_isDrawing)
        {
            // 开始新标注
            _isDrawing = true;
            _startPoint = pos;

            _currentPolygon = new Polygon
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0)),
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            OverlayCanvas.Children.Add(_currentPolygon);
        }

        // 添加顶点到当前多边形
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
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _currentPolygon == null) return;

        var pos = e.GetPosition(OverlayCanvas);

        // 实时更新多边形（最后一个点跟随鼠标）
        if (_viewModel?.CurrentRegion != null && _viewModel.CurrentRegion.Points.Count > 0)
        {
            var points = new PointCollection();
            // 除最后一个点外，其他点固定
            for (int i = 0; i < _viewModel.CurrentRegion.Points.Count; i++)
            {
                points.Add(_viewModel.CurrentRegion.Points[i]);
            }
            // 最后一个点跟随鼠标（临时）
            points.Add(pos);
            _currentPolygon.Points = points;
        }
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 不在这里结束绘制，需要双击完成
    }

    private void OverlayCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawing)
        {
            // 右键取消当前绘制
            CancelCurrentDrawing();
        }
    }

    private void CancelCurrentDrawing()
    {
        if (_currentPolygon != null)
        {
            OverlayCanvas.Children.Remove(_currentPolygon);
            _currentPolygon = null;
        }
        foreach (var marker in _vertexMarkers)
            OverlayCanvas.Children.Remove(marker);
        _vertexMarkers.Clear();

        _isDrawing = false;
        _viewModel?.CancelCurrentRegion();
        OverlayCanvas.ReleaseMouseCapture();
    }

    private void UpdateCurrentPolygonPoints()
    {
        if (_currentPolygon == null || _viewModel?.CurrentRegion == null) return;

        var points = new PointCollection();
        foreach (var p in _viewModel.CurrentRegion.Points)
        {
            points.Add(p);
        }
        _currentPolygon.Points = points;
    }

    // ─── 双击完成标注 ────────────────────────────────────────

    // 注意：双击通过 MouseLeftButtonDown 的 ClickCount 检测
    // 需要在 XAML 中不需要额外事件，在 code-behind 中处理
    // 实际上 WPF Canvas 没有 MouseDoubleClick，需要通过 ClickCount 判断

    // 修改 MouseLeftButtonDown 中的逻辑：
    // 如果 e.ClickCount == 2 且 _isDrawing，则完成标注

    private void SelectRegionAtPoint(Point point)
    {
        if (_viewModel?.CurrentImage == null) return;

        foreach (var region in _viewModel.CurrentImage.Regions)
        {
            if (IsPointInPolygon(point, region.Points))
            {
                region.IsSelected = true;
                _viewModel.SelectedRegion = region;
                RedrawAllAnnotations();
                return;
            }
        }
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
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Views/AnnotationView.xaml.cs
git commit -m "feat: add annotation view code-behind with polygon drawing"
```

---

## Task 5: 创建标注视图模型

**Files:**
- Create: `ViewModels/AnnotationViewModel.cs`

- [ ] **Step 1: 创建 AnnotationViewModel.cs**

```csharp
// ViewModels/AnnotationViewModel.cs
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PaddleOcrDesktop.Models;
using PaddleOcrDesktop.Services;

namespace PaddleOcrDesktop.ViewModels;

public partial class AnnotationViewModel : ViewModelBase
{
    private readonly OcrEngine _ocrEngine;
    private readonly AnnotationExportService _exportService;

    [ObservableProperty]
    private AnnotationImage? _currentImage;

    [ObservableProperty]
    private AnnotationRegion? _selectedRegion;

    [ObservableProperty]
    private bool _isDrawingMode;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private ObservableCollection<AnnotationImage> _allAnnotations = new();

    [ObservableProperty]
    private string _annotationCountText = "0 张图片 / 0 个标注";

    /// <summary>
    /// 当前正在绘制的标注区域（未完成）
    /// </summary>
    public AnnotationRegion? CurrentRegion { get; private set; }

    [ObservableProperty]
    private int _currentImageIndex;

    public AnnotationViewModel(OcrEngine ocrEngine)
    {
        _ocrEngine = ocrEngine;
        _exportService = new AnnotationExportService();
    }

    // ─── 图片加载 ─────────────────────────────────────────────

    [RelayCommand]
    private void OpenImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif|所有文件|*.*",
            Title = "选择图片",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            LoadImages(dialog.FileNames);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "选择图片文件夹" };

        if (dialog.ShowDialog() == true)
        {
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif" };
            var files = Directory.GetFiles(dialog.FolderName)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToArray();
            LoadImages(files);
        }
    }

    private void LoadImages(string[] fileNames)
    {
        foreach (var file in fileNames)
        {
            // 检查是否已存在
            if (AllAnnotations.Any(a => a.ImagePath == file)) continue;

            var annotation = new AnnotationImage
            {
                ImagePath = file,
                ImageWidth = 0,
                ImageHeight = 0
            };

            // 获取图片尺寸
            try
            {
                using var bmp = SkiaSharp.SKBitmap.Decode(file);
                if (!bmp.IsEmpty)
                {
                    annotation.ImageWidth = bmp.Width;
                    annotation.ImageHeight = bmp.Height;
                }
            }
            catch { }

            AllAnnotations.Add(annotation);
        }

        if (CurrentImage == null && AllAnnotations.Count > 0)
        {
            CurrentImage = AllAnnotations[0];
        }

        UpdateAnnotationCount();
        StatusMessage = $"已加载 {AllAnnotations.Count} 张图片";
    }

    // ─── 标注绘制 ─────────────────────────────────────────────

    [RelayCommand]
    private void StartDraw()
    {
        IsDrawingMode = !IsDrawingMode;
        if (!IsDrawingMode)
        {
            CancelCurrentRegion();
        }
    }

    public void AddPointToCurrentRegion(System.Windows.Point point)
    {
        if (CurrentImage == null) return;

        if (CurrentRegion == null)
        {
            CurrentRegion = new AnnotationRegion
            {
                Id = CurrentImage.NextId
            };
        }

        CurrentRegion.Points.Add(point);
    }

    public void FinishCurrentRegion(string text)
    {
        if (CurrentRegion == null || CurrentImage == null) return;
        if (CurrentRegion.Points.Count < 3)
        {
            CancelCurrentRegion();
            return;
        }

        CurrentRegion.Text = text;
        CurrentImage.Regions.Add(CurrentRegion);
        CurrentRegion = null;
        UpdateAnnotationCount();
        StatusMessage = $"标注区域 #{CurrentImage.Regions.Count} 已添加";
    }

    public void CancelCurrentRegion()
    {
        CurrentRegion = null;
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedRegion != null && CurrentImage != null)
        {
            CurrentImage.Regions.Remove(SelectedRegion);
            SelectedRegion = null;
            UpdateAnnotationCount();
        }
    }

    // ─── OCR 预标注 ───────────────────────────────────────────

    [RelayCommand]
    private async Task AutoAnnotate()
    {
        if (CurrentImage == null || !_ocrEngine.IsLoaded)
        {
            StatusMessage = "请先加载图片和 OCR 模型";
            return;
        }

        StatusMessage = "正在进行 OCR 预标注...";

        try
        {
            var result = await Task.Run(() => _ocrEngine.Recognize(CurrentImage.ImagePath));

            if (result.IsSuccess)
            {
                foreach (var region in result.Regions)
                {
                    var annotationRegion = new AnnotationRegion
                    {
                        Id = CurrentImage.NextId,
                        Text = region.Text,
                        Confidence = region.Confidence,
                        Points = new System.Collections.ObjectModel.ObservableCollection<System.Windows.Point>(region.Points)
                    };
                    CurrentImage.Regions.Add(annotationRegion);
                }

                UpdateAnnotationCount();
                StatusMessage = $"OCR 预标注完成: {result.Regions.Count} 个区域";
            }
            else
            {
                StatusMessage = $"OCR 预标注失败: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"OCR 预标注异常: {ex.Message}";
        }
    }

    // ─── 保存/加载标注项目 ────────────────────────────────────

    [RelayCommand]
    private void SaveAnnotations()
    {
        if (AllAnnotations.Count == 0)
        {
            StatusMessage = "没有标注数据可保存";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "标注项目|*.json",
            Title = "保存标注项目",
            FileName = "annotations"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _exportService.SaveAnnotationProject(dialog.FileName, AllAnnotations.ToList());
                StatusMessage = $"标注项目已保存: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
            }
        }
    }

    // ─── 导出训练数据 ─────────────────────────────────────────

    [RelayCommand]
    private void ExportTrainingData()
    {
        if (AllAnnotations.Count == 0)
        {
            StatusMessage = "没有标注数据可导出";
            return;
        }

        var dialog = new OpenFolderDialog { Title = "选择训练数据输出目录" };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var outputDir = dialog.FolderName;
                Directory.CreateDirectory(outputDir);

                // 导出 det 格式
                var detLabelFile = Path.Combine(outputDir, "det_train_label.txt");
                var detImageDir = Path.Combine(outputDir, "images");
                Directory.CreateDirectory(detImageDir);
                _exportService.ExportDetTrainingData(detLabelFile, AllAnnotations.ToList(), detImageDir);

                // 导出 rec 格式
                var recLabelFile = Path.Combine(outputDir, "rec_train_label.txt");
                var recImageDir = Path.Combine(outputDir, "rec_images");
                Directory.CreateDirectory(recImageDir);
                _exportService.ExportRecTrainingData(recLabelFile, recImageDir, AllAnnotations.ToList());

                StatusMessage = $"训练数据已导出到: {outputDir}";
                MessageBox.Show($"训练数据导出成功！\n\nDet 标签: {detLabelFile}\nRec 标签: {recLabelFile}\n\n目录结构：\n{outputDir}/\n  images/\n  det_train_label.txt\n  rec_images/\n    crop_img/\n  rec_train_label.txt",
                    "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"导出失败: {ex.Message}";
            }
        }
    }

    private void UpdateAnnotationCount()
    {
        int imgCount = AllAnnotations.Count;
        int regionCount = AllAnnotations.Sum(a => a.Regions.Count);
        AnnotationCountText = $"{imgCount} 张图片 / {regionCount} 个标注";
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `cd C:\Users\mtian\source\repos\PaddleOcrDesktop\PaddleOcrDesktop && dotnet build`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add ViewModels/AnnotationViewModel.cs
git commit -m "feat: add annotation view model with drawing, OCR pre-annotation and export"
```

---

## Task 6: 创建训练服务和训练视图

**Files:**
- Create: `Services/TrainingService.cs`
- Create: `Views/TrainingView.xaml`
- Create: `Views/TrainingView.xaml.cs`
- Create: `ViewModels/TrainingViewModel.cs`

- [ ] **Step 1: 创建 TrainingService.cs**

```csharp
// Services/TrainingService.cs
using System.Diagnostics;
using System.IO;
using System.Text;
using PaddleOcrDesktop.Models;

namespace PaddleOcrDesktop.Services;

public class TrainingService
{
    /// <summary>
    /// 生成 PaddleOCR 训练配置文件
    /// </summary>
    public string GenerateTrainingConfigYaml(TrainingConfig config)
    {
        var sb = new StringBuilder();

        if (config.Mode == TrainingMode.Detection)
        {
            sb.AppendLine("# PP-OCRv5 检测模型训练配置");
            sb.AppendLine($"Architecture:");
            sb.AppendLine($"  model_type: det");
            sb.AppendLine($"  algorithm: DB");
            sb.AppendLine($"  Backbone:");
            sb.AppendLine($"    name: PPLCNetV3");
            sb.AppendLine($"    scale: 0.5");
            sb.AppendLine($"  Neck:");
            sb.AppendLine($"    name: RSEFPN");
            sb.AppendLine($"    out_channels: 96");
            sb.AppendLine($"  Head:");
            sb.AppendLine($"    name: DBHead");
            sb.AppendLine($"    k: 50");
            sb.AppendLine();
            sb.AppendLine($"Loss:");
            sb.AppendLine($"  name: DBLoss");
            sb.AppendLine($"  alpha: 5");
            sb.AppendLine($"  beta: 10");
            sb.AppendLine($"  ohem_ratio: 3");
            sb.AppendLine();
            sb.AppendLine($"Optimizer:");
            sb.AppendLine($"  name: Adam");
            sb.AppendLine($"  lr:");
            sb.AppendLine($"    name: Cosine");
            sb.AppendLine($"    learning_rate: {config.LearningRate}");
            sb.AppendLine($"  regularizer:");
            sb.AppendLine($"    name: L2");
            sb.AppendLine($"    factor: 5.0e-05");
            sb.AppendLine();
            sb.AppendLine($"Train:");
            sb.AppendLine($"  dataset:");
            sb.AppendLine($"    name: SimpleDataSet");
            sb.AppendLine($"    data_dir: {Path.GetFullPath(config.DataDir)}");
            sb.AppendLine($"    label_file_list:");
            sb.AppendLine($"      - {Path.GetFullPath(config.LabelFilePath)}");
            sb.AppendLine($"    ratio_list: [1.0]");
            sb.AppendLine($"    transforms:");
            sb.AppendLine($"      - DecodeImage: {{img_mode: BGR, channel_first: false}}");
            sb.AppendLine($"      - DetLabelEncode: {{}}");
            sb.AppendLine($"      - KeepKeys: {{keep_keys: [image, polygons, ignore_tags]}}");
            sb.AppendLine($"  loader:");
            sb.AppendLine($"    shuffle: true");
            sb.AppendLine($"    batch_size_per_card: {config.BatchSize}");
            sb.AppendLine($"    drop_last: true");
            sb.AppendLine($"    num_workers: {config.NumWorkers}");
            sb.AppendLine();
            sb.AppendLine($"Eval:");
            sb.AppendLine($"  dataset:");
            sb.AppendLine($"    name: SimpleDataSet");
            sb.AppendLine($"    data_dir: {Path.GetFullPath(config.DataDir)}");
            sb.AppendLine($"    label_file_list:");
            sb.AppendLine($"      - {Path.GetFullPath(config.LabelFilePath)}");
            sb.AppendLine($"  loader:");
            sb.AppendLine($"    shuffle: false");
            sb.AppendLine($"    batch_size_per_card: 1");
            sb.AppendLine($"    drop_last: false");
            sb.AppendLine($"    num_workers: {config.NumWorkers}");
            sb.AppendLine();
            sb.AppendLine($"Global:");
            sb.AppendLine($"  output_dir: {Path.GetFullPath(config.OutputDir)}");
            sb.AppendLine($"  epoch_num: {config.Epochs}");
            sb.AppendLine($"  print_batch_step: 10");
            sb.AppendLine($"  save_epoch_step: 5");
            sb.AppendLine($"  eval_batch_step: [0, 100]");
            sb.AppendLine($"  save_inference_dir: {Path.Combine(config.OutputDir, "inference")}");
            sb.AppendLine($"  pretrained_model: {(string.IsNullOrEmpty(config.PretrainedModelDir) ? "" : Path.GetFullPath(config.PretrainedModelDir!))}");
        }
        else
        {
            sb.AppendLine("# PP-OCRv5 识别模型训练配置");
            sb.AppendLine($"Architecture:");
            sb.AppendLine($"  model_type: rec");
            sb.AppendLine($"  algorithm: SVTR_LCNet");
            sb.AppendLine($"  Backbone:");
            sb.AppendLine($"    name: PPLCNetV3");
            sb.AppendLine($"    scale: 0.5");
            sb.AppendLine($"  Head:");
            sb.AppendLine($"    name: MultiHead");
            sb.AppendLine($"    head_list:");
            sb.AppendLine($"      - CTCHead:");
            sb.AppendLine($"          Neck:");
            sb.AppendLine($"            name: svtr");
            sb.AppendLine($"            dims: 120");
            sb.AppendLine($"            depth: 2");
            sb.AppendLine($"            hidden_dims: 120");
            sb.AppendLine($"            use_guide: true");
            sb.AppendLine($"          Head:");
            sb.AppendLine($"            fc_decay: 0.00001");
            sb.AppendLine($"      - NRTRHead:");
            sb.AppendLine($"          nrtr_dim: 512");
            sb.AppendLine($"          max_text_length: 25");
            sb.AppendLine();
            sb.AppendLine($"Loss:");
            sb.AppendLine($"  name: MultiLoss");
            sb.AppendLine($"  loss_config_list:");
            sb.AppendLine($"    - CTCLoss: {{}}");
            sb.AppendLine($"    - NRTRLoss: {{}}");
            sb.AppendLine();
            sb.AppendLine($"Optimizer:");
            sb.AppendLine($"  name: Adam");
            sb.AppendLine($"  lr:");
            sb.AppendLine($"    name: Cosine");
            sb.AppendLine($"    learning_rate: {config.LearningRate}");
            sb.AppendLine($"  regularizer:");
            sb.AppendLine($"    name: L2");
            sb.AppendLine($"    factor: 5.0e-05");
            sb.AppendLine();
            sb.AppendLine($"Train:");
            sb.AppendLine($"  dataset:");
            sb.AppendLine($"    name: SimpleDataSet");
            sb.AppendLine($"    data_dir: {Path.GetFullPath(config.DataDir)}");
            sb.AppendLine($"    label_file_list:");
            sb.AppendLine($"      - {Path.GetFullPath(config.LabelFilePath)}");
            sb.AppendLine($"  loader:");
            sb.AppendLine($"    shuffle: true");
            sb.AppendLine($"    batch_size_per_card: {config.BatchSize}");
            sb.AppendLine($"    drop_last: true");
            sb.AppendLine($"    num_workers: {config.NumWorkers}");
            sb.AppendLine();
            sb.AppendLine($"Eval:");
            sb.AppendLine($"  dataset:");
            sb.AppendLine($"    name: SimpleDataSet");
            sb.AppendLine($"    data_dir: {Path.GetFullPath(config.DataDir)}");
            sb.AppendLine($"    label_file_list:");
            sb.AppendLine($"      - {Path.GetFullPath(config.LabelFilePath)}");
            sb.AppendLine($"  loader:");
            sb.AppendLine($"    shuffle: false");
            sb.AppendLine($"    batch_size_per_card: 1");
            sb.AppendLine($"    drop_last: false");
            sb.AppendLine($"    num_workers: {config.NumWorkers}");
            sb.AppendLine();
            sb.AppendLine($"Global:");
            sb.AppendLine($"  character_dict_path: {(string.IsNullOrEmpty(config.DictFilePath) ? "" : Path.GetFullPath(config.DictFilePath!))}");
            sb.AppendLine($"  max_text_length: 25");
            sb.AppendLine($"  output_dir: {Path.GetFullPath(config.OutputDir)}");
            sb.AppendLine($"  epoch_num: {config.Epochs}");
            sb.AppendLine($"  print_batch_step: 10");
            sb.AppendLine($"  save_epoch_step: 5");
            sb.AppendLine($"  eval_batch_step: [0, 100]");
            sb.AppendLine($"  pretrained_model: {(string.IsNullOrEmpty(config.PretrainedModelDir) ? "" : Path.GetFullPath(config.PretrainedModelDir!))}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 启动训练进程
    /// </summary>
    public Process? StartTraining(TrainingConfig config, string configYamlPath)
    {
        var deviceArg = config.Device == TrainingDevice.GPU
            ? $"--gpus={config.GpuDeviceId}"
            : "--gpus=";

        var args = $"tools/train.py -c \"{configYamlPath}\" {deviceArg}";

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = args,
            WorkingDirectory = GetPaddleOCRRepoPath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            var process = new Process { StartInfo = psi };
            process.Start();
            return process;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"启动训练失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 检查 PaddleOCR 环境是否可用
    /// </summary>
    public (bool ok, string message) CheckEnvironment()
    {
        // 检查 Python
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            var version = process?.StandardOutput.ReadToEnd().Trim();
            if (string.IsNullOrEmpty(version))
                return (false, "未找到 Python，请先安装 Python 3.8+");
        }
        catch
        {
            return (false, "未找到 Python，请先安装 Python 3.8+");
        }

        // 检查 PaddleOCR
        var paddlePath = GetPaddleOCRRepoPath();
        if (string.IsNullOrEmpty(paddlePath) || !Directory.Exists(paddlePath))
            return (false, "未找到 PaddleOCR 仓库，请设置 PaddleOCR 目录路径");

        var trainScript = Path.Combine(paddlePath, "tools", "train.py");
        if (!File.Exists(trainScript))
            return (false, $"未找到训练脚本: {trainScript}");

        return (true, "环境检查通过");
    }

    private string? GetPaddleOCRRepoPath()
    {
        // 从配置中读取，或使用默认路径
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "PaddleOCR");
        return Directory.Exists(defaultPath) ? defaultPath : null;
    }
}
```

- [ ] **Step 2: 创建 TrainingView.xaml**

```xml
<!-- Views/TrainingView.xaml -->
<UserControl x:Class="PaddleOcrDesktop.Views.TrainingView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:models="clr-namespace:PaddleOcrDesktop.Models"
             d:DesignHeight="600" d:DesignWidth="800">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="200"/>
        </Grid.RowDefinitions>

        <!-- 工具栏 -->
        <ToolBarTray Grid.Row="0">
            <ToolBar>
                <Button Content="▶️ 开始训练" Command="{Binding StartTrainingCommand}" Margin="4"/>
                <Button Content="⏹️ 停止训练" Command="{Binding StopTrainingCommand}" Margin="4"/>
                <Separator/>
                <Button Content="🔍 环境检查" Command="{Binding CheckEnvironmentCommand}" Margin="4"/>
                <Button Content="📋 生成配置" Command="{Binding GenerateConfigCommand}" Margin="4"/>
            </ToolBar>
        </ToolBarTray>

        <!-- 配置区 -->
        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <StackPanel Margin="16">
                <GroupBox Header="基本设置" Margin="0,0,0,12">
                    <Grid Margin="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="120"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="120"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>

                        <TextBlock Grid.Row="0" Grid.Column="0" Text="训练类型:" VerticalAlignment="Center" Margin="4"/>
                        <ComboBox Grid.Row="0" Grid.Column="1" Margin="4"
                                  SelectedItem="{Binding Config.Mode}"
                                  ItemsSource="{Binding Source={StaticResource TrainingModeValues}}"/>

                        <TextBlock Grid.Row="0" Grid.Column="2" Text="训练设备:" VerticalAlignment="Center" Margin="4"/>
                        <ComboBox Grid.Row="0" Grid.Column="3" Margin="4"
                                  SelectedItem="{Binding Config.Device}"
                                  ItemsSource="{Binding Source={StaticResource TrainingDeviceValues}}"/>

                        <TextBlock Grid.Row="1" Grid.Column="0" Text="数据目录:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2" Margin="4"
                                 Text="{Binding Config.DataDir}"/>
                        <Button Grid.Row="1" Grid.Column="3" Content="浏览..." Margin="4"
                                Command="{Binding BrowseDataDirCommand}"/>

                        <TextBlock Grid.Row="2" Grid.Column="0" Text="标签文件:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Margin="4"
                                 Text="{Binding Config.LabelFilePath}"/>
                        <Button Grid.Row="2" Grid.Column="3" Content="浏览..." Margin="4"
                                Command="{Binding BrowseLabelFileCommand}"/>

                        <TextBlock Grid.Row="3" Grid.Column="0" Text="输出目录:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="3" Grid.Column="1" Grid.ColumnSpan="2" Margin="4"
                                 Text="{Binding Config.OutputDir}"/>
                        <Button Grid.Row="3" Grid.Column="3" Content="浏览..." Margin="4"
                                Command="{Binding BrowseOutputDirCommand}"/>
                    </Grid>
                </GroupBox>

                <GroupBox Header="训练参数" Margin="0,0,0,12">
                    <Grid Margin="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="120"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="120"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>

                        <TextBlock Grid.Row="0" Grid.Column="0" Text="学习率:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="0" Grid.Column="1" Margin="4" Text="{Binding Config.LearningRate}"/>

                        <TextBlock Grid.Row="0" Grid.Column="2" Text="训练轮数:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="0" Grid.Column="3" Margin="4" Text="{Binding Config.Epochs}"/>

                        <TextBlock Grid.Row="1" Grid.Column="0" Text="批大小:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="1" Grid.Column="1" Margin="4" Text="{Binding Config.BatchSize}"/>

                        <TextBlock Grid.Row="1" Grid.Column="2" Text="工作进程:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="1" Grid.Column="3" Margin="4" Text="{Binding Config.NumWorkers}"/>

                        <TextBlock Grid.Row="2" Grid.Column="0" Text="GPU ID:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="2" Grid.Column="1" Margin="4" Text="{Binding Config.GpuDeviceId}"/>

                        <TextBlock Grid.Row="2" Grid.Column="2" Text="预训练模型:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="2" Grid.Column="3" Margin="4" Text="{Binding Config.PretrainedModelDir}"/>

                        <TextBlock Grid.Row="3" Grid.Column="0" Text="字典文件:" VerticalAlignment="Center" Margin="4"/>
                        <TextBox Grid.Row="3" Grid.Column="1" Grid.ColumnSpan="2" Margin="4"
                                 Text="{Binding Config.DictFilePath}"/>
                        <Button Grid.Row="3" Grid.Column="3" Content="浏览..." Margin="4"
                                Command="{Binding BrowseDictFileCommand}"/>
                    </Grid>
                </GroupBox>

                <GroupBox Header="状态" Margin="0,0,0,8">
                    <StackPanel Margin="8">
                        <TextBlock Text="{Binding StatusMessage}" FontWeight="Bold" Margin="4"/>
                        <ProgressBar Value="{Binding TrainingProgress}" Height="20" Margin="4"
                                     IsIndeterminate="{Binding IsTrainingRunning}"/>
                    </StackPanel>
                </GroupBox>
            </StackPanel>
        </ScrollViewer>

        <!-- 日志区 -->
        <GroupBox Grid.Row="2" Header="训练日志" Margin="4">
            <TextBox Text="{Binding TrainingLog, Mode=OneWay}"
                     IsReadOnly="True"
                     VerticalScrollBarVisibility="Auto"
                     HorizontalScrollBarVisibility="Auto"
                     TextWrapping="Wrap"
                     FontFamily="Consolas"
                     FontSize="11"
                     x:Name="LogTextBox"/>
        </GroupBox>
    </Grid>
</UserControl>
```

- [ ] **Step 3: 创建 TrainingView.xaml.cs**

```csharp
// Views/TrainingView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using PaddleOcrDesktop.ViewModels;

namespace PaddleOcrDesktop.Views;

public partial class TrainingView : UserControl
{
    public TrainingView()
    {
        InitializeComponent();
    }

    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LogTextBox != null)
        {
            LogTextBox.ScrollToEnd();
        }
    }
}
```

- [ ] **Step 4: 创建 TrainingViewModel.cs**

```csharp
// ViewModels/TrainingViewModel.cs
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PaddleOcrDesktop.Models;
using PaddleOcrDesktop.Services;

namespace PaddleOcrDesktop.ViewModels;

public partial class TrainingViewModel : ViewModelBase
{
    private readonly TrainingService _trainingService;

    [ObservableProperty]
    private TrainingConfig _config = new();

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private string _trainingLog = string.Empty;

    [ObservableProperty]
    private bool _isTrainingRunning;

    [ObservableProperty]
    private double _trainingProgress;

    private System.Diagnostics.Process? _trainingProcess;

    public TrainingViewModel()
    {
        _trainingService = new TrainingService();
        Config.DataDir = Path.Combine(Environment.CurrentDirectory, "train_data");
        Config.OutputDir = Path.Combine(Environment.CurrentDirectory, "output");
        Config.LabelFilePath = Path.Combine(Environment.CurrentDirectory, "train_data", "det_train_label.txt");
    }

    // ─── 浏览命令 ─────────────────────────────────────────────

    [RelayCommand]
    private void BrowseDataDir()
    {
        var dialog = new OpenFolderDialog { Title = "选择训练数据目录" };
        if (dialog.ShowDialog() == true)
            Config.DataDir = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseLabelFile()
    {
        var dialog = new OpenFileDialog { Filter = "标签文件|*.txt|所有文件|*.*", Title = "选择标签文件" };
        if (dialog.ShowDialog() == true)
            Config.LabelFilePath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowseOutputDir()
    {
        var dialog = new OpenFolderDialog { Title = "选择输出目录" };
        if (dialog.ShowDialog() == true)
            Config.OutputDir = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseDictFile()
    {
        var dialog = new OpenFileDialog { Filter = "字典文件|*.txt|所有文件|*.*", Title = "选择字典文件" };
        if (dialog.ShowDialog() == true)
            Config.DictFilePath = dialog.FileName;
    }

    // ─── 训练控制 ─────────────────────────────────────────────

    [RelayCommand]
    private void CheckEnvironment()
    {
        var (ok, message) = _trainingService.CheckEnvironment();
        StatusMessage = message;
        MessageBox.Show(message, ok ? "检查通过" : "检查失败",
            MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    [RelayCommand]
    private void GenerateConfig()
    {
        if (string.IsNullOrEmpty(Config.DataDir))
        {
            StatusMessage = "请先设置数据目录";
            return;
        }

        try
        {
            var yaml = _trainingService.GenerateTrainingConfigYaml(Config);
            var configPath = Path.Combine(Config.OutputDir, $"train_config_{Config.Mode.ToString().ToLower()}.yaml");
            Directory.CreateDirectory(Config.OutputDir);
            File.WriteAllText(configPath, yaml);
            StatusMessage = $"配置已生成: {configPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"生成配置失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartTraining()
    {
        if (IsTrainingRunning)
        {
            StatusMessage = "训练已在运行中";
            return;
        }

        if (string.IsNullOrEmpty(Config.DataDir) || string.IsNullOrEmpty(Config.LabelFilePath))
        {
            StatusMessage = "请先设置数据目录和标签文件";
            return;
        }

        try
        {
            // 先生成配置文件
            Directory.CreateDirectory(Config.OutputDir);
            var configPath = Path.Combine(Config.OutputDir, $"train_config_{Config.Mode.ToString().ToLower()}.yaml");
            var yaml = _trainingService.GenerateTrainingConfigYaml(Config);
            File.WriteAllText(configPath, yaml);

            IsTrainingRunning = true;
            TrainingLog = $"[{System.DateTime.Now:HH:mm:ss}] 启动训练...\n";
            TrainingLog += $"[{System.DateTime.Now:HH:mm:ss}] 配置: {configPath}\n";
            TrainingLog += $"[{System.DateTime.Now:HH:mm:ss}] 设备: {(Config.Device == TrainingDevice.GPU ? $"GPU {Config.GpuDeviceId}" : "CPU")}\n";
            TrainingLog += $"[{System.DateTime.Now:HH:mm:ss}] 模式: {Config.Mode}\n";
            TrainingLog += $"[{System.DateTime.Now:HH:mm:ss}] 数据: {Config.DataDir}\n";
            TrainingLog += $"[{System.DateTime.Now:HH:mm:ss}] 标签: {Config.LabelFilePath}\n\n";

            _trainingProcess = _trainingService.StartTraining(Config, configPath);

            if (_trainingProcess == null)
            {
                StatusMessage = "启动训练进程失败";
                IsTrainingRunning = false;
                return;
            }

            // 异步读取输出
            _ = Task.Run(() =>
            {
                while (!_trainingProcess.HasExited)
                {
                    var line = _trainingProcess.StandardOutput.ReadLine();
                    if (line != null)
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            TrainingLog += line + "\n";
                        });
                    }
                }

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsTrainingRunning = false;
                    StatusMessage = $"训练完成，退出码: {_trainingProcess.ExitCode}";
                    TrainingLog += $"\n[{System.DateTime.Now:HH:mm:ss}] 训练结束，退出码: {_trainingProcess.ExitCode}\n";
                });
            });

            StatusMessage = "训练已启动";
        }
        catch (Exception ex)
        {
            IsTrainingRunning = false;
            StatusMessage = $"启动训练失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StopTraining()
    {
        if (_trainingProcess != null && !_trainingProcess.HasExited)
        {
            try
            {
                _trainingProcess.Kill();
                TrainingLog += $"\n[{System.DateTime.Now:HH:mm:ss}] 训练已手动停止\n";
                StatusMessage = "训练已停止";
            }
            catch (Exception ex)
            {
                StatusMessage = $"停止失败: {ex.Message}";
            }
        }
        IsTrainingRunning = false;
    }
}
```

- [ ] **Step 5: 构建验证**

Run: `cd C:\Users\mtian\source/repos/PaddleOcrDesktop/PaddleOcrDesktop && dotnet build`
Expected: Build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add Services/TrainingService.cs Views/TrainingView.xaml Views/TrainingView.xaml.cs ViewModels/TrainingViewModel.cs
git commit -m "feat: add training service and training view with CPU/GPU support"
```

---

## Task 7: 在主窗口中集成标注和训练功能

**Files:**
- Modify: `MainWindow.xaml`
- Modify: `MainWindow.xaml.cs`
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: 修改 MainWindow.xaml — 增加 Tab 切换**

在现有三栏布局上方增加 TabControl，支持"识别模式"和"标注模式"切换：

```xml
<!-- MainWindow.xaml 修改：在 Grid.Row="1" 处 -->
<Grid Grid.Row="1">
    <TabControl x:ModeTabControl">
        <TabItem Header="🔍 识别模式">
            <!-- 现有的三栏布局 -->
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="200" MinWidth="150"/>
                    <ColumnDefinition Width="5"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="5"/>
                    <ColumnDefinition Width="300" MinWidth="200"/>
                </Grid.ColumnDefinitions>
                <!-- 文件列表、图片预览、识别结果 — 保持不变 -->
            </Grid>
        </TabItem>
        <TabItem Header="✏️ 标注模式">
            <views:AnnotationView x:Name="AnnotationViewControl"/>
        </TabItem>
        <TabItem Header="🏋️ 训练">
            <views:TrainingView x:Name="TrainingViewControl"/>
        </TabItem>
    </TabControl>
</Grid>
```

- [ ] **Step 2: 修改 MainWindow.xaml.cs — 初始化标注和训练 ViewModel**

```csharp
// 在 MainWindow 构造函数中增加
var ocrEngine = new OcrEngine(modelPath);
var annotationVm = new AnnotationViewModel(ocrEngine);
AnnotationViewControl.DataContext = annotationVm;

var trainingVm = new TrainingViewModel();
TrainingViewControl.DataContext = trainingVm;
```

- [ ] **Step 3: 修改工具栏增加模式切换按钮**

```xml
<!-- 在 ToolBar 中增加 -->
<Separator/>
<Button Content="✏️ 标注模式" Command="{Binding SwitchToAnnotationCommand}" Margin="4"/>
<Button Content="🏋️ 训练" Command="{Binding SwitchToTrainingCommand}" Margin="4"/>
```

- [ ] **Step 4: 构建验证**

Run: `dotnet build`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs ViewModels/MainViewModel.cs
git commit -m "feat: integrate annotation and training tabs into main window"
```

---

## Task 8: 添加 App.xaml 资源

**Files:**
- Modify: `App.xaml`

- [ ] **Step 1: 在 App.xaml 中添加枚举值资源**

```xml
<Application.Resources>
    <ResourceDictionary>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
        <views:FileNameConverter x:Key="FileNameConv" xmlns:views="clr-namespace:PaddleOcrDesktop.Views"/>
        
        <!-- 训练模式枚举值 -->
        <ObjectDataProvider x:Key="TrainingModeValues" MethodName="GetValues"
                            ObjectType="{x:Type sys:Enum}" xmlns:sys="clr-namespace:System;assembly=mscorlib">
            <ObjectDataProvider.MethodParameters>
                <x:Type TypeName="models:TrainingMode" xmlns:models="clr-namespace:PaddleOcrDesktop.Models"/>
            </ObjectDataProvider.MethodParameters>
        </ObjectDataProvider>
        
        <!-- 训练设备枚举值 -->
        <ObjectDataProvider x:Key="TrainingDeviceValues" MethodName="GetValues"
                            ObjectType="{x:Type sys:Enum}" xmlns:sys="clr-namespace:System;assembly=mscorlib">
            <ObjectDataProvider.MethodParameters>
                <x:Type TypeName="models:TrainingDevice" xmlns:models="clr-namespace:PaddleOcrDesktop.Models"/>
            </ObjectDataProvider.MethodParameters>
        </ObjectDataProvider>
    </ResourceDictionary>
</Application.Resources>
```

- [ ] **Step 2: 构建验证 + Commit**

Run: `dotnet build`
Expected: Build succeeded

```bash
git add App.xaml
git commit -m "maint: add enum resources for training view combo boxes"
```

---

## Task 9: 修复 AnnotationView 双击完成标注

**Files:**
- Modify: `Views/AnnotationView.xaml.cs`

- [ ] **Step 1: 在 MouseLeftButtonDown 中增加双击检测**

当前代码中 `OverlayCanvas_MouseLeftButtonDown` 只处理单击添加顶点。需要检测 `e.ClickCount == 2` 来完成标注：

在 `OverlayCanvas_MouseLeftButtonDown` 方法中，在添加顶点之后增加：

```csharp
// 如果双击且已有至少3个点，完成标注
if (e.ClickCount == 2 && CurrentRegion != null && CurrentRegion.Points.Count >= 3)
{
    // 弹出输入框让用户输入文本
    var inputDialog = new TextInputDialog("请输入标注文本:", CurrentRegion.Text ?? "");
    if (inputDialog.ShowDialog() == true)
    {
        FinishCurrentRegion(inputDialog.InputText);
    }
    else
    {
        CancelCurrentDrawing();
    }
    RedrawAllAnnotations();
    return;
}
```

- [ ] **Step 2: 创建 TextInputDialog**

Create `Views/TextInputDialog.xaml` 和 `.xaml.cs` — 简单的文本输入对话框。

- [ ] **Step 3: 构建验证 + Commit**

Run: `dotnet build`
Expected: Build succeeded

```bash
git add Views/AnnotationView.xaml.cs Views/TextInputDialog.xaml Views/TextInputDialog.xaml.cs
git commit -m "feat: add double-click to finish annotation and text input dialog"
```

---

## Task 10: 整体构建测试

- [ ] **Step 1: 完整构建**

Run: `cd C:\Users\mtian\source/repos/PaddleOcrDesktop/PaddleOcrDesktop && dotnet build`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: 运行应用验证**

Run: `dotnet run`
Expected: 应用启动，三个 Tab 页都能正常显示

- [ ] **Step 3: 功能验证清单**

- [ ] 识别模式：正常打开图片、识别、显示结果
- [ ] 标注模式：打开图片、绘制多边形、OCR 预标注、保存标注
- [ ] 导出训练数据：选择目录、生成 det/rec 格式文件
- [ ] 训练页面：配置参数、生成 YAML、环境检查
- [ ] ROI 功能：在识别模式下正常选择 ROI 并识别

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "feat: complete annotation and training feature set"
```

---

## PaddleOCR 训练指南（附录）

### 环境准备

1. 安装 Python 3.8+
2. 克隆 PaddleOCR 仓库：`git clone https://github.com/PaddlePaddle/PaddleOCR.git`
3. 安装依赖：`pip install -r PaddleOCR/requirements.txt`
4. GPU 训练额外需要：`pip install paddlepaddle-gpu`

### 训练步骤

1. 使用本工具的标注模式标注图片
2. 导出训练数据（自动生成 det 和 rec 格式）
3. 在训练页面配置参数并启动训练
4. 或使用生成的 YAML 配置文件手动运行：
   ```bash
   # CPU 训练
   python tools/train.py -c train_config_det.yaml --gpus=
   
   # GPU 训练
   python tools/train.py -c train_config_det.yaml --gpus=0
   ```

### 目录结构

```
train_data/
  images/                    # 原始图片
  det_train_label.txt        # 检测标签（JSON 格式）
  rec_images/
    crop_img/                # 裁剪的文本区域图片
  rec_train_label.txt        # 识别标签（image_path\ttext）
output/                      # 训练输出
  train_config_det.yaml      # 生成的配置文件
  train_config_rec.yaml
  best_accuracy/             # 最优模型
  latest/                    # 最新模型
```
