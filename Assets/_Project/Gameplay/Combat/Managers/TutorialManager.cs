using System;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.DevDebug;
using AstralShift.UI.PopupWindows;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class TutorialManager : MonoBehaviour
{
	[Serializable]
	public struct HUDTutorials
	{
		[SerializeField]
		private PopupWindowContentReference overViewTutorialReference;

		public async UniTask<bool> TryLaunchHUDTutorial(Action onEnd)
		{
			await PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.Overlay, new PopupContext((Action)delegate
			{
			}, (Action)delegate
			{
				onEnd?.Invoke();
			}, overViewTutorialReference));
			return true;
		}
	}

	[Serializable]
	public struct ControlsTutorials
	{
		[SerializeField]
		private ControlsTutorial controlsTutorialPrefab;

		public bool TryLaunchControlsTutorial(Action onEnd)
		{
			UnityEngine.Object.Instantiate(controlsTutorialPrefab).Init(onEnd);
			return true;
		}
	}

	[Serializable]
	public struct CPMTutorials
	{
		[SerializeField]
		private PopupWindowContentReference modCardsTutorialReference;

		[SerializeField]
		private PopupWindowContentReference weaponCardsTutorialReference;

		[SerializeField]
		private PopupWindowContentReference handManagementTutorialReference;

		[SerializeField]
		private PopupWindowContentReference mergingCardsTutorialReference;

		public async UniTask<bool> TryLaunchModCardsTutorial()
		{
			if (!DeveloperDebug.EnableTutorialPopups)
			{
				return false;
			}
			if (!GameDataManager.GetGameTriggerState("Tutorial_ModCards") || DeveloperDebug.ForceTutorialPopups)
			{
				bool isFinished = false;
				await PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.Multipage, new PopupContext((Action)delegate
				{
				}, (Action)delegate
				{
					GameDataManager.RegisterGameTrigger("Tutorial_ModCards", state: true);
					isFinished = true;
				}, modCardsTutorialReference));
				await UniTask.WaitUntil(() => isFinished);
				return true;
			}
			return false;
		}

		public async UniTask<bool> TryLaunchWeaponCardsTutorial()
		{
			if (!DeveloperDebug.EnableTutorialPopups)
			{
				return false;
			}
			if (!GameDataManager.GetGameTriggerState("Tutorial_WeaponCards") || DeveloperDebug.ForceTutorialPopups)
			{
				bool isFinished = false;
				await PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.Multipage, new PopupContext((Action)delegate
				{
				}, (Action)delegate
				{
					GameDataManager.RegisterGameTrigger("Tutorial_WeaponCards", state: true);
					isFinished = true;
				}, weaponCardsTutorialReference));
				await UniTask.WaitUntil(() => isFinished);
				return true;
			}
			return false;
		}

		public async UniTask<bool> TryLaunchHandManagementCardsTutorial()
		{
			if (!DeveloperDebug.EnableTutorialPopups)
			{
				return false;
			}
			if (!GameDataManager.GetGameTriggerState("Tutorial_HandManagement") || DeveloperDebug.ForceTutorialPopups)
			{
				bool isFinished = false;
				await PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.Multipage, new PopupContext((Action)delegate
				{
				}, (Action)delegate
				{
					GameDataManager.RegisterGameTrigger("Tutorial_HandManagement", state: true);
					isFinished = true;
				}, handManagementTutorialReference));
				await UniTask.WaitUntil(() => isFinished);
				return true;
			}
			return false;
		}

		public async UniTask<bool> TryLaunchMergingCardsTutorial()
		{
			if (!DeveloperDebug.EnableTutorialPopups)
			{
				return false;
			}
			if (!GameDataManager.GetGameTriggerState("Tutorial_MergingCards") || DeveloperDebug.ForceTutorialPopups)
			{
				bool isFinished = false;
				await PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.Multipage, new PopupContext((Action)delegate
				{
				}, (Action)delegate
				{
					GameDataManager.RegisterGameTrigger("Tutorial_MergingCards", state: true);
					isFinished = true;
				}, mergingCardsTutorialReference));
				await UniTask.WaitUntil(() => isFinished);
				return true;
			}
			return false;
		}
	}

	[Serializable]
	public struct CSMTutorials
	{
		[SerializeField]
		private PopupWindowContentReference charmCardsTutorialReference;

		public async UniTask<bool> TryLaunchCharmCardsTutorial()
		{
			if (!DeveloperDebug.EnableTutorialPopups)
			{
				return false;
			}
			if (!GameDataManager.GetGameTriggerState("Tutorial_Charms") || DeveloperDebug.ForceTutorialPopups)
			{
				bool isFinished = false;
				await PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.Multipage, new PopupContext((Action)delegate
				{
				}, (Action)delegate
				{
					GameDataManager.RegisterGameTrigger("Tutorial_Charms", state: true);
					isFinished = true;
				}, charmCardsTutorialReference));
				await UniTask.WaitUntil(() => isFinished);
				return true;
			}
			return false;
		}
	}

	[Serializable]
	public struct SSMTutorials
	{
		[SerializeField]
		private PopupWindowContentReference signatureCardsTutorialReference;

		public async UniTask<bool> TryLaunchSignatureCardsTutorial(Action onEnd)
		{
			if (!DeveloperDebug.EnableTutorialPopups)
			{
				onEnd?.Invoke();
				return false;
			}
			if (!GameDataManager.GetGameTriggerState("Tutorial_SignatureWeapons") || DeveloperDebug.ForceTutorialPopups)
			{
				await PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.Multipage, new PopupContext((Action)delegate
				{
				}, (Action)delegate
				{
					GameDataManager.RegisterGameTrigger("Tutorial_SignatureWeapons", state: true);
					onEnd?.Invoke();
				}, signatureCardsTutorialReference));
				return true;
			}
			onEnd?.Invoke();
			return false;
		}
	}

	[Serializable]
	public struct SLMTutorials
	{
		[SerializeField]
		private PopupWindowContentReference spellCardsTutorialReference;

		public async UniTask<bool> TryLaunchSpellCardsTutorial(Action onEnd)
		{
			if (!DeveloperDebug.EnableTutorialPopups)
			{
				onEnd?.Invoke();
				return false;
			}
			if (!GameDataManager.GetGameTriggerState("Tutorial_Spells") || DeveloperDebug.ForceTutorialPopups)
			{
				await PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.Multipage, new PopupContext((Action)delegate
				{
				}, (Action)delegate
				{
					GameDataManager.RegisterGameTrigger("Tutorial_Spells", state: true);
					onEnd?.Invoke();
				}, spellCardsTutorialReference));
				return true;
			}
			onEnd?.Invoke();
			return false;
		}
	}

	[Serializable]
	public struct MetaTutorials
	{
		[SerializeField]
		private PopupWindowContentReference metaProgressionTutorialReference;

		public async UniTask<bool> TryLaunchMetaProgressionTutorial()
		{
			if (!DeveloperDebug.EnableTutorialPopups)
			{
				return false;
			}
			if (!GameDataManager.GetGameTriggerState("Tutorial_MetaProgressionMenu") || DeveloperDebug.ForceTutorialPopups)
			{
				bool isFinished = false;
				await PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.Multipage, new PopupContext((Action)delegate
				{
				}, (Action)delegate
				{
					GameDataManager.RegisterGameTrigger("Tutorial_MetaProgressionMenu", state: true);
					isFinished = true;
				}, metaProgressionTutorialReference));
				await UniTask.WaitUntil(() => isFinished);
			}
			return false;
		}
	}

	public static TutorialManager Instance;

	[SerializeField]
	private ControlsTutorials _controlsTutorials;

	[Space]
	[SerializeField]
	private HUDTutorials _hudTutorials;

	[Space]
	[SerializeField]
	private CPMTutorials _cpmTutorials;

	[Space]
	[SerializeField]
	private CSMTutorials _csmTutorials;

	[Space]
	[SerializeField]
	private SSMTutorials _ssmTutorials;

	[Space]
	[SerializeField]
	private SLMTutorials _slmTutorials;

	[Space]
	[SerializeField]
	private MetaTutorials _metaTutorials;

	public ControlsTutorials Controls => _controlsTutorials;

	public HUDTutorials HUD => _hudTutorials;

	public CPMTutorials CPM => _cpmTutorials;

	public CSMTutorials CSM => _csmTutorials;

	public SSMTutorials SSM => _ssmTutorials;

	public SLMTutorials SLM => _slmTutorials;

	public MetaTutorials META => _metaTutorials;

	public void Init()
	{
		Instance = this;
	}
}
