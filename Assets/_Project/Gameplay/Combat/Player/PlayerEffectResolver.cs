using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Scenes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
	public class PlayerEffectResolver : MonoBehaviour
	{
		[Serializable]
		private struct ModifierEffectPair
		{
			public PerkModifierID modifier;

			public GameObject gameObject;
		}

		[SerializeField]
		private ModifierEffectPair[] _modifierEffects;

		private Dictionary<uint, GameObject> _tempModifiersVisuals;

		private Dictionary<uint, int> _tempModifiersCount;

		public void Init()
		{
			_tempModifiersVisuals = new Dictionary<uint, GameObject>();
			_tempModifiersCount = new Dictionary<uint, int>();
			for (int i = 0; i < _modifierEffects.Length; i++)
			{
				uint key = _modifierEffects[i].modifier;
				_tempModifiersVisuals.Add(key, _modifierEffects[i].gameObject);
				_tempModifiersCount.Add(key, 0);
			}
			SceneMaster.Instance.OnSceneInitPersist += ClearAllTemporaryEffects;
		}

		private void OnDestroy()
		{
			SceneMaster.Instance.OnSceneInitPersist -= ClearAllTemporaryEffects;
		}

		public void ApplyEffect(uint modifierID)
		{
			if (_tempModifiersCount.ContainsKey(modifierID))
			{
				_tempModifiersCount[modifierID]++;
				_tempModifiersCount[modifierID] = Mathf.Clamp(_tempModifiersCount[modifierID], 0, int.MaxValue);
				if (_tempModifiersVisuals.TryGetValue(modifierID, out var value))
				{
					value.SetActive(value: true);
				}
			}
		}

		public void RemoveEffect(uint modifierID)
		{
			if (_tempModifiersCount.ContainsKey(modifierID))
			{
				_tempModifiersCount[modifierID]--;
				_tempModifiersCount[modifierID] = Mathf.Clamp(_tempModifiersCount[modifierID], 0, int.MaxValue);
				if (_tempModifiersVisuals.TryGetValue(modifierID, out var value) && _tempModifiersCount[modifierID] == 0)
				{
					value.SetActive(value: false);
				}
			}
		}

		private void ClearAllTemporaryEffects()
		{
			uint[] array = _tempModifiersCount.Keys.ToArray();
			for (int i = 0; i < array.Length; i++)
			{
				_tempModifiersCount[array[i]] = 0;
			}
			foreach (GameObject value in _tempModifiersVisuals.Values)
			{
				value.SetActive(value: false);
			}
		}
	}
}
