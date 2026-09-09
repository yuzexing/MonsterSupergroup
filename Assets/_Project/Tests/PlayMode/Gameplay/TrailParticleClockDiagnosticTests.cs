using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class TrailParticleClockDiagnosticTests
    {
        [UnityTest]
        public IEnumerator AuthoredFireStoppedSeekReportsEveryChildClock()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Dash/GameObject/FIRE.prefab");
            Assert.That(prefab, Is.Not.Null);
            var go = UnityEngine.Object.Instantiate(prefab);
            ParticleSystem particle = go.GetComponent<ParticleSystem>();
            try
            {
                particle.Clear(true);
                particle.Play(true);
                Dump("play", particle);
                particle.Simulate(0.625f, true, true);
                Dump("emission-seek-0.625", particle);
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Dump("stop", particle);
                particle.Simulate(0.775f, true, false);
                Dump("tail-seek-0.775", particle);
                particle.Pause(true);
                Dump("pause", particle);
                yield return null;
                Dump("one-frame-after-pause", particle);
                for (int index = 0; index < 5; index++)
                {
                    particle.Simulate(0.016f, true, false);
                    Dump("fixed-delta-0.016-" + index, particle);
                }
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Dump("stop-after-seek", particle);
                yield return null;
                Dump("one-frame-after-final-stop", particle);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
#else
            yield break;
#endif
        }

        private static void Dump(string phase, ParticleSystem root)
        {
            Debug.Log("[TrailParticleClock] phase=" + phase + " allAlive=" + root.IsAlive(true) + " | " +
                string.Join(" | ", root.GetComponentsInChildren<ParticleSystem>(true).Select(particle =>
                    particle.name + ": ownAlive=" + particle.IsAlive(false) + " count=" + particle.particleCount +
                    " play=" + particle.isPlaying + " pause=" + particle.isPaused + " stop=" + particle.isStopped +
                    " emit=" + particle.isEmitting + " emissionEnabled=" + particle.emission.enabled +
                    " time=" + particle.time.ToString("F5") + " loop=" + particle.main.loop +
                    " speed=" + particle.main.simulationSpeed + " lifetimeMax=" + particle.main.startLifetime.constantMax)));
        }
    }
}

