using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

namespace AstralShift.HellMaiden.Helpers
{
	public static class AIHelpers
	{
		private static bool IsValid(BaseEnemyController enemy)
		{
			if (enemy != null && enemy.gameObject.activeInHierarchy)
			{
				return enemy.ID != -2;
			}
			return false;
		}

		public static BaseEnemyController[] GetAllEnemiesOnScreen()
		{
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			List<BaseEnemyController> list = new List<BaseEnemyController>();
			for (int i = 0; i < enemiesOnScreen.Count; i++)
			{
				if (IsValid(enemiesOnScreen[i]))
				{
					list.Add(enemiesOnScreen[i]);
				}
			}
			return list.ToArray();
		}

		public static BaseEnemyController FindClosestEnemyInScreenRange(Vector2 currentPosition)
		{
			BaseEnemyController result = null;
			float num = float.PositiveInfinity;
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			int count = enemiesOnScreen.Count;
			for (int i = 0; i < count; i++)
			{
				BaseEnemyController baseEnemyController = enemiesOnScreen[i];
				if (IsValid(baseEnemyController))
				{
					float sqrMagnitude = (baseEnemyController.GetHurtBoxPosition() - currentPosition).sqrMagnitude;
					if (sqrMagnitude < num)
					{
						num = sqrMagnitude;
						result = baseEnemyController;
					}
				}
			}
			return result;
		}

		public static BaseEnemyController FindFarthestEnemyInScreenRange(Vector2 currentPosition)
		{
			BaseEnemyController result = null;
			float num = -1f;
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			int count = enemiesOnScreen.Count;
			for (int i = 0; i < count; i++)
			{
				BaseEnemyController baseEnemyController = enemiesOnScreen[i];
				if (IsValid(baseEnemyController))
				{
					float sqrMagnitude = (baseEnemyController.GetHurtBoxPosition() - currentPosition).sqrMagnitude;
					if (sqrMagnitude > num)
					{
						num = sqrMagnitude;
						result = baseEnemyController;
					}
				}
			}
			return result;
		}

		public static BaseEnemyController FindClosestEnemyInScreenRangeExclude(Vector2 currentPosition, HashSet<BaseEnemyController> exclusion = null)
		{
			BaseEnemyController result = null;
			float num = float.PositiveInfinity;
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			int count = enemiesOnScreen.Count;
			for (int i = 0; i < count; i++)
			{
				BaseEnemyController baseEnemyController = enemiesOnScreen[i];
				if (IsValid(baseEnemyController) && (exclusion == null || !exclusion.Contains(baseEnemyController)))
				{
					float sqrMagnitude = (baseEnemyController.GetHurtBoxPosition() - currentPosition).sqrMagnitude;
					if (sqrMagnitude < num)
					{
						num = sqrMagnitude;
						result = baseEnemyController;
					}
				}
			}
			return result;
		}

		public static BaseEnemyController FindFarthestEnemyInScreenRangeExclude(Vector2 currentPosition, HashSet<BaseEnemyController> exclusion = null)
		{
			BaseEnemyController result = null;
			float num = -1f;
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			int count = enemiesOnScreen.Count;
			for (int i = 0; i < count; i++)
			{
				BaseEnemyController baseEnemyController = enemiesOnScreen[i];
				if (IsValid(baseEnemyController) && (exclusion == null || !exclusion.Contains(baseEnemyController)))
				{
					float sqrMagnitude = (baseEnemyController.GetHurtBoxPosition() - currentPosition).sqrMagnitude;
					if (sqrMagnitude > num)
					{
						num = sqrMagnitude;
						result = baseEnemyController;
					}
				}
			}
			return result;
		}

		public static BaseEnemyController[] GetAllEnemies()
		{
			Dictionary<int, BaseEnemyController>.ValueCollection values = EnemyAIManager.Instance.AllEnemies.Values;
			List<BaseEnemyController> list = new List<BaseEnemyController>();
			foreach (BaseEnemyController item in values)
			{
				if (IsValid(item))
				{
					list.Add(item);
				}
			}
			return list.ToArray();
		}

