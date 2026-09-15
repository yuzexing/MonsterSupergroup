using System;
using System.Collections;
using System.Collections.Generic;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class LimboCoroutineProbe : MonoBehaviour
    {
        public ServerWaveSchedule Schedule;
        public readonly List<int> ScheduledFrames = new List<int>();
        public readonly List<int> CoroutineFrames = new List<int>();
        private readonly int[] alive = new int[1];
        private void Update()
        {
            if (Schedule == null || ScheduledFrames.Count == 4) return;
            if (Schedule.TickReference(Time.timeAsDouble, Time.frameCount, true, alive[0], alive[0], alive, out var spawn))
            { Schedule.Resolve(spawn, true); ScheduledFrames.Add(Time.frameCount); alive[0]++; }
        }
        public IEnumerator ReferenceYields()
        {
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitWhile(() => false);
                CoroutineFrames.Add(Time.frameCount);
                yield return new WaitForSeconds(0);
            }
        }
    }

    public sealed class LimboCoroutineTimingTests
    {
        [UnityTest]
        public IEnumerator ZeroIntervalCurveAndLimitedSpawnsMatchRealUnityYieldSpacing()
        {
            foreach (var mode in new[] { ReferenceSpawnMode.CurveBudget, ReferenceSpawnMode.AliveTarget })
            {
                var go = new GameObject("Limbo coroutine oracle");
                try
                {
                    var probe = go.AddComponent<LimboCoroutineProbe>();
                    var clip = new ReferenceSpawnDefinition { Mode = mode, SourceEnemy = "yield-spacing-fixture", Start = 0,
                        End = 20, Count = 4, Cooldown = 0, Timestamps = new float[4] };
                    var reference = new ReferenceWaveProgram(new[] { clip }, Array.Empty<ReferenceBarrierDefinition>(), 30, 841.5766649882,
                        AnimationCurve.Linear(0, 0, 1, 1), 1.5f, 1, 2, 1.5f, 30, 5, 20, 6, 1.41f, 1, 1);
                    probe.Schedule = new ServerWaveSchedule("oracle", new WaveParameters(reference, Array.Empty<GameObject>(), 1000, 100), Time.timeAsDouble);
                    probe.StartCoroutine(probe.ReferenceYields());
                    double deadline = Time.realtimeSinceStartupAsDouble + 5;
                    while ((probe.ScheduledFrames.Count < 4 || probe.CoroutineFrames.Count < 4) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                    Assert.That(probe.ScheduledFrames.Count, Is.EqualTo(4)); Assert.That(probe.CoroutineFrames.Count, Is.EqualTo(4));
                    for (int i = 1; i < 4; i++)
                        Assert.That(probe.ScheduledFrames[i] - probe.ScheduledFrames[i - 1],
                            Is.EqualTo(probe.CoroutineFrames[i] - probe.CoroutineFrames[i - 1]), mode + " must preserve both yielded waits");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
        }
    }
}
