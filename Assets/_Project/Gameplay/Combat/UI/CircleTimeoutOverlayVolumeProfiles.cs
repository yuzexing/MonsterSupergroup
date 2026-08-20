using System;
using AstralShift.HellMaiden.Scenes;
using UnityEngine;
using UnityEngine.Rendering;

namespace AstralShift.HellMaiden.UI
{
	[CreateAssetMenu(fileName = "Circle Timeout Volume Profiles", menuName = "HellMaiden/Data/Circle Timeout Volume Profiles")]
	public class CircleTimeoutOverlayVolumeProfiles : ScriptableObject
	{
		[Serializable]
		public struct ProfileEntry
		{
			[SerializeField]
			private SceneEnum scene;

			[SerializeField]
			private VolumeProfile profile;

			public SceneEnum Scene => scene;

			public VolumeProfile Profile => profile;
		}

		[SerializeField]
		private ProfileEntry[] profiles;

		public VolumeProfile GetProfile(SceneEnum scene)
		{
			return Array.Find(profiles, (ProfileEntry e) => e.Scene == scene).Profile;
		}
	}
}
