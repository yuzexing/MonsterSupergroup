using System.ComponentModel;
using AstralShift.HellMaiden.Characters;
using AstralShift.HellMaiden.Timeline.TimelineCharacterMovement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	[TrackColor(0.111f, 0.111f, 0.4f)]
	[TrackClipType(typeof(CharacterMovementClip))]
	[TrackClipType(typeof(SetDirectionClip))]
	[TrackClipType(typeof(CharacterAnimationClip))]
	[TrackClipType(typeof(AnimationAndTweenClip))]
	[TrackClipType(typeof(EmoteClip))]
	[TrackBindingType(typeof(Transform))]
	[DisplayName("AstralShift/Cutscenes/Character Movement Track")]
	public class CharacterMovementTrack : AstralTrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			ScriptPlayable<CharacterMovementTrackMixer> scriptPlayable = ScriptPlayable<CharacterMovementTrackMixer>.Create(graph, inputCount);
			foreach (TimelineClip clip in GetClips())
			{
				if (clip.asset.GetType() == typeof(CharacterMovementClip))
				{
					CharacterMovementClip characterMovementClip = clip.asset as CharacterMovementClip;
					clip.displayName = "Tween";
					CharacterMovement component = (go.GetComponent<PlayableDirector>().GetGenericBinding(this) as Transform).GetComponent<CharacterMovement>();
					Transform transform = characterMovementClip.startLocation.Resolve(graph.GetResolver());
					Transform transform2 = characterMovementClip.endLocation.Resolve(graph.GetResolver());
					if (transform == null || transform2 == null)
					{
						break;
					}
					if (characterMovementClip.walkingSpeed)
					{
						float magnitude = (transform.position - transform2.position).magnitude;
						float moveSpeed = component.MoveSpeed;
						double num = magnitude / moveSpeed;
						clip.duration = num;
					}
				}
				else if (clip.asset.GetType() == typeof(SetDirectionClip))
				{
					SetDirectionClip setDirectionClip = clip.asset as SetDirectionClip;
					clip.displayName = setDirectionClip.directionToFace.ToString();
				}
				else if (clip.asset.GetType() == typeof(CharacterAnimationClip))
				{
					CharacterAnimationClip characterAnimationClip = clip.asset as CharacterAnimationClip;
					clip.displayName = characterAnimationClip.animation.name;
					if (characterAnimationClip.loop)
					{
						clip.duration = characterAnimationClip.AnimDuration;
						if (characterAnimationClip.loopTimes != 0)
						{
							clip.displayName = clip.displayName + "\t◀\ufe0f L" + characterAnimationClip.loopTimes;
						}
					}
					else if (clip.duration < (double)characterAnimationClip.animDuration)
					{
						clip.duration = characterAnimationClip.animDuration;
					}
					else if (clip.duration > (double)characterAnimationClip.animDuration)
					{
						clip.displayName += "\t◀\ufe0f HOLD";
					}
				}
				else if (clip.asset.GetType() == typeof(AnimationAndTweenClip))
				{
					AnimationAndTweenClip animationAndTweenClip = clip.asset as AnimationAndTweenClip;
					clip.displayName = "Tween (" + animationAndTweenClip.animation.name + ")";
				}
				else if (clip.asset.GetType() == typeof(EmoteClip))
				{
					EmoteClip emoteClip = clip.asset as EmoteClip;
					clip.displayName = $"**{emoteClip.emoji}**";
				}
			}
			SetClipsMinimumSize();
			return scriptPlayable;
		}

		public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
		{
			base.GatherProperties(director, driver);
		}

		protected override void SetClipsMinimumSize()
		{
			foreach (TimelineClip clip in GetClips())
			{
				if (!(clip.asset.GetType() == typeof(CharacterAnimationClip)) && clip.duration < 0.5)
				{
					clip.duration = 0.5;
				}
			}
		}
	}
}
