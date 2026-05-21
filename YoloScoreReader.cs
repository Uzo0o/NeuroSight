using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuroSight.AI
{
    public class DetectedDigit
    {
        public string Value { get; set; } = string.Empty;
        public float Confidence { get; set; }
        
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
    }

    public class YoloScoreReader : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly int _inputWidth = 640;  
        private readonly int _inputHeight = 640;
        private readonly string[] _classNames = { "-", ".", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "dot", "h", "kW", "null" };

        public YoloScoreReader(string modelPath)
        {
            _session = new InferenceSession(modelPath);
        }

        /// <summary>
        /// Runs inference and returns the raw detections. 
        /// This allows the Flywheel Clock Manager to handle the state logic.
        /// </summary>
        public List<DetectedDigit> GetRawDetections(Mat frame, float minConfidence = 0.35f)
        {
            // Calculate scale offsets back to original image size
            float xScale = frame.Width / (float)_inputWidth;
            float yScale = frame.Height / (float)_inputHeight;

            // 1. PRE-PROCESS (High performance optimized)
            var tensor = Preprocess(frame);

            // 2. INFERENCE
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", tensor) };
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);

            // 3. POST-PROCESS 
            var outputTensor = results.First().AsTensor<float>();
            
            // FIXED: Passing minConfidence correctly down to the parser
            return ParseOutput(outputTensor, minConfidence, xScale, yScale);
        }

        /// <summary>
        /// Optional helper to draw detection graphics directly onto an image frame.
        /// </summary>
        public void DrawDetections(Mat frame, List<DetectedDigit> detections)
        {
            foreach (var digit in detections)
            {
                // Draw Bounding Box
                Cv2.Rectangle(frame, new Point(digit.X1, digit.Y1), new Point(digit.X2, digit.Y2), new Scalar(0, 255, 0), 2);

                // Draw Text Label
                string label = $"{digit.Value} ({(digit.Confidence * 100):0}%)";
                int textY = Math.Max(20, digit.Y1 - 10); 
                
                Cv2.PutText(frame, label, new Point(digit.X1, textY), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
            }
        }

        private DenseTensor<float> Preprocess(Mat originalFrame)
        {
            using var resized = new Mat();
            Cv2.Resize(originalFrame, resized, new Size(_inputWidth, _inputHeight));
            Cv2.CvtColor(resized, resized, ColorConversionCodes.BGR2RGB);

            var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });

            // OPTIMIZED: Ripped out .At<Vec3b>() bounds checking. 
            // Utilizing direct memory pointers to map pixel layout in microseconds.
            unsafe
            {
                byte* dataPtr = (byte*)resized.Data.ToPointer();
                int step = (int)resized.Step(); // Number of bytes per row

                for (int y = 0; y < _inputHeight; y++)
                {
                    byte* rowPtr = dataPtr + (y * step);
                    for (int x = 0; x < _inputWidth; x++)
                    {
                        int pixelOffset = x * 3;
                        
                        // RGB order due to CvtColor call above
                        tensor[0, 0, y, x] = rowPtr[pixelOffset] / 255.0f;     // R
                        tensor[0, 1, y, x] = rowPtr[pixelOffset + 1] / 255.0f; // G
                        tensor[0, 2, y, x] = rowPtr[pixelOffset + 2] / 255.0f; // B
                    }
                }
            }
            return tensor;
        }

        // FIXED: Added minConfidence parameter to perfectly match the method call signature
        private List<DetectedDigit> ParseOutput(Tensor<float> output, float minConfidence, float xScale, float yScale)
        {
            var detected = new List<DetectedDigit>();
            int numDetections = output.Dimensions[1]; // Always 100 due to baked NMS max_det

            for (int i = 0; i < numDetections; i++)
            {
                float conf = output[0, i, 4]; // Column 4 is Confidence
        
                // Use the passed user-defined configuration threshold
                if (conf < minConfidence) continue;

                int classId = (int)output[0, i, 5]; // Column 5 is Class ID
                if (classId < 0 || classId >= _classNames.Length) continue;

                string charValue = _classNames[classId];
                if (charValue == "null") continue;

                // Columns 0-3 are X1, Y1, X2, Y2 coordinates
                int x1 = (int)(output[0, i, 0] * xScale);
                int y1 = (int)(output[0, i, 1] * yScale);
                int x2 = (int)(output[0, i, 2] * xScale);
                int y2 = (int)(output[0, i, 3] * yScale);

                detected.Add(new DetectedDigit
                {
                    Value = charValue, 
                    Confidence = conf, 
                    X1 = x1, 
                    Y1 = y1, 
                    X2 = x2, 
                    Y2 = y2
                });
            }

            return detected;
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}