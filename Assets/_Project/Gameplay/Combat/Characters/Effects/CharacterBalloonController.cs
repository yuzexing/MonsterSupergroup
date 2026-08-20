using System;
using FMODUnity;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.Characters.Effects
{
	public class CharacterBalloonController : MonoBehaviour
	{
		[Serializable]
		public struct BalloonDeformType
		{
			public BalloonType type;

			public Sprite balloonSprite;

			public Sprite iconSprite;
		}

		[Serializable]
		public struct BalloonAnimationType
		{
			public BalloonType type;

			public Sprite BalloonSprite;
		}

		[Serializable]
		public struct EmojiSound
		{
			public EmojiType emoji;

			public EventReference sound;
		}

		public enum BalloonType
		{
			None = 0,
			Inspect = 1,
			Dialogue = 2,
			Ignite = 3,
			Story = 4,
			LoveEvent = 5,
			Task = 6,
			RozenConflict = 7,
			QuestionMark = 8,
			ExclamationMark = 9,
			Thread = 10,
			PressZ = 11,
			Shop = 12,
			StoryTime = 13,
			Door = 14,
			Save = 15,
			Tutorial = 16,
			Endings = 17,
			Favors = 18
		}

		public enum EmojiType
		{
			None = 0,
			Drop = 1,
			Heart = 2,
			Mad = 3,
			Music = 4,
			Sleep = 5,
			Star = 6,
			Tenten = 7,
			Pichi = 8,
			QuestionMark = 9,
			ExclamationMark = 10,
			Dots = 11,
			Muffy = 12
		}

		public Animator animator;

		public Image balloon;

		public Image staticBalloonIcon;

		public Image dynamicBalloonIcon;

		public Sprite emojiBalloon;

		public BalloonDeformType[] DeformBalloons;

		public BalloonAnimationType[] AnimationBalloons;

		public EmojiSound[] EmojiSounds;

		private static readonly int ShowAnimHash = Animator.StringToHash("Show");

		private static readonly string StaticTrigger = "Static";

		private static readonly int EmojiHash = Animator.StringToHash("Emoji");

		public bool IsShowingBaloon { get; set; }

		public void DisplayBalloon(bool show, BalloonType balloonType = BalloonType.None)
		{
			IsShowingBaloon = show;
			if (show)
			{
				balloon.enabled = true;
				for (int i = 0; i < DeformBalloons.Length; i++)
				{
					if (DeformBalloons[i].type == balloonType)
					{
						dynamicBalloonIcon.enabled = false;
						staticBalloonIcon.enabled = true;
						balloon.sprite = DeformBalloons[i].balloonSprite;
						staticBalloonIcon.sprite = DeformBalloons[i].iconSprite;
						Show(show, StaticTrigger);
					}
				}
				for (int j = 0; j < AnimationBalloons.Length; j++)
				{
					if (AnimationBalloons[j].type == balloonType)
					{
						staticBalloonIcon.enabled = false;
						dynamicBalloonIcon.enabled = true;
						balloon.sprite = AnimationBalloons[j].BalloonSprite;
						Show(show, balloonType.ToString());
						break;
					}
				}
			}
			else
			{
				Hide();
			}
		}

		public void DisplayEmoji(EmojiType emojiType)
		{
			if (emojiType == EmojiType.Tenten || emojiType == EmojiType.Pichi)
			{
				balloon.enabled = false;
				staticBalloonIcon.enabled = false;
				dynamicBalloonIcon.enabled = false;
				animator.Play(emojiType.ToString());
			}
			else
			{
				balloon.enabled = true;
				balloon.sprite = emojiBalloon;
				balloon.color = Color.white;
				staticBalloonIcon.enabled = false;
				dynamicBalloonIcon.enabled = true;
				animator.SetTrigger(emojiType.ToString());
				animator.SetTrigger(EmojiHash);
			}
			for (int i = 0; i < EmojiSounds.Length; i++)
			{
				if (emojiType == EmojiSounds[i].emoji)
				{
					if (!EmojiSounds[i].sound.IsNull)
					{
						RuntimeManager.PlayOneShotAttached(EmojiSounds[i].sound, base.gameObject);
					}
					break;
				}
			}
		}

		private void Show(bool show, string trigger)
		{
			animator.SetBool(ShowAnimHash, show);
			animator.SetTrigger(trigger);
		}

		private void Hide()
		{
			animator.SetBool(ShowAnimHash, value: false);
		}

		private void ResetAllTriggers()
		{
			AnimatorControllerParameter[] parameters = animator.parameters;
			foreach (AnimatorControllerParameter animatorControllerParameter in parameters)
			{
				if (animatorControllerParameter.type == AnimatorControllerParameterType.Trigger)
				{
					animator.ResetTrigger(animatorControllerParameter.name);
				}
			}
		}
	}
}
