using System;

namespace MonsterSupergroup.NetworkCombat
{
    // One second of frames, not a percentile of second averages. Bounded even at uncapped FPS.
    public sealed class NetworkDiagnosticsWindow
    {
        private readonly float[] frames = new float[4096];
        public readonly int[] Histogram = new int[7]; // <=8.34,16.67,25,50,100,250,>250 ms
        private int retained;
        public int Count { get; private set; }
        public int Overflow => Count - retained;
        public int LongFrames { get; private set; }
        public int LongDetails { get; private set; }
        public double Sum { get; private set; }
        public double Maximum { get; private set; }
        public double Mean => Sum / Math.Max(1, Count);
        public bool Add(float milliseconds)
        {
            Count++; Sum += milliseconds; Maximum = Math.Max(Maximum, milliseconds);
            if (retained < frames.Length) frames[retained++] = milliseconds;
            Histogram[milliseconds <= 8.34f ? 0 : milliseconds <= 16.67f ? 1 : milliseconds <= 25 ? 2 :
                milliseconds <= 50 ? 3 : milliseconds <= 100 ? 4 : milliseconds <= 250 ? 5 : 6]++;
            if (milliseconds <= 100) return false;
            LongFrames++;
            if (LongDetails >= 8) return false;
            LongDetails++; return true;
        }
        public void Sort() => Array.Sort(frames, 0, retained);
        public double Percentile(double fraction) => retained == 0 ? 0 : frames[Math.Min(retained - 1, (int)Math.Ceiling(retained * fraction) - 1)];
        public void Reset() { retained = Count = LongFrames = LongDetails = 0; Sum = Maximum = 0; Array.Clear(Histogram, 0, Histogram.Length); }
    }
}
