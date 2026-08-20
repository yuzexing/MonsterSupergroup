using System.Collections.Generic;
using AstralShift.HellMaiden.Characters;
using AstralShift.HellMaiden.Dialogue;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class NPCLoader : MonoBehaviour
	{
		public List<Transform> poetLocations;

		public NPC_DS_LUT NPC_DS_LUT;

		private Transform spawnParent;

		public void Init(Transform spawnParent)
		{
			this.spawnParent = spawnParent;
		}

		public GameObject SpawnPoet(PoetID poetID)
		{
			int index = Random.Range(0, poetLocations.Count);
			GameObject obj = Object.Instantiate(NPC_DS_LUT.LUT[poetID], spawnParent);
			obj.transform.position = poetLocations[index].position;
			Vector2 facingDirection = ((poetLocations[index].localScale.x != 0f) ? ((poetLocations[index].localScale.x < 0f) ? Vector2.left : Vector2.right) : ((Random.Range(-10f, 10f) < 0f) ? Vector2.left : Vector2.right));
			obj.GetComponent<CharacterMovement>().FacingDirection = facingDirection;
			obj.name = NPC_DS_LUT.LUT[poetID].name;
			poetLocations.Remove(poetLocations[index]);
			return obj;
		}
	}
}
