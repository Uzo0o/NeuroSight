using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeuroSight.Core;
using NeuroSight.Services;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NeuroSight.AI;

namespace NeuroSight.ViewModels
{
    public partial class RunViewModel : ObservableObject
    {
        private readonly AppConfig _config;
        private readonly ReadingLog _log;
        private InferenceOrchestrator? _orchestrator;

        [ObservableProperty]
        private string _timerValue = "00:00";

        [ObservableProperty]
        private string _homeScore = "0";

        [ObservableProperty]
        private string _awayScore = "0";

        [ObservableProperty]
        private string _statusText = "Ready. Configure regions in Setup first.";

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _showPreview;

        [ObservableProperty]
        private object? _previewBitmap;

        public RunViewModel()
        {
            _config = AppConfig.Load();
            _log = new ReadingLog();
            ShowPreview = _config.ShowVideoPreview;
        }

        [RelayCommand]
        private void TogglePreview()
        {
            ShowPreview = !ShowPreview;
            _config.ShowVideoPreview = ShowPreview;
            _config.Save();
            StatusText = ShowPreview ? "Preview enabled (higher CPU usage)" : "Preview disabled (headless mode)";
        }

        [RelayCommand]
        private void StartEngine()
        {
            if (_config.Regions.Count == 0)
            {
                StatusText = "ERROR: No crop regions saved! Go to Setup first.";
                return;
            }

            try
            {
                _orchestrator = new InferenceOrchestrator(_config, _log);
                _orchestrator.OnReadingsUpdated += OnReadingsUpdated;
                _orchestrator.OnPreviewFrame += OnPreviewFrame;
                _orchestrator.Start();
                IsRunning = true;
                StatusText = $"AI running. Processing {_config.Regions.Count} regions. vMix: {(_config.VmixEnabled ? "ON" : "OFF")}";
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to start: {ex.Message}";
            }
        }

        [RelayCommand]
        private void StopEngine()
        {
            _orchestrator?.Stop();
            _orchestrator?.Dispose();
            _orchestrator = null;
            IsRunning = false;
            PreviewBitmap = null;
            StatusText = "Engine stopped.";
        }

        [RelayCommand]
        private async Task ExportLog()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"neurosight_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            await Task.Run(() => _log.ExportCsv(path));
            StatusText = $"Log exported to {path}";
        }

        private void OnReadingsUpdated(Dictionary<string, SmoothedReading> smoothedReadings)
        {
            foreach (var kv in smoothedReadings)
            {
                if (kv.Key.Contains("Timer", StringComparison.OrdinalIgnoreCase))
                    TimerValue = kv.Value.Value;
                else if (kv.Key.Contains("Home", StringComparison.OrdinalIgnoreCase))
                    HomeScore = kv.Value.Value;
                else if (kv.Key.Contains("Away", StringComparison.OrdinalIgnoreCase))
                    AwayScore = kv.Value.Value;
            }
        }

        private void OnPreviewFrame(Mat frame)
        {
            if (!ShowPreview) return;
            // Convert on UI thread
            var bitmap = ConvertMatToBitmap(frame);
            PreviewBitmap = bitmap;
        }

        private static Avalonia.Media.Imaging.WriteableBitmap ConvertMatToBitmap(Mat mat)
        {
            using var bgra = new Mat();
            Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
            var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(bgra.Width, bgra.Height),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Unpremul);
            using (var fb = bitmap.Lock())
            {
                unsafe
                {
                    Buffer.MemoryCopy(bgra.Data.ToPointer(), fb.Address.ToPointer(), bgra.Total() * bgra.ElemSize(), bgra.Total() * bgra.ElemSize());
                }
            }
            return bitmap;
        }
    }
}
