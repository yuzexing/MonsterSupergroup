using AstralShift.HellMaiden.Timeline.TransformTween;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline.TimelineCharacterMovement
{
	public class CharacterMovementClip : TransformTweenClip
	{
		[Header("Character Movement")]
		public bool walkingSpeed;

		public bool runningSpeed;

		public bool isMoonwalking;

		public bool lockZcoord = true;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<TransformTweenBehaviour> scriptPlayable = ScriptPlayable<TransformTweenBehaviour>.Create(graph, template);
			TransformTweenBehaviour behaviour = scriptPlayable.GetBehaviour();
			behaviour.startLocation = startLocation.Resolve(graph.GetResolver());
			behaviour.endLocation = endLocation.Resolve(graph.GetResolver());
			int num = 1;
			if (!behaviour.startLocation || !behaviour.endLocation)
			{
				Debug.LogError(base.name + ": StartLocation or EndLocation not set in timeline clip!");
				return scriptPlayable;
			}
			if (lockZcoord)
			{
				behaviour.startLocation.position = new Vector3(behaviour.startLocation.position.x, behaviour.startLocation.position.y, num);
				behaviour.endLocation.position = new Vector3(behaviour.endLocation.position.x, behaviour.endLocation.position.y, num);
			}
			behaviour.isMoonwalking = isMoonwalking;
			SetPlayableEmoji(behaviour);
			return scriptPlayable;
		}
	}
}
