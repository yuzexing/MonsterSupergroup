using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.U2D;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[CreateAssetMenu(fileName = "DantePenSpriteShapeGeometryModifier", menuName = "DantePenSpriteShapeGeometryModifier", order = 1)]
	public class DantePenSpriteShapeGeometryModifier : SpriteShapeGeometryModifier
	{
		[BurstCompile]
		internal struct ApplyCylindricalProjectionJob : IJobParallelFor
		{
			public NativeSlice<Vector3> positions;

			public float radius;

			public float lerpFactor;

			public void Execute(int index)
			{
				float3 float5 = positions[index];
				float x = math.atan2(float5.y, float5.x);
				float num = math.abs(math.cos(x));
				num = (2f * num - 1f) * lerpFactor;
				float t = math.smoothstep(0f, 1f, num);
				float num2 = math.lerp(math.cos(x) * radius, float5.x, t);
				float z = math.lerp(math.sin(x) * radius, float5.z, t);
				float3 float6 = float5;
				float6.x = num2 * 1.1f;
				float6.y *= 0.8f;
				float6.z = z;
				positions[index] = float6;
			}
		}

		public float radius = 1f;

		[Range(0f, 1f)]
		public float lerpFactor;

		public override JobHandle MakeModifierJob(JobHandle generator, SpriteShapeController spriteShapeController, NativeArray<ushort> indices, NativeSlice<Vector3> positions, NativeSlice<Vector2> texCoords, NativeSlice<Vector4> tangents, NativeArray<SpriteShapeSegment> segments, NativeArray<float2> colliderData)
		{
			return IJobParallelForExtensions.Schedule(new ApplyCylindricalProjectionJob
			{
				positions = positions,
				radius = radius,
				lerpFactor = lerpFactor
			}, positions.Length, 64, generator);
		}
	}
}
