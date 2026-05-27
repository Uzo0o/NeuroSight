using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuroSight.AI
{
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

        public List<DetectionResult> Detect(Mat frame, float minConfidence = 0.40f)
        {
            float xScale = frame.Width / (float)_inputWidth;
            float yScale = frame.Height / (float)_inputHeight;

            var tensor = Preprocess(frame);
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", tensor) };
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();

            return ParseOutput(outputTensor, minConfidence, xScale, yScale);
        }

        public void DrawDetections(Mat frame, List<DetectionResult> detections)
        {
            foreach (var d in detections)
            {
                Cv2.Rectangle(frame, new Point(d.X1, d.Y1), new Point(d.X2, d.Y2), new Scalar(0, 255, 0), 2);
                string label = $"{d.Value} ({(d.Confidence * 100):0}%)";
                int textY = Math.Max(20, d.Y1 - 10);
                Cv2.PutText(frame, label, new Point(d.X1, textY), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
            }
        }

        private DenseTensor<float> Preprocess(Mat originalFrame)
        {
            using var resized = new Mat();
            Cv2.Resize(originalFrame, resized, new Size(_inputWidth, _inputHeight));
            Cv2.CvtColor(resized, resized, ColorConversionCodes.BGR2RGB);

            var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
            unsafe
            {
                byte* dataPtr = (byte*)resized.Data.ToPointer();
                int step = (int)resized.Step();
                for (int y = 0; y < _inputHeight; y++)
                {
                    byte* rowPtr = dataPtr + (y * step);
                    for (int x = 0; x < _inputWidth; x++)
                    {
                        int p = x * 3;
                        tensor[0, 0, y, x] = rowPtr[p] / 255.0f;
                        tensor[0, 1, y, x] = rowPtr[p + 1] / 255.0f;
                        tensor[0, 2, y, x] = rowPtr[p + 2] / 255.0f;
                    }
                }
            }
            return tensor;
        }

        private List<DetectionResult> ParseOutput(Tensor<float> output, float minConfidence, float xScale, float yScale)
        {
            var detected = new List<DetectionResult>();
            int numDetections = output.Dimensions[1];

            for (int i = 0; i < numDetections; i++)
            {
                float conf = output[0, i, 4];
                if (conf < minConfidence) continue;

                int classId = (int)output[0, i, 5];
                if (classId < 0 || classId >= _classNames.Length) continue;

                string charValue = _classNames[classId];
                if (charValue == "null") continue;

                int x1 = (int)(output[0, i, 0] * xScale);
                int y1 = (int)(output[0, i, 1] * yScale);
                int x2 = (int)(output[0, i, 2] * xScale);
                int y2 = (int)(output[0, i, 3] * yScale);

                detected.Add(new DetectionResult
                {
                    Value = charValue,
                    Confidence = conf,
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    ClassId = classId
                });
            }
            return detected;
        }

        public void Dispose() => _session?.Dispose();
    }
}
