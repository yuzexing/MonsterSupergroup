using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.U2D;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Obsolete]
	public class DanteMeleeAttack : PlayerMeleeAttack
	{
		public PlayerAttackHitBox hitBox;

		public SpriteShapeController spriteShapeController;

		public MeshRenderer textRenderer;

		public Transform pen;

		public Transform fire;

		public ParticleSystem mainSystem;

		public ParticleSystem trailSystem;

		private ParticleSystem[] _trailSystems;

		private UnityEngine.Splines.Spline _spline;

		private DantePenSpriteShapeGeometryModifier _modifier;

		[Header("General Settings")]
		[Range(0f, 360f)]
		public float directionAngle;

		[Header("Animation Settings")]
		public float totalDuration = 3f;

		public float startDuration = 0.33f;

		public AnimationCurve startEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		public float endDuration = 1f;

		public AnimationCurve endEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		[Header("Shape Settings")]
		public int numberOfDivisions = 20;

		public float angle = 120f;

		public float depth = 0.2f;

		public float radius = 1.5f;

		private List<Vector2> positions;

		private List<Vector2> tangents;

		private Vector2 _direction;

		private readonly int _StartFadeSID = Shader.PropertyToID("_StartFade");

		private readonly int _EndFadeSID = Shader.PropertyToID("_EndFade");

		private Coroutine _animationCoroutine;

		private Coroutine _animationStartCoroutine;

		private Coroutine _animationEndCoroutine;

		public override void Init(Vector3 direction, int damage, AttackStats attackStats, PlayerStats playerMetaStats, Action OnHit, Action OnEnd)
		{
			if (_trailSystems == null)
			{
				_trailSystems = trailSystem.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
			}
			GenerateShape(direction);
			GenerateCollision();
			hitBox.collider.enabled = false;
			spriteShapeController.spriteShapeRenderer.enabled = false;
			base.OnHit = OnHit;
			base.OnEnd = OnEnd;
		}

		private void OnDisable()
		{
			OnHit = null;
			OnEnd = null;
		}

		public override void Attack()
		{
			RunAnimation();
		}

		private void ClearPoints()
		{
			spriteShapeController.spline.Clear();
			if (spriteShapeController.modifiers.Count != 0 && spriteShapeController.modifiers[0] is DantePenSpriteShapeGeometryModifier modifier)
			{
				_modifier = modifier;
			}
			_spline = new UnityEngine.Splines.Spline();
			positions = new List<Vector2>();
			tangents = new List<Vector2>();
		}

		public void GenerateShape(Vector3 direction)
		{
			ClearPoints();
			_direction = direction;
			_direction.y = 0f - _direction.y;
			float num = Vector2.SignedAngle(_direction, Vector2.right) + angle / 2f;
			float num2 = angle / (float)numberOfDivisions;
			float num3 = num2 * (MathF.PI / 180f);
			for (int i = 0; i <= numberOfDivisions; i++)
			{
				float f = (num - num2 * (float)i) * (MathF.PI / 180f);
				Vector2 vector = new Vector2(Mathf.Cos(f), Mathf.Sin(f));
				vector.Normalize();
				positions.Add(vector);
				Vector2 vector2 = new Vector2(0f - vector.y, vector.x);
				vector2.Normalize();
				tangents.Add(vector2);
				Vector2 vector3 = vector * radius;
				spriteShapeController.spline.InsertPointAt(i, vector3);
				float num4 = radius * 0.55f * num3 / (MathF.PI / 2f);
				Vector2 vector4 = vector2 * num4;
				spriteShapeController.spline.SetTangentMode(i, ShapeTangentMode.Continuous);
				spriteShapeController.spline.SetRightTangent(i, -vector4);
				spriteShapeController.spline.SetLeftTangent(i, vector4);
				if (i == 0 || i == numberOfDivisions)
				{
					spriteShapeController.spline.SetCorner(i, value: true);
				}
				BezierKnot item = new BezierKnot
				{
					Position = new float3(vector3.x, vector3.y, 0f),
					TangentIn = new float3(vector4.x, vector4.y, 0f),
					TangentOut = -new float3(vector4.x, vector4.y, 0f)
				};
				_spline.Add(item, TangentMode.AutoSmooth);
			}
			_modifier.radius = radius;
			spriteShapeController.RefreshSpriteShape();
		}

		public void GenerateMesh(Vector3 direction)
		{
			textRenderer.GetComponent<MeshFilter>().mesh = GenerateMesh(radius, angle, depth, numberOfDivisions);
			float num = Vector2.SignedAngle(Vector2.right, direction);
			num += angle / 2f;
			textRenderer.transform.eulerAngles = new Vector3(textRenderer.transform.eulerAngles.x, textRenderer.transform.eulerAngles.y, num);
		}

		private Mesh GenerateMesh(float radius, float angle, float depth, int segments)
		{
			Mesh mesh = new Mesh();
			angle = Mathf.Clamp(angle, 0f, 360f);
			int num = (segments + 1) * 2;
			Vector3[] array = new Vector3[num];
			Vector2[] array2 = new Vector2[num];
			int[] array3 = new int[segments * 6];
			float num2 = MathF.PI / 180f * angle / (float)segments;
			for (int i = 0; i <= segments; i++)
			{
				float f = num2 * (float)i;
				float x = Mathf.Cos(f) * radius;
				float z = Mathf.Sin(f) * radius;
				array[i] = new Vector3(x, (0f - depth) / 2f, z);
				array[i + segments + 1] = new Vector3(x, depth / 2f, z);
				float x2 = (float)i / (float)segments * (angle / 360f);
				array2[i] = new Vector2(x2, 0f);
				array2[i + segments + 1] = new Vector2(x2, 1f);
			}
			for (int j = 0; j < segments; j++)
			{
				int num3 = j;
				int num4 = j + 1;
				int num5 = j + segments + 1;
				int num6 = j + segments + 2;
				array3[j * 6] = num3;
				array3[j * 6 + 1] = num4;
				array3[j * 6 + 2] = num6;
				array3[j * 6 + 3] = num3;
				array3[j * 6 + 4] = num6;
				array3[j * 6 + 5] = num5;
			}
			mesh.vertices = array;
			mesh.triangles = array3;
			mesh.uv = array2;
			mesh.RecalculateNormals();
			return mesh;
		}

		private void GenerateCollision()
		{
			PolygonCollider2D polygonCollider2D = hitBox.collider as PolygonCollider2D;
			Vector2[] array = new Vector2[spriteShapeController.spline.GetPointCount() + 1];
			for (int i = 0; i < array.Length; i++)
			{
				if (i != 0)
				{
					array[i] = spriteShapeController.spline.GetPosition(i - 1);
					array[i].x *= 1.1f;
					array[i].y *= 0.8f;
				}
			}
			polygonCollider2D.points = array;
		}

		public void RunAnimation()
		{
			if (_animationCoroutine != null)
			{
				StopCoroutine(_animationCoroutine);
			}
			mainSystem.Clear(withChildren: true);
			trailSystem.Clear(withChildren: true);
			_animationCoroutine = StartCoroutine(AnimationCoroutine());
		}

		private IEnumerator AnimationCoroutine()
		{
			if (_animationStartCoroutine != null)
			{
				StopCoroutine(_animationStartCoroutine);
				_animationStartCoroutine = null;
			}
			if (_animationEndCoroutine != null)
			{
				StopCoroutine(_animationEndCoroutine);
				_animationEndCoroutine = null;
			}
			_animationStartCoroutine = StartCoroutine(AnimationStartCoroutine());
			yield return _animationStartCoroutine;
			hitBox.collider.enabled = true;
			yield return new WaitForSeconds(totalDuration);
			hitBox.collider.enabled = false;
			_animationEndCoroutine = StartCoroutine(AnimationEndCoroutine());
			yield return _animationEndCoroutine;
			_animationCoroutine = null;
		}

		private IEnumerator AnimationStartCoroutine()
		{
			float3 float5 = _spline.EvaluatePosition(0f);
			Vector3 newPosition = new Vector3(float5.x * 1.1f, float5.y * 0.8f, 0f);
			pen.transform.localPosition = base.transform.position;
			pen.gameObject.SetActive(value: true);
			fire.transform.localPosition = newPosition;
			SetShaderStartValues();
			spriteShapeController.spriteShapeRenderer.enabled = true;
			for (int i = 0; i < _trailSystems.Length; i++)
			{
				ParticleSystem.MainModule main = _trailSystems[i].main;
				main.startLifetime = totalDuration + endDuration;
			}
			mainSystem.Clear(withChildren: true);
			trailSystem.Clear(withChildren: true);
			yield return null;
			mainSystem.Play(withChildren: true);
			float t = 0f;
			Vector2 startPosition = base.transform.localPosition;
			while (t < 1f)
			{
				t += 10f * Time.deltaTime;
				pen.transform.localPosition = Vector3.Slerp(startPosition, newPosition, t);
				yield return null;
			}
			t = 0f;
			float speed = 1f / startDuration;
			while (t < 1f)
			{
				t += speed * Time.deltaTime;
				float num = startEase.Evaluate(t);
				float5 = _spline.EvaluatePosition(t);
				newPosition = new Vector3(float5.x * 1.1f, float5.y * 0.8f, 0f);
				pen.transform.localPosition = newPosition;
				fire.transform.localPosition = newPosition;
				spriteShapeController.spriteShapeRenderer.materials[1].SetFloat(_StartFadeSID, num * angle / 360f);
				textRenderer.material.SetFloat(_StartFadeSID, num * angle / 360f);
				yield return null;
			}
			_animationStartCoroutine = null;
		}

		private IEnumerator AnimationEndCoroutine()
		{
			mainSystem.Stop();
			float t = 0f;
			Vector3 startPosition = pen.transform.localPosition;
			while (t < 1f)
			{
				t += 10f * Time.deltaTime;
				pen.transform.localPosition = Vector3.Slerp(startPosition, base.transform.localPosition, t);
				yield return null;
			}
			pen.gameObject.SetActive(value: false);
			t = 0f;
			float num = endEase.Evaluate(t);
			spriteShapeController.spriteShapeRenderer.materials[1].SetFloat(_EndFadeSID, num * angle / 360f);
			textRenderer.material.SetFloat(_EndFadeSID, num * angle / 360f);
			float speed = 1f / endDuration;
			while (t < 1f)
			{
				t += speed * Time.deltaTime;
				num = endEase.Evaluate(t);
				spriteShapeController.spriteShapeRenderer.materials[1].SetFloat(_EndFadeSID, num * angle / 360f);
				textRenderer.material.SetFloat(_EndFadeSID, num * angle / 360f);
				yield return null;
			}
			yield return new WaitUntil(() => !mainSystem.IsAlive(withChildren: true));
			_animationEndCoroutine = null;
			OnEnd?.Invoke();
		}

		private void SetShaderStartValues()
		{
			float time = 0f;
			float num = startEase.Evaluate(time);
			float num2 = endEase.Evaluate(time);
			spriteShapeController.spriteShapeRenderer.materials[1].SetFloat(_StartFadeSID, num * angle / 360f);
			spriteShapeController.spriteShapeRenderer.materials[1].SetFloat(_EndFadeSID, num2 * angle / 360f);
			textRenderer.material.SetFloat(_StartFadeSID, num * angle / 360f);
			textRenderer.material.SetFloat(_EndFadeSID, num2 * angle / 360f);
		}
	}
}
