using System.Collections;
using AstralShift.HellMaiden.AI.Boss;
using AstralShift.Helpers.Camera;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy.Boss.Libicocco.Attacks
{
	public class ShadowSpotAttackBehaviour : BossAttackBehaviour
	{
		[SerializeField]
		private AnimatedBossAttack shadowSpot;

		[SerializeField]
		private float shadowSpotFadeOutDuration;

		[SerializeField]
		private float shadowSpotDuration;

		[SerializeField]
		private float shadowSpotFadeInDuration;

		private void Start()
		{
			shadowSpot.gameObject.SetActive(value: false);
		}

		public override void Positioning()
		{
			onPositioningEnd?.Invoke();
		}

		public override void Warning()
		{
			BarkWarning();
			WarningBossAnimation(onWarningEnd);
		}

		public override void Attack()
		{
			AttackBossAnimation(delegate
			{
				StartCoroutine(ApplyShadow());
			});
		}

		private IEnumerator ApplyShadow()
		{
			shadowSpot.gameObject.SetActive(value: true);
			shadowSpot.GetComponent<ParticleSystem>().Play();
			shadowSpot.RunInAnimation();
			shadowSpot.GetComponentInChildren<SpriteRenderer>();
			shadowSpot.transform.localPosition = Vector3.zero;
			shadowSpot.GetComponent<CameraFollow2D>().target = GameDirector.Instance.Player.transform;
			float time = 0f;
			while (time < shadowSpotFadeOutDuration)
			{
				Mathf.Lerp(0f, 1f, time / shadowSpotFadeOutDuration);
				time += Time.deltaTime;
				yield return null;
			}
			yield return new WaitForSeconds(shadowSpotDuration);
			time = 0f;
			shadowSpot.RunOutAnimation();
			while (time < shadowSpotFadeInDuration)
			{
				Mathf.Lerp(1f, 0f, time / shadowSpotFadeInDuration);
				time += Time.deltaTime;
				yield return null;
			}
			StopAttack();
			onAttackEnd?.Invoke();
			yield return null;
		}

		public override void Dispose()
		{
			StopAttack();
			base.Dispose();
		}

		private void StopAttack()
		{
			shadowSpot.gameObject.SetActive(value: false);
		}
	}
}
