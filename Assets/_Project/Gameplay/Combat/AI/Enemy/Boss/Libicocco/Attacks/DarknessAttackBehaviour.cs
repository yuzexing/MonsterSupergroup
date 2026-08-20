using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.AI.Boss;
using FMODUnity;
using Sirenix.Utilities;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AstralShift.HellMaiden.AI.Enemy.Boss.Libicocco.Attacks
{
	public class DarknessAttackBehaviour : BossAttackBehaviour
	{
		[SerializeField]
		private Light2D light;

		[SerializeField]
		private Light2D replacementLight;

		[SerializeField]
		private float lightsFadeOutDuration;

		[SerializeField]
		private float lightsOutDuration;

		[SerializeField]
		private float lightsFadeInDuration;

		private float startIntensity;

		private float finalIntensity;

		[SerializeField]
		private bool fullDarkness;

		[SerializeField]
		private bool infinite;

		[SerializeField]
		private EventReference darknessSound;

		[SerializeField]
		private List<AnimatedBossAttack> darkAttacks;

		private List<AnimatedBossAttack> darkAttacksFiltered;

		[SerializeField]
		private AnimatedBossAttack fullScreenDarkAttack;

		private void Start()
		{
			darkAttacksFiltered = darkAttacks.ToArray().ToList();
			if (light != null)
			{
				startIntensity = light.intensity;
			}
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
			AttackBossAnimation(Callback);
			if (infinite)
			{
				onAttackEnd?.Invoke();
			}
			void Callback()
			{
				StartCoroutine(fullDarkness ? PlayWithLightFogFullDarkness() : PlayWithLightFog());
				RuntimeManager.PlayOneShot(darknessSound);
			}
		}

		private IEnumerator PlayWithLight()
		{
			float tmpLightDuration = 0f;
			float angle = Random.Range(0, 4) * 90;
			replacementLight.transform.Rotate(Vector3.forward, angle);
			while (tmpLightDuration < lightsFadeOutDuration)
			{
				light.intensity = Mathf.Lerp(startIntensity, finalIntensity, tmpLightDuration / lightsFadeOutDuration);
				replacementLight.intensity = Mathf.Lerp(finalIntensity, startIntensity, tmpLightDuration / lightsFadeOutDuration);
				tmpLightDuration += Time.deltaTime;
				yield return null;
			}
			yield return new WaitForSeconds(lightsOutDuration);
			tmpLightDuration = 0f;
			while (tmpLightDuration < lightsFadeInDuration)
			{
				light.intensity = Mathf.Lerp(finalIntensity, startIntensity, tmpLightDuration / lightsFadeInDuration);
				replacementLight.intensity = Mathf.Lerp(startIntensity, finalIntensity, tmpLightDuration / lightsFadeInDuration);
				tmpLightDuration += Time.deltaTime;
				yield return null;
			}
			EndAttack();
			yield return null;
		}

		private IEnumerator PlayWithLightFog()
		{
			AnimatedBossAttack darkAttack = darkAttacksFiltered[Random.Range(0, darkAttacksFiltered.Count)];
			darkAttack.gameObject.SetActive(value: true);
			darkAttack.RunInAnimation();
			yield return new WaitForSeconds(lightsFadeOutDuration);
			darkAttack.GetComponentsInChildren<ParticleSystem>().ForEach(delegate(ParticleSystem p)
			{
				p.Play();
			});
			yield return new WaitForSeconds(lightsOutDuration);
			darkAttack.RunOutAnimation();
			MonoBehaviour.print("AAA " + darkAttack.outAnimation.Length);
			yield return new WaitForSeconds(darkAttack.outAnimation.Length + 0.01f);
			darkAttack.gameObject.SetActive(value: false);
			darkAttacksFiltered = darkAttacks.ToArray().ToList();
			darkAttacksFiltered.Remove(darkAttack);
			EndAttack();
			yield return null;
		}

		private IEnumerator PlayWithLightFogFullDarkness()
		{
			AnimatedBossAttack darkAttack = fullScreenDarkAttack;
			darkAttack.gameObject.SetActive(value: true);
			darkAttack.GetComponentsInChildren<ParticleSystem>().ForEach(delegate(ParticleSystem p)
			{
				p.Play();
			});
			darkAttack.RunInAnimation();
			yield return new WaitForSeconds(lightsFadeOutDuration);
			yield return new WaitForSeconds(lightsOutDuration);
			darkAttack.RunOutAnimation();
			yield return new WaitForSeconds(darkAttack.outAnimation.Length + 0.01f);
			darkAttack.gameObject.SetActive(value: false);
			EndAttack();
			yield return null;
		}

		private IEnumerator PlayWithLightFullDarkness()
		{
			float tmpLightDuration = 0f;
			while (tmpLightDuration < lightsFadeOutDuration)
			{
				light.intensity = Mathf.Lerp(startIntensity, finalIntensity, tmpLightDuration / lightsFadeOutDuration);
				tmpLightDuration += Time.deltaTime;
				yield return null;
			}
			yield return new WaitForSeconds(lightsOutDuration);
			tmpLightDuration = 0f;
			while (tmpLightDuration < lightsFadeInDuration)
			{
				light.intensity = Mathf.Lerp(finalIntensity, startIntensity, tmpLightDuration / lightsFadeInDuration);
				tmpLightDuration += Time.deltaTime;
				yield return null;
			}
			EndAttack();
			yield return null;
		}

		private void EndAttack()
		{
			if (!infinite)
			{
				onAttackEnd?.Invoke();
			}
			else
			{
				StartCoroutine(fullDarkness ? PlayWithLightFogFullDarkness() : PlayWithLightFog());
			}
		}

		public override void Dispose()
		{
			StopAllCoroutines();
			if (light != null)
			{
				light.intensity = startIntensity;
			}
			if (replacementLight != null)
			{
				replacementLight.intensity = 0f;
			}
		}
	}
}
