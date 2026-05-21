using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using DirectShowLib;
using NeuroSight.Core; // For AppState
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

        // --- Mouse Drawing State ---
        private bool _isDrawing = false;
        private Point _startPoint;
        private Rectangle? _currentRectShape;
        
        // Let's hardcode the label for now. Later, you can add a text popup 
        // to ask the user "What is this?" (Timer, Home, Away)
        private string _currentLabel = "Timer"; 

        public SetupView()
        {
            InitializeComponent();
            LoadCameras();
            RedrawSavedRegions(); // If they come back to this screen, show what they already drew
        }

        // ==========================================
        // 1. MOUSE DRAG & CROP LOGIC
        // ==========================================

        private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Only draw on left click. (Maybe use Right Click to delete boxes later?)
            if (!e.GetCurrentPoint(DrawCanvas).Properties.IsLeftButtonPressed) return;

            _isDrawing = true;
            _startPoint = e.GetPosition(DrawCanvas);

            // Create a physical green rectangle on the screen
            _currentRectShape = new Rectangle
            {
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 0, 255, 0)) // Semi-transparent green center
            };

            // Put the top-left corner exactly where the mouse clicked
            Canvas.SetLeft(_currentRectShape, _startPoint.X);
            Canvas.SetTop(_currentRectShape, _startPoint.Y);

            // Add it to the UI
            DrawCanvas.Children.Add(_currentRectShape);
        }

        private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDrawing || _currentRectShape == null) return;

            // Where is the mouse right now?
            Point currentPoint = e.GetPosition(DrawCanvas);

            // Calculate the width and height (Math.Abs handles dragging backwards/upwards)
            double x = Math.Min(currentPoint.X, _startPoint.X);
            double y = Math.Min(currentPoint.Y, _startPoint.Y);
            double width = Math.Max(currentPoint.X, _startPoint.X) - x;
            double height = Math.Max(currentPoint.Y, _startPoint.Y) - y;

            // Update the green box in real-time
            Canvas.SetLeft(_currentRectShape, x);
            Canvas.SetTop(_currentRectShape, y);
            _currentRectShape.Width = width;
            _currentRectShape.Height = height;
        }

        private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDrawing || _currentRectShape == null) return;

            _isDrawing = false;

            // Prevent saving tiny accidental micro-clicks
            if (_currentRectShape.Width < 10 || _currentRectShape.Height < 10)
            {
                DrawCanvas.Children.Remove(_currentRectShape);
                _currentRectShape = null;
                return;
            }

            // Save the exact coordinates to our Global State!
            var finalBounds = new Rect(
                Canvas.GetLeft(_currentRectShape), 
                Canvas.GetTop(_currentRectShape), 
                _currentRectShape.Width, 
                _currentRectShape.Height);

            AppState.Regions.Add(new CaptureRegion 
            { 
                Label = _currentLabel, 
                Bounds = finalBounds 
            });

            // (Optional) Draw a text tag above the box so they know what it is
            var labelTag = new TextBlock
            {
                Text = _currentLabel,
                Foreground = Brushes.LimeGreen,
                FontWeight = FontWeight.Bold,
                FontSize = 14
            };
            Canvas.SetLeft(labelTag, finalBounds.X);
            Canvas.SetTop(labelTag, finalBounds.Y - 20); // Push text slightly above the box
            DrawCanvas.Children.Add(labelTag);

            _currentRectShape = null;
            StatusText.Text = $"Saved region '{_currentLabel}'. Draw another or proceed to Live Run.";
        }

        private void RedrawSavedRegions()
        {
            DrawCanvas.Children.Clear();
            foreach (var region in AppState.Regions)
            {
                var rect = new Rectangle
                {
                    Stroke = Brushes.Orange, // Make saved ones orange
                    StrokeThickness = 2,
                    Width = region.Bounds.Width,
                    Height = region.Bounds.Height
                };
                Canvas.SetLeft(rect, region.Bounds.X);
                Canvas.SetTop(rect, region.Bounds.Y);
                DrawCanvas.Children.Add(rect);
                
                var label = new TextBlock { Text = region.Bounds.ToString(), Foreground = Brushes.Orange };
                Canvas.SetLeft(label, region.Bounds.X);
                Canvas.SetTop(label, region.Bounds.Y - 20);
                DrawCanvas.Children.Add(label);
            }
        }

        // ==========================================
        // 2. CAMERA FEED LOGIC (Pasted from old file)
        // ==========================================
        // Note: I stripped out the AI here. SetupView ONLY displays the raw camera feed. 
        // The AI only turns on when they click over to the "RunView".

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
                // Save their choice to Global State so RunView knows which camera to use!
                AppState.SelectedCameraIndex = selectedCamera.Index;

                _capture = new VideoCapture(selectedCamera.Index, VideoCaptureAPIs.DSHOW);
                _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
                _capture.Set(VideoCaptureProperties.FrameHeight, 1080);
                
                _cancellationTokenSource = new CancellationTokenSource();
                Task.Run(() => CaptureLoop(_cancellationTokenSource.Token));
            }
        }

        private void CaptureLoop(CancellationToken token)
        {
            using var frame = new Mat();
            while (!token.IsCancellationRequested)
            {
                if (_capture!.Read(frame) && !frame.Empty())
                {
                    var bitmap = ConvertMatToAvaloniaBitmap(frame);
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => VideoFeedDisplay.Source = bitmap);
                }
                Thread.Sleep(15); 
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
}