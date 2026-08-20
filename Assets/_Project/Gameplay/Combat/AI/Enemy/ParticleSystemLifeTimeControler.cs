using UnityEngine;

public class ParticleSystemLifeTimeControler : MonoBehaviour
{
	public int[] indexes;

	public void SetParticleSystemLifeTime(ParticleSystem ps, float lifeTime = 1f)
	{
		Transform child = ps.transform;
		for (int i = 0; i < indexes.Length; i++)
		{
			child = child.GetChild(indexes[i]);
		}
		ParticleSystem.MainModule main = child.GetComponent<ParticleSystem>().main;
		main.startLifetime = lifeTime;
	}
}
