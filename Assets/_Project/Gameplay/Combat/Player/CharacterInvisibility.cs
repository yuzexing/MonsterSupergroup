using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
	public class CharacterInvisibility : MonoBehaviour
	{
		[Tooltip("SpriteRenderers you want to be toggled when visible/invisible")]
		public List<SpriteRenderer> spriteRenderers;

		[Tooltip("Character Light Parent, every light child will be made visible/invisible")]
		public Transform lightsParent;

		[Tooltip("Objects you want to be culled by the camera when made invisible")]
		public List<GameObject> visibleObjects;

		private Dictionary<int, int> _objectLayers;

		public void TurnOffSprite()
		{
			for (int i = 0; i < spriteRenderers.Count; i++)
			{
				spriteRenderers[i].enabled = false;
			}
		}

		public void TurnOnSprite()
		{
			for (int i = 0; i < spriteRenderers.Count; i++)
			{
				spriteRenderers[i].enabled = true;
			}
		}

		public void TurnOffLights()
		{
			lightsParent.gameObject.SetActive(value: false);
		}

		public void TurnOnLights()
		{
			lightsParent.gameObject.SetActive(value: true);
		}

		public void TurnOffRender()
		{
			_objectLayers = new Dictionary<int, int>();
			int layer = LayerMask.NameToLayer("NotRendered");
			for (int i = 0; i < visibleObjects.Count; i++)
			{
				int layer2 = visibleObjects[i].layer;
				_objectLayers.Add(visibleObjects[i].GetHashCode(), layer2);
				visibleObjects[i].layer = layer;
			}
		}

		public void TurnOnRender()
		{
			for (int i = 0; i < visibleObjects.Count; i++)
			{
				visibleObjects[i].layer = _objectLayers[visibleObjects[i].GetHashCode()];
			}
		}
	}
}
