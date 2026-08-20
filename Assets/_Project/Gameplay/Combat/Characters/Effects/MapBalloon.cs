using AstralShift.Helpers;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.Characters.Effects
{
	public class MapBalloon : MonoBehaviour, IPausable
	{
		public CharacterBalloonController.BalloonType balloonType = CharacterBalloonController.BalloonType.Save;

		public CharacterBalloonController balloonController;

		public bool intermitent = true;

		public float popupDelay = 0.75f;

		public float popupTime = 3f;

		public float popupInterval = 10f;

		private Transform player;

		private Vector3 position;

		private bool isclose;

		private bool ispaused;

		private void Start()
		{
			player = GameDirector.Instance.Player.transform;
			position = base.transform.position;
		}

		private void OnBecameVisible()
		{
			if (!ispaused)
			{
				if (intermitent)
				{
					InvokeRepeating("ShowPopup", popupDelay, popupInterval);
				}
				else
				{
					balloonController.DisplayBalloon(show: true, balloonType);
				}
			}
			InvokeRepeating("DistanceCheck", 0f, 0.5f);
		}

		private void OnBecameInvisible()
		{
			CancelInvoke();
		}

		private void ShowPopup()
		{
			balloonController.DisplayBalloon(show: true, balloonType);
			StartCoroutine(Wait.SetTimeout(popupTime, delegate
			{
				balloonController.DisplayBalloon(show: false);
			}));
		}

		private void DistanceCheck()
		{
			if (ispaused)
			{
				return;
			}
			if (Vector2.Distance(position, player.position) < player.lossyScale.x * 2f)
			{
				balloonController.DisplayBalloon(show: false);
				CancelInvoke("ShowPopup");
				isclose = true;
			}
			else if (isclose)
			{
				isclose = false;
				if (intermitent)
				{
					InvokeRepeating("ShowPopup", popupDelay, popupInterval);
				}
				else
				{
					balloonController.DisplayBalloon(show: true, balloonType);
				}
			}
		}

		public void OnPausePausables()
		{
			balloonController.DisplayBalloon(show: false);
			ispaused = true;
		}

		public void OnResumePausables()
		{
			ispaused = false;
			isclose = true;
		}

		private void OnDestroy()
		{
		}
	}
}
