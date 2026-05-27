using OpenCvSharp;
using System.Collections.Generic;

namespace NeuroSight.Core
{
    public static class AppState
    {
        public static int SelectedCameraIndex { get; set; } = 0;
        public static List<CaptureRegion> Regions { get; set; } = new List<CaptureRegion>();
    }
}