		public static BaseEnemyController FindClosestEnemyExclude(Vector2 currentPosition, HashSet<BaseEnemyController> exclusion = null)
		{
			BaseEnemyController result = null;
			float num = float.PositiveInfinity;
			foreach (BaseEnemyController value in EnemyAIManager.Instance.AllEnemies.Values)
			{
				if (IsValid(value) && (exclusion == null || !exclusion.Contains(value)))
				{
					float sqrMagnitude = (value.GetHurtBoxPosition() - currentPosition).sqrMagnitude;
					if (sqrMagnitude < num)
					{
						num = sqrMagnitude;
						result = value;
					}
				}
			}
			return result;
		}

		public static BaseEnemyController FindFarthestEnemyExclude(Vector2 currentPosition, HashSet<BaseEnemyController> exclusion = null)
		{
			BaseEnemyController result = null;
			float num = -1f;
			foreach (BaseEnemyController value in EnemyAIManager.Instance.AllEnemies.Values)
			{
				if (IsValid(value) && (exclusion == null || !exclusion.Contains(value)))
				{
					float sqrMagnitude = (value.GetHurtBoxPosition() - currentPosition).sqrMagnitude;
					if (sqrMagnitude > num)
					{
						num = sqrMagnitude;
						result = value;
					}
				}
			}
			return result;
		}

		public static BaseEnemyController[] FindEnemiesInCircleRange(Vector2 currentPosition, float minDetectionRadius, float maxDetectionRadius)
		{
			List<BaseEnemyController> list = new List<BaseEnemyController>();
			FindEnemiesInCircleRangeNonAlloc(currentPosition, minDetectionRadius, maxDetectionRadius, list);
			return list.ToArray();
		}

		public static void FindEnemiesInCircleRangeNonAlloc(Vector2 currentPosition, float minDetectionRadius, float maxDetectionRadius, List<BaseEnemyController> resultsList)
		{
			resultsList.Clear();
			float num = minDetectionRadius * minDetectionRadius;
			float num2 = maxDetectionRadius * maxDetectionRadius;
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			int count = enemiesOnScreen.Count;
			for (int i = 0; i < count; i++)
			{
				BaseEnemyController baseEnemyController = enemiesOnScreen[i];
				if (IsValid(baseEnemyController))
				{
					float sqrMagnitude = (baseEnemyController.GetHurtBoxPosition() - currentPosition).sqrMagnitude;
					if (sqrMagnitude >= num && sqrMagnitude <= num2)
					{
						resultsList.Add(baseEnemyController);
					}
				}
			}
		}

		public static BaseEnemyController FindClosestEnemyInCircleRange(Vector2 currentPosition, float minDetectionRadius, float maxDetectionRadius)
		{
			BaseEnemyController result = null;
			float num = minDetectionRadius * minDetectionRadius;
			float num2 = maxDetectionRadius * maxDetectionRadius;
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			int count = enemiesOnScreen.Count;
			for (int i = 0; i < count; i++)
			{
				BaseEnemyController baseEnemyController = enemiesOnScreen[i];
				if (IsValid(baseEnemyController))
				{
					float sqrMagnitude = (baseEnemyController.GetHurtBoxPosition() - currentPosition).sqrMagnitude;
					if (sqrMagnitude >= num && sqrMagnitude < num2)
					{
						num2 = sqrMagnitude;
						result = baseEnemyController;
					}
				}
			}
			return result;
		}

