using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuroSight.AI
{
    public class FlywheelClockManager
    {
        // Internal State
        private TimeSpan _currentBroadcastTime = TimeSpan.Zero;
        private TimeSpan _lastSeenAiTime = TimeSpan.Zero;
        
        // Time Tracking
        private DateTime _lastSimulatedTick = DateTime.Now;
        private bool _isRunning = false;
        
        // Glitch & Pause Protection
        private int _frozenFrameCount = 0;
        private const int FramesToConfirmPause = 5; // AI must see the same time for 5 frames to confirm a pause

        public string ProcessRawDetections(List<DetectedDigit> detectedDigits)
        {
            // 1. Attempt to parse the raw AI digits into a real Time
            TimeSpan? incomingAiTime = ParseAiTime(detectedDigits);

            // 2. FLYWHEEL LOGIC: Do we trust the AI, or do we fly blind?
            if (incomingAiTime.HasValue)
            {
                TimeSpan aiTime = incomingAiTime.Value;
                double timeDifference = (aiTime - _lastSeenAiTime).TotalSeconds;

                // SCENARIO A: The clock is ticking down normally (-1 second)
                if (timeDifference == -1.0)
                {
                    _isRunning = true;
                    _frozenFrameCount = 0;
                    _lastSeenAiTime = aiTime;
                    _currentBroadcastTime = aiTime; // Sync our internal clock perfectly to the AI
                    _lastSimulatedTick = DateTime.Now;
                }
                // SCENARIO B: The clock is frozen (Referee blew the whistle)
                else if (timeDifference == 0)
                {
                    _frozenFrameCount++;
                    if (_frozenFrameCount >= FramesToConfirmPause)
                    {
                        _isRunning = false; // Stop our internal timer!
                        _currentBroadcastTime = aiTime;
                    }
                }
                // SCENARIO C: A massive jump (e.g., Referee resets the clock to 12:00 from 10:00)
                else if (Math.Abs(timeDifference) > 2.0 && _frozenFrameCount > 10) 
                {
                    // We only accept huge jumps if the clock was heavily paused beforehand.
                    // This prevents mid-play hallucinations from ruining the broadcast.
                    _currentBroadcastTime = aiTime;
                    _lastSeenAiTime = aiTime;
                }
                // SCENARIO D: A 1-frame hallucination (e.g. 12:56 -> 02:56). 
                // We do nothing! We let the code fall through to the internal stopwatch.
            }

            // 3. DEAD RECKONING: If the clock is running, but the AI gave us garbage 
            // (or we ignored a hallucination), we simulate the time passing ourselves.
            if (_isRunning)
            {
                TimeSpan timeSinceLastTick = DateTime.Now - _lastSimulatedTick;
                
                // If a full second has passed in real life, tick our internal clock down
                if (timeSinceLastTick.TotalSeconds >= 1.0)
                {
                    _currentBroadcastTime = _currentBroadcastTime.Subtract(TimeSpan.FromSeconds(1));
                    _lastSimulatedTick = DateTime.Now; // Reset our stopwatch
                }
            }

            return FormatTimeSpan(_currentBroadcastTime);
        }

        // --- Helper Methods ---

        private TimeSpan? ParseAiTime(List<DetectedDigit> detectedDigits)
        {
            var clockDigits = detectedDigits
                .Where(d => char.IsDigit(d.Value[0]))
                .OrderBy(d => d.X1)
                .Select(d => d.Value)
                .ToList();

            if (clockDigits.Count < 3 || clockDigits.Count > 4) return null;

            int minutes = 0, seconds = 0;

            if (clockDigits.Count == 4)
            {
                minutes = int.Parse(clockDigits[0] + clockDigits[1]);
                seconds = int.Parse(clockDigits[2] + clockDigits[3]);
            }
            else if (clockDigits.Count == 3)
            {
                minutes = int.Parse(clockDigits[0]);
                seconds = int.Parse(clockDigits[1] + clockDigits[2]);
            }

            if (seconds >= 60 || minutes > 99) return null; // AI Hallucinated an impossible time

            return new TimeSpan(0, minutes, seconds);
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            // Prevents negative time if the clock hits 00:00
            if (ts.TotalSeconds < 0) return "00:00"; 
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}