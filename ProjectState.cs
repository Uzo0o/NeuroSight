using OpenCvSharp;
using System.Collections.Generic;

namespace NeuroSight.Core
{
    public class CaptureRegion
    {
        public string Label { get; set; }
        
        // We use Avalonia's Rect so it's easy to draw and easy to convert to OpenCV later
        public Avalonia.Rect Bounds { get; set; } 
    }

    public static class AppState
    {
        public static int SelectedCameraIndex { get; set; } = 0;
        public static List<CaptureRegion> Regions { get; set; } = new List<CaptureRegion>();
    }
}