using System.Collections.Generic;
using System.Linq;
using AstralShift.Helpers;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class PropSpawner : MonoBehaviour
	{
		public Prop[] props;

		public BoxCollider2D area;

		public Transform parent;

		private List<GameObject> spawnedProps = new List<GameObject>();

		private List<PropAsset> replaceableProps;

		public Vector2Int totalPropsAmount;

		public Dictionary<Prop, int> propAmounts = new Dictionary<Prop, int>();

		public bool jitterOn = true;

		public GridSettings gridSubdivision;

		public async UniTask SpawnPropsAsync(BoxCollider2D area, Transform parent, List<PropAsset> replaceableProps)
		{
			this.area = area;
			this.parent = parent;
			this.replaceableProps = replaceableProps;
			await SpawnPropsAsync();
		}

		public void SpawnProps()
		{
			SpawnPropsAsync();
		}

		public async UniTask SpawnPropsAsync()
		{
			if (area == null)
			{
				Debug.LogError("BoxCollider2D (area) is not assigned!");
				return;
			}
			if (parent == null)
			{
				Debug.LogError("Parent object is not assigned!");
				return;
			}
			if (!AssertMinValues(totalPropsAmount.x, props, out var sum))
			{
				Debug.LogError("Can't possibily generate min amount of props! Increase props max amount or reduce totalProps min amount!");
				return;
			}
			if (totalPropsAmount.y > sum)
			{
				totalPropsAmount.y = sum;
				Debug.LogWarning("Max props value is impossible. Adjusted to sum of props max value!");
			}
			if (totalPropsAmount.x < 0)
			{
				totalPropsAmount.x = 0;
				Debug.LogWarning("Amount of props can't be less than zero!");
			}
			await UniTask.SwitchToMainThread();
			await gridSubdivision.CreateGrid(area);
			await ClearProps();
			Dictionary<Vector2, Prop> usedPositions = new Dictionary<Vector2, Prop>();
			float totalWeight = 0f;
			propAmounts = new Dictionary<Prop, int>();
			Prop[] array = props;
			foreach (Prop prop in array)
			{
				if ((!prop.isPrefabList && prop.prefab == null) || (prop.isPrefabList && (prop.prefabs == null || prop.prefabs.Count == 0)))
				{
					Debug.LogError("Unassigned prop prefab");
					return;
				}
				prop.PrefabsToSpawnIdxList.Clear();
				if (prop.prefab != null)
				{
					prop.Width = prop.prefab.Width;
					prop.Height = prop.prefab.Height;
				}
				if (prop.isPrefabList)
				{
					prop.PrefabsDimensions = new Vector2[prop.prefabs.Count];
					for (int j = 0; j < prop.PrefabsDimensions.Length; j++)
					{
						prop.PrefabsDimensions[j] = new Vector2(prop.prefabs[j].Width, prop.prefabs[j].Height);
					}
				}
				if (prop.minAmount != 0)
				{
					totalWeight += prop.weight;
					propAmounts.Add(prop, prop.minAmount);
				}
			}
			if (propAmounts.Count == 0)
			{
				array = props;
				foreach (Prop prop2 in array)
				{
					totalWeight += prop2.weight;
					propAmounts.Add(prop2, prop2.maxAmount);
				}
			}
			int propsAmount = RandomHelpers.GetRandomInt(totalPropsAmount.x, totalPropsAmount.y + 1);
			MonoBehaviour.print("Spawning " + propsAmount + " props!");
			await UniTask.SwitchToThreadPool();
			Dictionary<Vector2, Transform> positionParents = new Dictionary<Vector2, Transform>();
			int num = 0;
			while (num < propsAmount)
			{
				Prop prop3 = SelectPropByWeight(totalWeight);
				if (prop3 == null)
				{
					Debug.LogError("Couldn't select Prop!");
					return;
				}
				int num2 = propsAmount - num;
				MonoBehaviour.print("remainingAmount = " + num2);
				int num3 = prop3.minAmount;
				int num4 = prop3.maxAmount;
				int num5 = 0;
				if (usedPositions.ContainsValue(prop3))
				{
					num3 = 1;
					num4 = Mathf.Clamp(prop3.maxAmount, 1, prop3.maxAmount - usedPositions.Values.Count((Prop p) => p == prop3));
					if (num3 > num2)
					{
						num3 = num2;
					}
					if (num4 > num2)
					{
						num4 = num2;
					}
					if (num4 > 0)
					{
						num5 = 1;
					}
				}
				else
				{
					num5 = ((num3 == 0) ? 1 : num3);
				}
				MonoBehaviour.print("minAmount = " + num3 + " maxAmount = " + num4);
				MonoBehaviour.print("amountToSpawn = " + num5);
				propAmounts[prop3] -= num5;
				if (propAmounts[prop3] <= 0)
				{
					propAmounts.Remove(prop3);
					totalWeight -= prop3.weight;
					if (propAmounts.Count == 0)
					{
						array = props;
						foreach (Prop prop4 in array)
						{
							totalWeight += prop4.weight;
							propAmounts.Add(prop4, prop4.maxAmount - prop4.minAmount);
						}
					}
				}
				num += num5;
				Debug.Log("PropSpawner: Spawning prop");
				for (int num6 = 0; num6 < num5; num6++)
				{
					int num7 = 0;
					Vector2 vector;
					if (!prop3.isPrefabList)
					{
						vector = new Vector2(prop3.Width, prop3.Height);
					}
					else
					{
						num7 = RandomHelpers.GetRandomInt(0, prop3.PrefabsDimensions.Length);
						vector = prop3.PrefabsDimensions[num7];
					}
					Vector2 gridPosition = gridSubdivision.GetGridPosition(prop3, vector.x, vector.y);
					if (gridPosition != -Vector2.one)
					{
						if (jitterOn)
						{
							float num8 = gridSubdivision.squareSize / 2f;
							float randomFloat = RandomHelpers.GetRandomFloat(0f - num8, num8);
							float randomFloat2 = RandomHelpers.GetRandomFloat(0f - num8, num8);
							gridPosition += new Vector2(randomFloat, randomFloat2);
						}
						usedPositions.Add(gridPosition, prop3);
						positionParents.Add(gridPosition, parent);
						if (prop3.isPrefabList)
						{
							prop3.PrefabsToSpawnIdxList.Add(num7);
						}
					}
				}
				Debug.Log("PropSpawner: Finished spawning prop");
			}
			await UniTask.SwitchToMainThread();
			for (int num9 = 0; num9 < props.Length; num9++)
			{
				props[num9].ResetCounter();
			}
			foreach (KeyValuePair<Vector2, Prop> item in usedPositions)
			{
				PropAsset prefab = item.Value.GetPrefab();
				Transform valueOrDefault = positionParents.GetValueOrDefault(item.Key);
				Vector2 offsetedPosition = prefab.GetOffsetedPosition(item.Key);
				offsetedPosition += new Vector2(parent.position.x, parent.position.y);
				PropAsset propAsset = Object.Instantiate(prefab, offsetedPosition, prefab.transform.rotation, valueOrDefault);
				if (propAsset.isReplaceable && replaceableProps != null)
				{
					replaceableProps.Add(propAsset);
				}
				FlipProp(propAsset.gameObject, item.Value.flipChanceX, item.Value.flipChanceY);
				propAsset.ProceduralGenerate();
				spawnedProps.Add(propAsset.gameObject);
			}
			array = props;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].PrefabsToSpawnIdxList.Clear();
			}
			await UniTask.SwitchToThreadPool();
		}

		private Prop SelectPropByWeight(float totalWeight)
		{
			float randomFloat = RandomHelpers.GetRandomFloat(0f, totalWeight);
			float num = 0f;
			foreach (Prop key in propAmounts.Keys)
			{
				num += key.weight;
				if (randomFloat <= num)
				{
					return key;
				}
			}
			return null;
		}

		private void FlipProp(GameObject prop, float flipChanceX, float flipChanceY)
		{
			SpriteRenderer component = prop.GetComponent<SpriteRenderer>();
			if (component != null)
			{
				bool flipX = RandomHelpers.GetRandomFloat() <= flipChanceX;
				bool flipY = RandomHelpers.GetRandomFloat() <= flipChanceY;
				component.flipX = flipX;
				component.flipY = flipY;
			}
		}

		public UniTask ClearProps(bool clearListOnly = false)
		{
			foreach (GameObject spawnedProp in spawnedProps)
			{
				if (spawnedProp != null && !clearListOnly)
				{
					Object.DestroyImmediate(spawnedProp);
				}
			}
			spawnedProps.Clear();
			return UniTask.CompletedTask;
		}

		public bool AssertMinValues(int min, Prop[] props, out int sum)
		{
			sum = 0;
			for (int i = 0; i < props.Length; i++)
			{
				sum += props[i].maxAmount;
			}
			if (sum < min)
			{
				return false;
			}
			return true;
		}
	}
}
