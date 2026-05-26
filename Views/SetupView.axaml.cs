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

        private bool _isDrawing = false;
        private Avalonia.Point _startPoint;
        private Rectangle? _currentRectShape;

        public SetupView()
        {
            InitializeComponent();
            LoadCameras();
            RedrawSavedRegions(); 
        }

        // ==========================================
        // MOUSE LOGIC & DYNAMIC LABELS
        // ==========================================

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

            // DYNAMIC LABEL: Grab the text from whatever the user currently selected in the dropdown
            string selectedLabel = (RegionTypeDropdown.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Unknown";

            var finalBounds = new Avalonia.Rect(
                Canvas.GetLeft(_currentRectShape), 
                Canvas.GetTop(_currentRectShape), 
                _currentRectShape.Width, 
                _currentRectShape.Height);

            AppState.Regions.Add(new CaptureRegion { Label = selectedLabel, Bounds = finalBounds });

            var labelTag = new TextBlock
            {
                Text = selectedLabel,
                Foreground = Brushes.LimeGreen,
                FontWeight = FontWeight.Bold,
                FontSize = 14
            };
            Canvas.SetLeft(labelTag, finalBounds.X);
            Canvas.SetTop(labelTag, finalBounds.Y - 20); 
            DrawCanvas.Children.Add(labelTag);

            _currentRectShape = null;
            if(StatusText != null) StatusText.Text = $"Saved region '{selectedLabel}'. Change the dropdown to draw another.";
        }

        private void RedrawSavedRegions()
        {
            DrawCanvas.Children.Clear();
            foreach (var region in AppState.Regions)
            {
                var rect = new Rectangle
                {
                    Stroke = Brushes.Orange, 
                    StrokeThickness = 2,
                    Width = region.Bounds.Width,
                    Height = region.Bounds.Height
                };
                Canvas.SetLeft(rect, region.Bounds.X);
                Canvas.SetTop(rect, region.Bounds.Y);
                DrawCanvas.Children.Add(rect);
                
                var label = new TextBlock { Text = region.Label, Foreground = Brushes.Orange, FontWeight = FontWeight.Bold };
                Canvas.SetLeft(label, region.Bounds.X);
                Canvas.SetTop(label, region.Bounds.Y - 20);
                DrawCanvas.Children.Add(label);
            }
        }

        // ==========================================
        // CAMERA FEED FIXES
        // ==========================================

        private void LoadCameras()
        {
            CameraDropdown.Items.Clear();
            DsDevice[] captureDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            if (captureDevices.Length == 0) return;

            for (int i = 0; i < captureDevices.Length; i++)
                CameraDropdown.Items.Add(new CameraInfo { Index = i, Name = captureDevices[i].Name });

            CameraDropdown.SelectedIndex = 0;
        }

        private void BtnStart_Click(object? sender, RoutedEventArgs e)
        {
            if (_capture != null && _capture.IsOpened()) return;

            if (CameraDropdown.SelectedItem is CameraInfo selectedCamera)
            {
                AppState.SelectedCameraIndex = selectedCamera.Index;

                _capture = new VideoCapture(selectedCamera.Index, VideoCaptureAPIs.DSHOW);
                
                // 1. THE RESOLUTION FIX: Request 1080p, but use the MJPG codec to prevent the black screen crash!
                _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
                _capture.Set(VideoCaptureProperties.FrameHeight, 1080);

                if (!_capture.IsOpened())
                {
                    StatusText.Text = "Failed to open camera.";
                    return;
                }
                
                // 2. THE CROP FIX: Ask the camera what resolution it actually booted up in
                double actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                double actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);

                // 3. Lock the Canvas to that exact physical pixel size
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
                    // FIX: Clone the frame so Avalonia's UI thread doesn't conflict with OpenCV's memory map
                    using var clonedFrame = frame.Clone();
                    var bitmap = ConvertMatToAvaloniaBitmap(clonedFrame);
                    
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                    {
                        VideoFeedDisplay.Source = bitmap;
                    });
                }
                // FIX: Slowed down slightly to 30ms (~30 FPS) to prevent UI thread flooding
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
            // 1. Wipe the saved crops from the Global State memory
            AppState.Regions.Clear();

            // 2. Erase the physical green/orange boxes and text labels from the screen
            DrawCanvas.Children.Clear();

            // 3. Reset the mouse drawing state just in case they clicked this mid-drag
            _isDrawing = false;
            _currentRectShape = null;

            // 4. Update the UI text
            if (StatusText != null) 
            {
                StatusText.Text = "All regions cleared. You can now draw new crop boxes.";
            }
        }

        private Avalonia.Media.Imaging.WriteableBitmap ConvertMatToAvaloniaBitmap(Mat mat)
        {
            using var bgraMat = new Mat();
            Cv2.CvtColor(mat, bgraMat, ColorConversionCodes.BGR2BGRA);
            var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(bgraMat.Width, bgraMat.Height),
                new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);

            using (var lockedFramebuffer = bitmap.Lock())
            {
                unsafe { Buffer.MemoryCopy(bgraMat.Data.ToPointer(), lockedFramebuffer.Address.ToPointer(), bgraMat.Total() * bgraMat.ElemSize(), bgraMat.Total() * bgraMat.ElemSize()); }
            }
            return bitmap;
        }
    }

    public class CameraInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }
}