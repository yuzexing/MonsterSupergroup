using System;

namespace MonsterSupergroup.GAS
{
    internal static class NumericModifierValidation
    {
        public static float Finite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Value must be finite.");
            }

            return value;
        }
    }
}
