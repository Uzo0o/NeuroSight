using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DirectShowLib;
using NeuroSight.AI;
using NeuroSight.Core;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NeuroSight.Views
{
    public partial class RunView : UserControl
    {
        private VideoCapture? _capture;
        private CancellationTokenSource? _cancellationTokenSource;
        
        private YoloScoreReader _aiEngine;
        private FlywheelClockManager _clockManager;
        private string _currentTimerString = "00:00";

        public RunView()
        {
            InitializeComponent();

            // Initialize your newly trained, fully generalized model!
            string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ML", "best2.onnx");
            _aiEngine = new YoloScoreReader(modelPath);
            _clockManager = new FlywheelClockManager();
        }

        private void BtnStartAI_Click(object? sender, RoutedEventArgs e)
        {
            if (_capture != null && _capture.IsOpened()) return;

            // Safety catch: Make sure they actually drew boxes first
            if (AppState.Regions.Count == 0)
            {
                StatusText.Text = "ERROR: No crop regions saved! Go back to Setup and draw boxes first.";
                return;
            }

            // Open the camera they chose on the first screen
            _capture = new VideoCapture(AppState.SelectedCameraIndex, VideoCaptureAPIs.DSHOW);

            if (!_capture.IsOpened())
            {
                StatusText.Text = "Failed to open camera.";
                return;
            }

            StatusText.Text = $"AI Engine Running. Processing {AppState.Regions.Count} ROIs...";
            _cancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => CaptureLoop(_cancellationTokenSource.Token));
        }

        private void CaptureLoop(CancellationToken token)
        {
            using var frame = new Mat();

            while (!token.IsCancellationRequested)
            {
                if (_capture!.Read(frame) && !frame.Empty())
                {
                    List<DetectedDigit> allGlobalDetections = new List<DetectedDigit>();
                    bool timerFoundInRegions = false;

                    // ==========================================
                    // THE MAGIC CROP & SHIFT LOOP
                    // ==========================================
                    foreach (var region in AppState.Regions)
                    {
                        // 1. Math Safety: Prevent OpenCV crash if the user dragged slightly off-screen
                        int x = Math.Max(0, (int)region.Bounds.X);
                        int y = Math.Max(0, (int)region.Bounds.Y);
                        int w = Math.Min(frame.Width - x, (int)region.Bounds.Width);
                        int h = Math.Min(frame.Height - y, (int)region.Bounds.Height);

                        if (w <= 0 || h <= 0) continue; 

                        // Create the OpenCV Rect
                        OpenCvSharp.Rect cvRect = new OpenCvSharp.Rect(x, y, w, h);

                        // 2. Slice the giant 1080p frame down to just the tiny box
                        using Mat croppedMat = new Mat(frame, cvRect);

                        // 3. Run YOLO ONLY on this tiny, isolated high-res crop
                        List<DetectedDigit> localDetections = _aiEngine.GetRawDetections(croppedMat, 0.35f);

                        // 4. Shift Coordinates back to the global frame
                        foreach (var digit in localDetections)
                        {
                            digit.X1 += x;
                            digit.X2 += x;
                            digit.Y1 += y;
                            digit.Y2 += y;
                            
                            allGlobalDetections.Add(digit);
                        }

                        // 5. Intelligent Routing based on the Label
                        if (region.Label == "Timer")
                        {
                            timerFoundInRegions = true;
                            // Blast the timer numbers straight into the Flywheel
                            _currentTimerString = _clockManager.ProcessRawDetections(localDetections);
                        }
                        // Note: You can add `else if (region.Label == "Home Score")` later!
                    }

                    // ==========================================
                    // RENDER & UI UPDATE
                    // ==========================================

                    // 6. Draw the shifted green boxes over the full 1080p video feed
                    _aiEngine.DrawDetections(frame, allGlobalDetections);

                    // 7. Convert and send to Avalonia UI
                    using var clonedFrame = frame.Clone();
                    var bitmap = ConvertMatToAvaloniaBitmap(clonedFrame);

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        VideoFeedDisplay.Source = bitmap;
                        
                        if (timerFoundInRegions)
                            LiveTimerText.Text = _currentTimerString;
                    });
                }
                
                // Keep UI smooth. Slicing images is incredibly fast, so 20ms sleep easily keeps us at 50 FPS.
                Thread.Sleep(20); 
            }
        }

        private void BtnStopAI_Click(object? sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
            VideoFeedDisplay.Source = null;
            if(StatusText != null) StatusText.Text = "AI Engine Stopped.";
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

        // Extremely Important: Prevents a locked camera crash if you click the sidebar to switch tabs while the AI is running
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            BtnStopAI_Click(null, null);
        }
    }
}