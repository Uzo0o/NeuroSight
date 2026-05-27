using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using DirectShowLib;
using NeuroSight.Core;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NeuroSight.Views
{
    public partial class SetupView : UserControl
    {
        private VideoCapture? _capture;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly AppConfig _config;

        private bool _isDrawing = false;
        private Avalonia.Point _startPoint;
        private Rectangle? _currentRectShape;

        public SetupView()
        {
            InitializeComponent();
            _config = AppConfig.Load();
            LoadCameras();
            RedrawSavedRegions();
        }

        private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(DrawCanvas).Properties.IsLeftButtonPressed) return;
            _isDrawing = true;
            _startPoint = e.GetPosition(DrawCanvas);

            _currentRectShape = new Rectangle
            {
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 0, 255, 0))
            };
            Canvas.SetLeft(_currentRectShape, _startPoint.X);
            Canvas.SetTop(_currentRectShape, _startPoint.Y);
            DrawCanvas.Children.Add(_currentRectShape);
        }

        private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDrawing || _currentRectShape == null) return;
            Avalonia.Point currentPoint = e.GetPosition(DrawCanvas);
            double x = Math.Min(currentPoint.X, _startPoint.X);
            double y = Math.Min(currentPoint.Y, _startPoint.Y);
            double width = Math.Max(currentPoint.X, _startPoint.X) - x;
            double height = Math.Max(currentPoint.Y, _startPoint.Y) - y;
            Canvas.SetLeft(_currentRectShape, x);
            Canvas.SetTop(_currentRectShape, y);
            _currentRectShape.Width = width;
            _currentRectShape.Height = height;
        }

        private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDrawing || _currentRectShape == null) return;
            _isDrawing = false;

            if (_currentRectShape.Width < 10 || _currentRectShape.Height < 10)
            {
                DrawCanvas.Children.Remove(_currentRectShape);
                _currentRectShape = null;
                return;
            }

            string selectedLabel = (RegionTypeDropdown.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Unknown";
            double x = Canvas.GetLeft(_currentRectShape);
            double y = Canvas.GetTop(_currentRectShape);

            _config.Regions.Add(new CaptureRegion
            {
                Label = selectedLabel,
                X = x,
                Y = y,
                Width = _currentRectShape.Width,
                Height = _currentRectShape.Height
            });
            _config.Save();

            var labelTag = new TextBlock
            {
                Text = selectedLabel,
                Foreground = Brushes.LimeGreen,
                FontWeight = FontWeight.Bold,
                FontSize = 14
            };
            Canvas.SetLeft(labelTag, x);
            Canvas.SetTop(labelTag, y - 20);
            DrawCanvas.Children.Add(labelTag);

            _currentRectShape = null;
            if (StatusText != null) StatusText.Text = $"Saved region '{selectedLabel}'. Change dropdown to draw another.";
        }

        private void RedrawSavedRegions()
        {
            DrawCanvas.Children.Clear();
            foreach (var region in _config.Regions)
            {
                var rect = new Rectangle
                {
                    Stroke = Brushes.Orange,
                    StrokeThickness = 2,
                    Width = region.Width,
                    Height = region.Height
                };
                Canvas.SetLeft(rect, region.X);
                Canvas.SetTop(rect, region.Y);
                DrawCanvas.Children.Add(rect);

                var label = new TextBlock { Text = region.Label, Foreground = Brushes.Orange, FontWeight = FontWeight.Bold };
                Canvas.SetLeft(label, region.X);
                Canvas.SetTop(label, region.Y - 20);
                DrawCanvas.Children.Add(label);
            }
        }

        private void LoadCameras()
        {
            CameraDropdown.Items.Clear();
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            if (devices.Length == 0) return;

            for (int i = 0; i < devices.Length; i++)
                CameraDropdown.Items.Add(new CameraInfo { Index = i, Name = devices[i].Name });

            if (_config.SelectedCameraIndex < devices.Length)
                CameraDropdown.SelectedIndex = _config.SelectedCameraIndex;
            else
                CameraDropdown.SelectedIndex = 0;
        }

        private void BtnStart_Click(object? sender, RoutedEventArgs e)
        {
            if (_capture != null && _capture.IsOpened()) return;

            if (CameraDropdown.SelectedItem is CameraInfo selectedCamera)
            {
                _config.SelectedCameraIndex = selectedCamera.Index;
                _config.Save();

                _capture = new VideoCapture(selectedCamera.Index, VideoCaptureAPIs.DSHOW);
                _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
                _capture.Set(VideoCaptureProperties.FrameHeight, 1080);

                if (!_capture.IsOpened())
                {
                    StatusText.Text = "Failed to open camera.";
                    return;
                }

                double actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                double actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                DrawCanvas.Width = actualWidth > 0 ? actualWidth : 1920;
                DrawCanvas.Height = actualHeight > 0 ? actualHeight : 1080;

                _cancellationTokenSource = new CancellationTokenSource();
                Task.Run(() => CaptureLoop(_cancellationTokenSource.Token));
                StatusText.Text = $"Camera feed running at {DrawCanvas.Width}x{DrawCanvas.Height}. Draw your crop boxes.";
            }
        }

        private void CaptureLoop(CancellationToken token)
        {
            using var frame = new Mat();
            while (!token.IsCancellationRequested)
            {
                if (_capture!.Read(frame) && !frame.Empty())
                {
                    using var clonedFrame = frame.Clone();
                    var bitmap = ConvertMatToAvaloniaBitmap(clonedFrame);
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        VideoFeedDisplay.Source = bitmap;
                    });
                }
                Thread.Sleep(30);
            }
        }

        private void BtnStop_Click(object? sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
            VideoFeedDisplay.Source = null;
        }

        private void BtnClear_Click(object? sender, RoutedEventArgs e)
        {
            _config.Regions.Clear();
            _config.Save();
            DrawCanvas.Children.Clear();
            _isDrawing = false;
            _currentRectShape = null;
            if (StatusText != null) StatusText.Text = "All regions cleared. You can now draw new crop boxes.";
        }

        private Avalonia.Media.Imaging.WriteableBitmap ConvertMatToAvaloniaBitmap(Mat mat)
        {
            using var bgraMat = new Mat();
            Cv2.CvtColor(mat, bgraMat, ColorConversionCodes.BGR2BGRA);
            var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(bgraMat.Width, bgraMat.Height),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.PixelFormat.Bgra8888);
            using (var lockedFramebuffer = bitmap.Lock())
            {
                unsafe
                {
                    Buffer.MemoryCopy(bgraMat.Data.ToPointer(), lockedFramebuffer.Address.ToPointer(), bgraMat.Total() * bgraMat.ElemSize(), bgraMat.Total() * bgraMat.ElemSize());
                }
            }
            return bitmap;
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            BtnStop_Click(null, null);
        }
    }

    public class CameraInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }
}
