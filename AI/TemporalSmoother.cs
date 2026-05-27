using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuroSight.AI
{
    /// <summary>
    /// Multi-frame consensus engine that eliminates 7-segment transition glitches.
    /// Each digit position is tracked independently with a stability buffer.
    /// </summary>
    public class TemporalSmoother
    {
        private readonly int _bufferSize;
        private readonly int _consensusThreshold;
        private readonly int _transitionLockFrames;

        // Per-region state
        private readonly Dictionary<string, RegionState> _states = new();

        public TemporalSmoother(int bufferSize = 12, int consensusThreshold = 8, int transitionLockFrames = 5)
        {
            _bufferSize = bufferSize;
            _consensusThreshold = consensusThreshold;
            _transitionLockFrames = transitionLockFrames;
        }

        public SmoothedReading Process(string regionLabel, List<DetectionResult> rawDetections)
        {
            if (!_states.TryGetValue(regionLabel, out var state))
            {
                state = new RegionState();
                _states[regionLabel] = state;
            }

            // 1. Parse raw detections into a candidate string
            var candidate = ParseDetections(rawDetections);
            state.Buffer.Add(candidate);
            if (state.Buffer.Count > _bufferSize)
                state.Buffer.RemoveAt(0);

            // 2. If buffer not full yet, return last stable
            if (state.Buffer.Count < _consensusThreshold)
                return new SmoothedReading(state.LastStableValue, state.LastStableConfidence, false);

            // 3. Find most common value in buffer
            var groups = state.Buffer.GroupBy(v => v.Value)
                                    .Select(g => new { Value = g.Key, Count = g.Count(), AvgConf = g.Average(x => x.Confidence) })
                                    .OrderByDescending(g => g.Count)
                                    .ToList();

            var winner = groups.First();
            bool isStable = winner.Count >= _consensusThreshold;
            string outputValue = state.LastStableValue;
            float outputConf = state.LastStableConfidence;

            // 4. Transition lock: if value changes, require extra confirmation
            if (isStable && winner.Value != state.LastStableValue)
            {
                state.TransitionCounter++;
                if (state.TransitionCounter >= _transitionLockFrames)
                {
                    outputValue = winner.Value;
                    outputConf = winner.AvgConf;
                    state.LastStableValue = winner.Value;
                    state.LastStableConfidence = winner.AvgConf;
                    state.TransitionCounter = 0;
                }
            }
            else if (isStable && winner.Value == state.LastStableValue)
            {
                state.TransitionCounter = 0;
                outputValue = winner.Value;
                outputConf = winner.AvgConf;
            }

            // 5. Semantic validation for timer regions
            if (regionLabel.Contains("Timer", StringComparison.OrdinalIgnoreCase) ||
                regionLabel.Contains("Clock", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsValidTime(outputValue))
                    return new SmoothedReading(state.LastStableValue, state.LastStableConfidence, false);
            }

            bool changed = outputValue != state.PreviousEmittedValue;
            if (changed) state.PreviousEmittedValue = outputValue;

            return new SmoothedReading(outputValue, outputConf, changed);
        }

        private (string Value, float Confidence) ParseDetections(List<DetectionResult> detections)
        {
            var digits = detections
                .Where(d => char.IsDigit(d.Value[0]) || d.Value == "." || d.Value == "-")
                .OrderBy(d => d.X1)
                .ToList();

            if (digits.Count == 0) return ("", 0f);

            string value = string.Concat(digits.Select(d => d.Value));
            float avgConf = digits.Average(d => d.Confidence);
            return (value, avgConf);
        }

        private bool IsValidTime(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            // Accept formats: MM:SS, M:SS, or raw digits
            var parts = value.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int min) && int.TryParse(parts[1], out int sec))
                return sec < 60 && min >= 0 && min <= 99;

            // Also accept plain digit strings for scoreboards
            return value.All(c => char.IsDigit(c) || c == '.' || c == '-');
        }

        public void Reset() => _states.Clear();

        private class RegionState
        {
            public List<(string Value, float Confidence)> Buffer { get; } = new();
            public string LastStableValue { get; set; } = "";
            public float LastStableConfidence { get; set; }
            public string PreviousEmittedValue { get; set; } = "";
            public int TransitionCounter { get; set; } = 0;
        }
    }

    public record SmoothedReading(string Value, float Confidence, bool HasChanged);
}