		public static BaseEnemyController FindFarthestEnemyInCircleRange(Vector2 currentPosition, float minDetectionRadius, float maxDetectionRadius)
		{
			BaseEnemyController result = null;
			float num = minDetectionRadius * minDetectionRadius;
			float num2 = maxDetectionRadius * maxDetectionRadius;
			float num3 = num;
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			int count = enemiesOnScreen.Count;
			for (int i = 0; i < count; i++)
			{
				BaseEnemyController baseEnemyController = enemiesOnScreen[i];
				if (IsValid(baseEnemyController))
				{
					float sqrMagnitude = (baseEnemyController.GetHurtBoxPosition() - currentPosition).sqrMagnitude;
					if (sqrMagnitude <= num2 && sqrMagnitude >= num3)
					{
						num3 = sqrMagnitude;
						result = baseEnemyController;
					}
				}
			}
			return result;
		}

		public static void FindEnemiesInConeRangeNonAlloc(Vector2 origin, Vector2 direction, float minDetectionRadius, float maxDetectionRadius, float coneAngle, List<BaseEnemyController> resultsList)
		{
			resultsList.Clear();
			direction.Normalize();
			float num = minDetectionRadius * minDetectionRadius;
			float num2 = maxDetectionRadius * maxDetectionRadius;
			float num3 = Mathf.Cos(coneAngle * 0.5f * (MathF.PI / 180f));
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			int count = enemiesOnScreen.Count;
			for (int i = 0; i < count; i++)
			{
				BaseEnemyController baseEnemyController = enemiesOnScreen[i];
				if (IsValid(baseEnemyController) && baseEnemyController.gameObject.activeInHierarchy)
				{
					Vector2 vector = baseEnemyController.GetHurtBoxPosition() - origin;
					float sqrMagnitude = vector.sqrMagnitude;
					if (!(sqrMagnitude < num) && !(sqrMagnitude > num2) && Vector2.Dot(direction, vector.normalized) >= num3)
					{
						resultsList.Add(baseEnemyController);
					}
				}
			}
		}

		public static BaseEnemyController FindClosestEnemyInConeRange(Vector2 origin, Vector2 direction, float minDetectionRadius, float maxDetectionRadius, float coneAngle)
		{
			direction.Normalize();
			float num = minDetectionRadius * minDetectionRadius;
			float num2 = maxDetectionRadius * maxDetectionRadius;
			float num3 = Mathf.Cos(coneAngle * 0.5f * (MathF.PI / 180f));
			BaseEnemyController result = null;
			float num4 = float.MaxValue;
			List<BaseEnemyController> enemiesOnScreen = EnemyAIManager.Instance.EnemiesOnScreen;
			int count = enemiesOnScreen.Count;
			for (int i = 0; i < count; i++)
			{
				BaseEnemyController baseEnemyController = enemiesOnScreen[i];
				if (IsValid(baseEnemyController))
				{
					Vector2 vector = baseEnemyController.GetHurtBoxPosition() - origin;
					float sqrMagnitude = vector.sqrMagnitude;
					if (!(sqrMagnitude < num) && !(sqrMagnitude > num2) && !(Vector2.Dot(direction, vector.normalized) < num3) && sqrMagnitude < num4)
					{
						num4 = sqrMagnitude;
						result = baseEnemyController;
					}
				}
			}
			return result;
		}

		public static List<BaseEnemyController> FindEnemiesInConeRangeOrderedByDistance(Vector2 origin, Vector2 direction, float minDetectionRadius, float maxDetectionRadius, float coneAngle)
		{
			List<BaseEnemyController> list = new List<BaseEnemyController>();
			FindEnemiesInConeRangeNonAlloc(origin, direction, minDetectionRadius, maxDetectionRadius, coneAngle, list);
			list.Sort(delegate(BaseEnemyController a, BaseEnemyController b)
			{
				float sqrMagnitude = (a.GetHurtBoxPosition() - origin).sqrMagnitude;
				float sqrMagnitude2 = (b.GetHurtBoxPosition() - origin).sqrMagnitude;
				return sqrMagnitude.CompareTo(sqrMagnitude2);
			});
			return list;
		}
	}
}
