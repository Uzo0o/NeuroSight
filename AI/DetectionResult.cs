namespace NeuroSight.AI
{
    public class DetectionResult
    {
        public string Value { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public int ClassId { get; set; }
    }
}
