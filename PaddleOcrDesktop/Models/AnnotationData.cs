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
