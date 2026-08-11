using System;

namespace MonsterSupergroup.GAS.Unity
{
    public sealed class UnityRandomSource : IRandomSource
    {
        private const float LargestValueBelowOne = 0.99999994f;

        public float Next01()
        {
            float value = UnityEngine.Random.value;
            if (float.IsNaN(value) || value < 0f || value > 1f)
            {
                throw new InvalidOperationException("UnityEngine.Random.value returned a value outside [0, 1].");
            }

            return value < 1f ? value : LargestValueBelowOne;
        }
    }
}
