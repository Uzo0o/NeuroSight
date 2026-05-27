using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NeuroSight.Core
{
    public class RegionReading
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string RegionLabel { get; set; } = "";
        public string Value { get; set; } = "";
        public float Confidence { get; set; }
        public bool SentToVmix { get; set; }
    }

    public class ReadingLog
    {
        private readonly List<RegionReading> _history = new();
        private readonly int _maxHistory;

        public ReadingLog(int maxHistory = 5000) => _maxHistory = maxHistory;

        public void Record(string region, string value, float confidence, bool sentToVmix)
        {
            _history.Add(new RegionReading
            {
                RegionLabel = region,
                Value = value,
                Confidence = confidence,
                SentToVmix = sentToVmix
            });

            if (_history.Count > _maxHistory)
                _history.RemoveAt(0);
        }

        public IReadOnlyList<RegionReading> GetHistory(string? regionFilter = null, int lastN = 100)
        {
            var query = _history.AsEnumerable();
            if (!string.IsNullOrEmpty(regionFilter))
                query = query.Where(r => r.RegionLabel == regionFilter);
            return query.TakeLast(lastN).ToList();
        }

        public void ExportCsv(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,Region,Value,Confidence,VmixSent");
            foreach (var r in _history)
                sb.AppendLine($"{r.Timestamp:O},{r.RegionLabel},{r.Value},{r.Confidence:F2},{r.SentToVmix}");
            File.WriteAllText(path, sb.ToString());
        }

        public void ExportJson(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
