using System.Collections;
using Animancer;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
	public class LevelUpAnimation : MonoBehaviour
	{
		[SerializeField]
		private Animator anim;

		[SerializeField]
		private AnimancerComponent animancerComponent;

		[SerializeField]
		private ClipTransition levelUpAnimationClip;

		[SerializeField]
		private ClipTransition vanishAnimationClip;

		[SerializeField]
		private EventReference levelUpSound;

		[Header("Knockback Settings")]
		[SerializeField]
		private float knockbackRadius;

		[SerializeField]
		private LayerMask enemyLayers;

		[SerializeField]
		private KnockbackSettings knockbackSettings;

		public void StartAnimation()
		{
			animancerComponent.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			animancerComponent.Stop();
			animancerComponent.Layers[0].Play(levelUpAnimationClip);
			RuntimeManager.PlayOneShot(levelUpSound);
		}

		public void TriggerLevelUp()
		{
			Leveler.Instance.EvalLevelUp();
		}

		public void KnockbackEnemies()
		{
			RaycastHit2D[] array = Physics2D.CircleCastAll(base.transform.position, knockbackRadius, Vector2.up, 0f, enemyLayers);
			if (array.Length == 0)
			{
				return;
			}
			RaycastHit2D[] array2 = array;
			foreach (RaycastHit2D raycastHit2D in array2)
			{
				if (raycastHit2D.transform.TryGetComponent<BaseEnemyController>(out var component))
				{
					component.BruteforceKnockBack(base.transform.position, knockbackSettings);
				}
			}
		}

		public void DisableLevelUpAnimation()
		{
			animancerComponent.UpdateMode = AnimatorUpdateMode.Normal;
			animancerComponent.Layers[0].Play(vanishAnimationClip);
		}

		public IEnumerator DelayDisableLevelUpAnimation(float delay)
		{
			yield return new WaitForSeconds(delay);
			animancerComponent.Layers[0].Play(vanishAnimationClip);
		}
	}
}
