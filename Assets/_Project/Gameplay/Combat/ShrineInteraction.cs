using System;
using System.Threading;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Shrines;
using AstralShift.HellMaiden.GameStats;
using AstralShift.HellMaiden.UI.HUD;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class ShrineInteraction : Interaction
	{
		public ShrineData shrineData;

		public Animator shrineAnimator;

		public Transform BuffParent;

		public float labelTimeOutTime = 3f;

		public Animator LabelAnimator;

		public TMP_Text text;

		[SerializeField]
		private MinimapIconTarget minimapIconTarget;

		private Animator _buffAnimator;

		[SerializeField]
		private ShrineSFX shrineSFX;

		private CancellationTokenSource _timeoutLabelCTS;

		private int LabelShowParamHash => Animator.StringToHash("Show");

		private int PickedUpParamHash => Animator.StringToHash("PickedUp");

		private void OnEnable()
		{
			minimapIconTarget?.CreateIcon();
		}

		private void OnDisable()
		{
			minimapIconTarget?.DisposeIcon();
		}

		public void Start()
		{
			_buffAnimator = UnityEngine.Object.Instantiate(shrineData.BuffSpherePrefab, BuffParent);
		}

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			RunStatsTracker.Instance?.PlayerStatsEntry.RegisterTempleActivated(shrineData.ID);
			shrineSFX.PoweredShrineSound();
			PlayerHand.Instance.ApplyShrine(shrineData);
			shrineAnimator.SetBool(PickedUpParamHash, value: true);
			_buffAnimator.SetBool(PickedUpParamHash, value: true);
			string term = shrineData.pickupText;
			LocalizationMediator.GetTranslation(ref term);
			text.text = term;
			if (_timeoutLabelCTS == null)
			{
				_timeoutLabelCTS = new CancellationTokenSource();
			}
			RunLabelTimeout(_timeoutLabelCTS.Token);
			minimapIconTarget.DisposeIcon();
			OnEnd();
		}

		private async void RunLabelTimeout(CancellationToken token)
		{
			try
			{
				if ((bool)LabelAnimator)
				{
					LabelAnimator.SetBool(LabelShowParamHash, value: true);
				}
				await UniTask.Delay((int)(labelTimeOutTime * 1000f), DelayType.DeltaTime, PlayerLoopTiming.Update, token, cancelImmediately: true);
				if ((bool)LabelAnimator)
				{
					LabelAnimator.SetBool(LabelShowParamHash, value: false);
				}
				_timeoutLabelCTS.Dispose();
				_timeoutLabelCTS = null;
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}
	}
}
