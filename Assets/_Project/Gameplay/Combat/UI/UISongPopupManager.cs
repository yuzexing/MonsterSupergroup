using System.Collections;
using Animancer;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Scenes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI
{
	public class UISongPopupManager : MonoBehaviour
	{
		public static UISongPopupManager instance;

		[SerializeField]
		private float defaultShowTime = 1f;

		[SerializeField]
		private TextMeshProUGUI text;

		[SerializeField]
		private AnimancerComponent animancerComponent;

		[SerializeField]
		private ClipTransition songPopupAppear;

		[SerializeField]
		private ClipTransition songPopupDisappear;

		[SerializeField]
		private ContentSizeFitter contentSizeFitter;

		private float waitDuration = 1f;

		[SerializeField]
		private bool deactivate = true;

		private void Awake()
		{
			if (instance == null)
			{
				instance = this;
			}
		}

		private void Start()
		{
			if (!deactivate)
			{
				SceneMaster.Instance.OnSceneShowFinish += ShowFinishSongNameOnSceneFinish;
				MusicPlayer.Instance.onNextTrack += ShowSongName;
			}
		}

		private void OnDestroy()
		{
			if (!deactivate)
			{
				MusicPlayer.Instance.onNextTrack -= ShowSongName;
			}
		}

		public void ShowFinishSongNameOnSceneFinish()
		{
			SceneMaster.Instance.OnSceneShowFinish -= ShowFinishSongNameOnSceneFinish;
			ShowSongName();
		}

		public void ShowSongName()
		{
			MusicPlayer.Instance.CurrentMusicEvent.getDescription(out var description);
			description.getUserPropertyByIndex(0, out var property);
			ShowSongName(property.name, defaultShowTime);
		}

		public void ShowSongName(string songName, float duration)
		{
			waitDuration = duration;
			text.text = songName;
			LayoutRebuilder.ForceRebuildLayoutImmediate(base.transform as RectTransform);
			contentSizeFitter.enabled = false;
			contentSizeFitter.enabled = true;
			animancerComponent.Play(songPopupAppear);
		}

		public void HideSongName()
		{
			StartCoroutine(HideCoroutine());
		}

		private IEnumerator HideCoroutine()
		{
			yield return new WaitForSeconds(waitDuration);
			animancerComponent.Play(songPopupDisappear);
		}
	}
}
