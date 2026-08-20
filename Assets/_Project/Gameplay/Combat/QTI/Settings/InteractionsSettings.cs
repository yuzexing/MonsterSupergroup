using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.QTI.Settings
{
	[CreateAssetMenu(fileName = "InteractionsSettings", menuName = "QTI/ScriptableObjects/InteractionsSettings", order = 2)]
	public class InteractionsSettings : ScriptableObject
	{
		public static PrioritiesEnumSelector dynamicEnumSelector = new PrioritiesEnumSelector();

		private static InteractionsSettings instance;

		[Header("Priorities")]
		[SerializeField]
		private string prioritiesEnumFolder = "Assets/Quick_Trigger_Interaction/Enums";

		[SerializeField]
		private string prioritiesEnumAssetName = "PrioritiesEnum";

		[Header("Physics Triggers")]
		[SerializeField]
		private bool forceInputTriggerLayer;

		[LayerSelector]
		[SerializeField]
		private int inputTriggerLayer;

		[SerializeField]
		private bool forceCollisionTriggerLayer;

		[LayerSelector]
		[SerializeField]
		private int collisionTriggerLayer;

		public string PrioritiesEnumFolder => prioritiesEnumFolder;

		public string PrioritiesEnumAssetName => prioritiesEnumAssetName;

		public bool ForceInputTriggerLayer => forceInputTriggerLayer;

		public bool ForceCollisionTriggerLayer => forceCollisionTriggerLayer;

		public int InputTriggerLayer => inputTriggerLayer;

		public int CollisionTriggerLayer => collisionTriggerLayer;

		public static InteractionsSettings Instance
		{
			get
			{
				return instance;
			}
			set
			{
				instance = value;
			}
		}

		public int GetCollisionTriggerLayer(int layer)
		{
			if (ForceCollisionTriggerLayer)
			{
				return CollisionTriggerLayer;
			}
			return layer;
		}

		public int GetInputTriggerLayer(int layer)
		{
			if (ForceInputTriggerLayer)
			{
				return InputTriggerLayer;
			}
			return layer;
		}

		public LayerMask AssignInputTriggerLayerMask(LayerMask layerMask)
		{
			if (ForceInputTriggerLayer)
			{
				return 1 << InputTriggerLayer;
			}
			return layerMask;
		}
	}
}
