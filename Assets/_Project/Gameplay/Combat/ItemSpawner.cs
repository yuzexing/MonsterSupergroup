using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Items;
using UnityEngine;

public class ItemSpawner : SerializedProgressable
{
	public WorldItem worldItemToSpawn;

	[Header("Item Spawn Settings")]
	[SerializeField]
	private float minSpawnDistance;

	[SerializeField]
	private float maxSpawnDistance;

	public override void Init()
	{
		WorldItem worldItem = LootManager.Instance.GetWorldItem(worldItemToSpawn);
		if (worldItem == null)
		{
			Debug.Log("Couldn't spawn " + worldItemToSpawn.name + "!");
			return;
		}
		Vector2 normalized = Random.insideUnitCircle.normalized;
		worldItem.transform.position = GameDirector.Instance.Player.CurrentPosition + (Vector3)normalized * Random.Range(minSpawnDistance, maxSpawnDistance);
		worldItem.Show();
	}

	public override void ProgressUpdate()
	{
	}

	public override void End()
	{
	}
}
