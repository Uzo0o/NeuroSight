using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NeuroSight.Core
{
    public class CaptureRegion
    {
        public string Label { get; set; } = "Unknown";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public class AppConfig
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeuroSight", "config.json");

        public int SelectedCameraIndex { get; set; } = 0;
        public List<CaptureRegion> Regions { get; set; } = new();

        // Performance
        public bool ShowVideoPreview { get; set; } = true;
        public bool DrawDetectionBoxes { get; set; } = true;
        public int TargetFps { get; set; } = 30;
        public float ConfidenceThreshold { get; set; } = 0.40f;

        // vMix Output
        public string VmixHost { get; set; } = "127.0.0.1";
        public int VmixPort { get; set; } = 8099;
        public string VmixTimerInput { get; set; } = "Timer";
        public string VmixHomeScoreInput { get; set; } = "Home";
        public string VmixAwayScoreInput { get; set; } = "Away";
        public bool VmixEnabled { get; set; } = false;

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static AppConfig Load()
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            return new AppConfig();
        }
    }
}
