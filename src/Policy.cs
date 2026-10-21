using System;

namespace AspectSwitch
{
    public sealed class Reading
    {
        public bool Ok;
        public int Input = -1;
        public int Aspect = -1;
        public bool Wrote;
        public bool Skipped;
        public string Error;
        public string Monitor;
        public string MonitorId;
        public MonitorCandidate[] Monitors;
    }

    // A write is confirmed on a later fresh connection; scaling changes can invalidate handles.
    public sealed class Policy
    {
        public int Input = -1;
        public int StableCount;
        public int Attempts;
        public DateTime NextAttempt = DateTime.MinValue;
        public void Missing() { StableCount = 0; }
        public void Reset() { Input = -1; StableCount = 0; Attempts = 0; NextAttempt = DateTime.MinValue; }
        public int Observe(Reading reading, Settings settings, DateTime now)
        {
            if (!reading.Ok) { Missing(); return -1; }
            if (Input != reading.Input) { Input = reading.Input; StableCount = 0; Attempts = 0; NextAttempt = DateTime.MinValue; }
            StableCount = Math.Min(StableCount + 1, 2);
            int desired = settings.DesiredAspect(reading.Input);
            if (reading.Aspect == desired) { Attempts = 0; return -1; }
            if (desired < 0 || !settings.AutoEnabled || StableCount < 2 || Attempts >= 3 || now < NextAttempt) return -1;
            return desired;
        }
        public void Attempted(DateTime now) { Attempts++; NextAttempt = now.AddSeconds(10); }
    }
}
