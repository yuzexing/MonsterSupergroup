using UnityEngine;

namespace AstralShift.QTI.Helpers
{
	public static class Math
	{
		public static int NumberOfTraillingZeros(int n)
		{
			int num = 0;
			while ((n & 1) != 1)
			{
				num++;
				n >>= 1;
			}
			return num;
		}

		public static int ToDigit(this int value, int index)
		{
			return (int)((float)value / Mathf.Pow(10f, index)) % 10;
		}

		public static int DigitCount(this int value)
		{
			return (int)((value == 0) ? 1f : Mathf.Floor(Mathf.Log10(Mathf.Abs(value)) + 1f));
		}

		public static float Remap(this float from, float fromMin, float fromMax, float toMin, float toMax)
		{
			float num = from - fromMin;
			float num2 = fromMax - fromMin;
			float num3 = num / num2;
			return (toMax - toMin) * num3 + toMin;
		}

		public static Vector3 ZeroZVector3(Vector3 source)
		{
			return new Vector3(source.x, source.y, 0f);
		}

		public static Vector2 To2D(this Vector3 vec)
		{
			return new Vector2(vec.x, vec.z);
		}

		public static Vector2 GetDirectionAtoB(Vector2 positionA, Vector2 positionB, bool normalize = false)
		{
			if (normalize)
			{
				return (positionB - positionA).normalized;
			}
			return positionB - positionA;
		}

		public static Vector3 GetDirectionAtoB(Vector3 positionA, Vector3 positionB, bool normalize = false)
		{
			if (normalize)
			{
				return (positionB - positionA).normalized;
			}
			return positionB - positionA;
		}

		public static Vector2 Rotate(this Vector2 direction, float delta)
		{
			return new Vector2(direction.x * Mathf.Cos(delta) - direction.y * Mathf.Sin(delta), direction.x * Mathf.Sin(delta) + direction.y * Mathf.Cos(delta));
		}
	}
}
