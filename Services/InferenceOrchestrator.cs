using NeuroSight.AI;
using NeuroSight.Core;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NeuroSight.Services
{
    public class InferenceOrchestrator : IDisposable
    {
        private readonly AppConfig _config;
        private readonly YoloScoreReader _ai;
        private readonly TemporalSmoother _smoother;
        private readonly VmixTcpService _vmix;
        private readonly ReadingLog _log;
        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;

        public bool IsRunning { get; private set; }
        public event Action<Dictionary<string, SmoothedReading>>? OnReadingsUpdated;
        public event Action<Mat>? OnPreviewFrame;  // Only fired if preview enabled

        public InferenceOrchestrator(AppConfig config, ReadingLog log)
        {
            _config = config;
            _log = log;
            string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ML", "best2.onnx");
            _ai = new YoloScoreReader(modelPath);
            _smoother = new TemporalSmoother(bufferSize: 12, consensusThreshold: 8, transitionLockFrames: 5);
            _vmix = new VmixTcpService();
            _vmix.Configure(config.VmixHost, config.VmixPort, config.VmixEnabled);
        }

        public void Start()
        {
            if (IsRunning) return;
            if (_config.Regions.Count == 0) throw new InvalidOperationException("No regions configured");

            _capture = new VideoCapture(_config.SelectedCameraIndex, VideoCaptureAPIs.DSHOW);
            _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
            _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
            _capture.Set(VideoCaptureProperties.FrameHeight, 1080);

            if (!_capture.IsOpened()) throw new InvalidOperationException("Failed to open camera");

            _cts = new CancellationTokenSource();
            IsRunning = true;
            Task.Run(() => Loop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
            IsRunning = false;
        }

        private void Loop(CancellationToken token)
        {
            using var frame = new Mat();
            int frameIntervalMs = 1000 / _config.TargetFps;

            while (!token.IsCancellationRequested)
            {
                if (_capture!.Read(frame) && !frame.Empty())
                {
                    var readings = new Dictionary<string, SmoothedReading>();
                    var allDetections = new List<DetectionResult>();

                    foreach (var region in _config.Regions)
                    {
                        int x = Math.Max(0, (int)region.X);
                        int y = Math.Max(0, (int)region.Y);
                        int w = Math.Min(frame.Width - x, (int)region.Width);
                        int h = Math.Min(frame.Height - y, (int)region.Height);
                        if (w <= 0 || h <= 0) continue;

                        using Mat crop = new Mat(frame, new Rect(x, y, w, h));
                        var localDets = _ai.Detect(crop, _config.ConfidenceThreshold);

                        // Shift back to global coords for drawing
                        foreach (var d in localDets)
                        {
                            d.X1 += x; d.X2 += x; d.Y1 += y; d.Y2 += y;
                            allDetections.Add(d);
                        }

                        var smoothed = _smoother.Process(region.Label, localDets);
                        readings[region.Label] = smoothed;

                        // Send to vMix if value changed and is valid
                        if (smoothed.HasChanged && !string.IsNullOrEmpty(smoothed.Value))
                        {
                            _ = Task.Run(async () =>
                            {
                                bool sent = false;
                                string input = region.Label switch
                                {
                                    "Timer" or "Shot Clock" => _config.VmixTimerInput,
                                    "Home Score" => _config.VmixHomeScoreInput,
                                    "Away Score" => _config.VmixAwayScoreInput,
                                    _ => region.Label
                                };
                                sent = await _vmix.SendTextAsync(input, smoothed.Value);
                                _log.Record(region.Label, smoothed.Value, smoothed.Confidence, sent);
                            });
                        }
                    }

                    // Preview only if enabled (saves massive CPU)
                    if (_config.ShowVideoPreview)
                    {
                        using var displayFrame = frame.Clone();
                        if (_config.DrawDetectionBoxes)
                            _ai.DrawDetections(displayFrame, allDetections);
                        OnPreviewFrame?.Invoke(displayFrame);
                    }

                    OnReadingsUpdated?.Invoke(readings);
                }

                Thread.Sleep(frameIntervalMs);
            }
        }

        public void Dispose()
        {
            Stop();
            _ai.Dispose();
        }
    }
}
