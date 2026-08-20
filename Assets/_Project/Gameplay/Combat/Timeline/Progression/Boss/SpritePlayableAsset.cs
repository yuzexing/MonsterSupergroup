using System;
using UnityEngine;
using UnityEngine.Playables;

[Serializable]
public class SpritePlayableAsset : PlayableAsset
{
	public Sprite sprite;

	public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
	{
		return Playable.Null;
	}
}
