using AstralShift.Helpers;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AstralShift.HellMaiden.Data.Cards
{
	public abstract class CardData : ScriptableObject
	{
		[Header("General Settings")]
		public uint ID;

		public string Title;

		[TextArea]
		[SerializeField]
		protected string Description;

		[SerializeField]
		protected bool HasQuote;

		[SerializeField]
		[ConditionalHide("HasQuote", true)]
		[TextArea]
		protected string Quote;

		public bool hasLocalization;

		[SerializeField]
		protected string TitleKey;

		[SerializeField]
		protected string DescriptionKey;

		[SerializeField]
		protected string QuoteKey;

		public PoetPoolID poolID;

		public float poolWeight = 1f;

		[Space]
		[SerializeReference]
		private DataDependency[] dependencies;

		[Space]
		[SerializeField]
		protected AssetReference visualDataReference;

		public bool HideQuoteKeyField
		{
			get
			{
				if (hasLocalization)
				{
					return !HasQuote;
				}
				return false;
			}
		}

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

		public virtual string GetTitle()
		{
			if (hasLocalization)
			{
				string term = TitleKey;
				LocalizationMediator.GetTranslation(ref term);
				return term;
			}
			return Title;
		}

		public virtual string GetDescription()
		{
			if (hasLocalization)
			{
				string term = DescriptionKey;
				LocalizationMediator.GetTranslation(ref term);
				return term;
			}
			return Description;
		}

		public virtual bool GetQuote(out string text)
		{
			if (HasQuote)
			{
				if (hasLocalization)
				{
					string term = QuoteKey;
					LocalizationMediator.GetTranslation(ref term);
					text = term;
					return true;
				}
				text = Quote;
				return true;
			}
			text = null;
			return false;
		}

		public bool RequestVisualData(out AsyncOperationHandle<CardVisualData> operationHandle)
		{
			return AddressableHelpers.TryLoadAssetAsyncWithHandle(visualDataReference, out operationHandle);
		}
	}
}
