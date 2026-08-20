using System.Collections.Generic;
using AstralShift.HellMaiden.Items;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class DropLootInteraction : Interaction
	{
		public LootSettingsData items;

		public Vector2 LootPositionOffset;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			List<WorldItem> overridenLoot = LootManager.Instance.GetOverridenLoot(0f, items);
			for (int i = 0; i < overridenLoot.Count; i++)
			{
				Vector2 normalized = Random.insideUnitCircle.normalized;
				overridenLoot[i].Show();
				overridenLoot[i].transform.position = base.transform.position + (Vector3)normalized * Random.Range(LootPositionOffset.x, LootPositionOffset.y);
			}
			OnEnd();
		}
	}
}
