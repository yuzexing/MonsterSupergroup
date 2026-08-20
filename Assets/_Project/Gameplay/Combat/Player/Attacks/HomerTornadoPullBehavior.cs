using System;
using System.Collections.Generic;
using AstralShift.HellMaiden;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;
using UnityEngine.Rendering;

public class HomerTornadoPullBehavior : MonoBehaviour
{
	private class PushState
	{
		public float angle;

		public float radius;
	}

	[SerializeField]
	public float magnitude = 1.5f;

	public float minDistance = 0.5f;

	private Vector2 appliedWindDirection;

	public Transform enemyParent;

	public KnockbackSettings knockback;

	[SerializeField]
	private bool pullClockwise = true;

	[Header("Spiral")]
	[SerializeField]
	public float pullStrength = 2f;

	[SerializeField]
	public float spiralStrength = 1.5f;

	[Header("Push")]
	[SerializeField]
	public float pushRadialSpeed = 3f;

	[SerializeField]
	public float pushAngularSpeed = 360f;

	[SerializeField]
	public float releaseDistance = 3f;

	private readonly Dictionary<EnemyController, PushState> pushingEnemies = new Dictionary<EnemyController, PushState>();

	private PlayerMovement player;

	private void Start()
	{
		player = GameDirector.Instance.Player;
	}

	private void Update()
	{
		if (pushingEnemies.Count == 0)
		{
			return;
		}
		List<EnemyController> list = new List<EnemyController>();
		foreach (KeyValuePair<EnemyController, PushState> pushingEnemy in pushingEnemies)
		{
			EnemyController key = pushingEnemy.Key;
			PushState value = pushingEnemy.Value;
			if (!key)
			{
				list.Add(key);
				continue;
			}
			value.angle += pushAngularSpeed * (MathF.PI / 180f) * Time.deltaTime;
			value.radius += pushRadialSpeed * Time.deltaTime;
			Vector2 vector = new Vector2(Mathf.Cos(value.angle), Mathf.Sin(value.angle)) * value.radius;
			key.transform.localPosition = vector;
			if (value.radius >= releaseDistance)
			{
				key.collider.enabled = true;
				key.attackScript.enabled = true;
				key.transform.SetParent(null, worldPositionStays: true);
				key.BruteforceKnockBack(player.transform.position, knockback);
				list.Add(key);
			}
		}
		foreach (EnemyController item in list)
		{
			if ((bool)item)
			{
				item.forceWindInteraction = false;
				item.ultimateWindInteraction = Vector2.zero;
				SortingGroup component = item.spriteRenderer.GetComponent<SortingGroup>();
				if (component != null)
				{
					component.sortingLayerName = "Props";
				}
			}
			pushingEnemies.Remove(item);
		}
	}

	private void OnTriggerStay2D(Collider2D other)
	{
		EnemyController componentInParent = other.GetComponentInParent<EnemyController>();
		if (!componentInParent || pushingEnemies.ContainsKey(componentInParent))
		{
			return;
		}
		Vector2 vector = (Vector2)base.transform.position - (Vector2)componentInParent.transform.position;
		if (vector.sqrMagnitude < minDistance * minDistance)
		{
			componentInParent.forceWindInteraction = false;
			componentInParent.ultimateWindInteraction = Vector2.zero;
			componentInParent.collider.enabled = false;
			componentInParent.attackScript.enabled = false;
			SortingGroup component = componentInParent.spriteRenderer.GetComponent<SortingGroup>();
			if (component != null)
			{
				component.sortingLayerName = "Foreground";
			}
			componentInParent.transform.SetParent(enemyParent, worldPositionStays: true);
			Vector2 vector2 = componentInParent.transform.localPosition;
			PushState pushState = new PushState
			{
				radius = vector2.magnitude,
				angle = Mathf.Atan2(vector2.y, vector2.x)
			};
			if (pushState.radius < 0.05f)
			{
				pushState.radius = 0.05f;
			}
			pushingEnemies.Add(componentInParent, pushState);
		}
		else
		{
			Vector2 normalized = vector.normalized;
			Vector2 vector3 = (pullClockwise ? new Vector2(normalized.y, 0f - normalized.x) : new Vector2(0f - normalized.y, normalized.x));
			Vector2 normalized2 = (normalized * pullStrength + vector3 * spiralStrength).normalized;
			componentInParent.forceWindInteraction = true;
			componentInParent.ultimateWindInteraction = normalized2 * magnitude;
			componentInParent.ultimateWindInteraction = normalized2 * magnitude;
		}
	}

	public void ReleaseAllEnemies()
	{
		foreach (EnemyController item in new List<EnemyController>(pushingEnemies.Keys))
		{
			item.collider.enabled = true;
			item.attackScript.enabled = true;
			item.transform.SetParent(null, worldPositionStays: true);
			item.forceWindInteraction = false;
			item.ultimateWindInteraction = Vector2.zero;
			SortingGroup component = item.spriteRenderer.GetComponent<SortingGroup>();
			if (component != null)
			{
				component.sortingLayerName = "Props";
			}
		}
	}

	private void OnTriggerExit2D(Collider2D other)
	{
		EnemyController componentInParent = other.GetComponentInParent<EnemyController>();
		if ((bool)componentInParent)
		{
			componentInParent.forceWindInteraction = false;
			componentInParent.ultimateWindInteraction = Vector2.zero;
		}
	}
}
