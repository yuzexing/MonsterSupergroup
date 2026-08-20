using System.Collections;
using AstralShift.FadeEffect;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Scenes;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public class BossSpawner : SerializedProgressable
	{
		[SerializeField]
		private FadeEffectEnum fadeOutEffect = FadeEffectEnum.Minos;

		[SerializeField]
		private FadeEffectEnum fadeInEffect = FadeEffectEnum.Minos;

		public SceneEnum BossScene { get; set; }

		public override void Init()
		{
			StartCoroutine(TransitionCheckRoutine());
		}

		private IEnumerator TransitionCheckRoutine()
		{
			while (PlayerState.IsBusy())
			{
				yield return null;
			}
			LoadBossScene();
			yield return null;
		}

		private void LoadBossScene()
		{
			SceneMaster.Instance.LoadScene(BossScene, fadeOutEffect, fadeInEffect);
		}

		public override void ProgressUpdate()
		{
		}

		public override void End()
		{
		}
	}
}
