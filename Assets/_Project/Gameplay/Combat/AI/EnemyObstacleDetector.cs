using System;
using UnityEngine;

namespace AstralShift.HellMaiden.AI
{
	[RequireComponent(typeof(Collider2D))]
	public class EnemyObstacleDetector : MonoBehaviour
	{
		public bool isTrigger;

		public Action<Collider2D> OnTriggerEnter;

		public Action<Collider2D> OnTriggerStay;

		private Collider2D _collider;

		public Collider2D Collider
		{
			get
			{
				if (!(_collider == null))
				{
					return _collider;
				}
				return _collider = GetComponent<Collider2D>();
			}
		}

		private void Reset()
		{
			_collider = GetComponent<Collider2D>();
		}

		private void Awake()
		{
			if (_collider == null)
			{
				_collider = GetComponent<Collider2D>();
			}
		}

		private void OnTriggerEnter2D(Collider2D otherCollider)
		{
			if (isTrigger && base.enabled)
			{
				OnTriggerEnter?.Invoke(otherCollider);
			}
		}

		private void OnTriggerStay2D(Collider2D otherCollider)
		{
			if (isTrigger && base.enabled)
			{
				OnTriggerStay?.Invoke(otherCollider);
			}
		}

		public void ToggleCollision(bool value)
		{
			_collider.enabled = value;
		}
	}
}
