namespace MonsterSupergroup.GAS.Unity
{
    /// <summary>A deterministic random source whose state is independent from Unity's global RNG.</summary>
    public sealed class SeededRandomSource : IRandomSource
    {
        private const float LargestValueBelowOne = 0.99999994f;

        private readonly System.Random random;

        public SeededRandomSource(int seed)
        {
            random = new System.Random(seed);
        }

        public float Next01()
        {
            float value = (float)random.NextDouble();
            return value < 1f ? value : LargestValueBelowOne;
        }
    }
}
