using AstralShift.Helpers;
using MonsterSupergroup.Gameplay.Options;
using UnityEngine.Localization;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AstralShift.HellMaiden.Data.Cards
{
	public abstract class CardData : ScriptableObject
	{
		[Header("General Settings")]
		public uint ID;

        [SerializeField] private LocalizedString localizedTitle = new();
        [SerializeField] protected LocalizedString localizedDescription = new();
        [SerializeField] private LocalizedString localizedQuote = new();
        public LocalizedString LocalizedTitle => localizedTitle;
        public LocalizedString LocalizedDescription => localizedDescription;
        public LocalizedString LocalizedQuote => localizedQuote;

		public PoetPoolID poolID;

		public float poolWeight = 1f;

		[Space]
		[SerializeReference]
		private DataDependency[] dependencies;

		[Space]
		[SerializeField]
		protected AssetReference visualDataReference;

		public DataDependency[] Dependencies => dependencies;

		public bool HasDependencies
		{
			get
			{
				if (dependencies != null)
				{
					return dependencies.Length != 0;
				}
				return false;
			}
		}

		public AssetReference VisualDataReference => visualDataReference;

        public virtual string GetTitle() => GameLocalization.Resolve(localizedTitle, ID);
        public virtual string GetDescription() => GameLocalization.Resolve(localizedDescription, ID);
        public virtual bool GetQuote(out string text)
        {
            text = localizedQuote == null || localizedQuote.IsEmpty ? null : GameLocalization.Resolve(localizedQuote, ID);
            return !string.IsNullOrWhiteSpace(text);
        }

		public bool RequestVisualData(out AsyncOperationHandle<CardVisualData> operationHandle)
		{
			return AddressableHelpers.TryLoadAssetAsyncWithHandle(visualDataReference, out operationHandle);
		}
	}
}
