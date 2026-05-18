// MainWindow.xaml.cs
using System.Windows;
using PaddleOcrDesktop.Services;
using PaddleOcrDesktop.ViewModels;

namespace PaddleOcrDesktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // 连接 ROI 事件
        ImageViewControl.RoiSelected += (s, roi) => _viewModel.SetRoi(roi);

        // 连接识别结果到识别框绘制
        _viewModel.ResultViewModel.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(ResultViewModel.Regions))
            {
                Dispatcher.Invoke(() =>
                {
                    ImageViewControl.ClearRecognitionRegions();
                    if (_viewModel.ResultViewModel.Regions.Count > 0)
                    {
                        ImageViewControl.DrawRecognitionRegions(
                            _viewModel.ResultViewModel.Regions.ToList());
                    }
                });
            }
        };

        // 初始化标注 VM（共享主 VM 的 OcrEngine，避免重复加载模型）
        var annotationVm = new AnnotationViewModel(_viewModel.OcrEngine);
        AnnotationViewControl.DataContext = annotationVm;

        // 初始化训练 VM
        var trainingVm = new TrainingViewModel();
        TrainingViewControl.DataContext = trainingVm;
    }
}
