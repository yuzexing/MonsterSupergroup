using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Included only in validation players. Captures actual Boot -> Gameplay frames.
    public sealed class LustSinnerVisualProcessProbe : MonoBehaviour
    {
        private string directory;
        private float deadline;
        private bool failed;
        private BootGameplayNetworkManager manager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            const string prefix = "--lust-visual=";
            string option = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix));
            if (option == null) return;
            var probe = new GameObject("LustSinner visual validation").AddComponent<LustSinnerVisualProcessProbe>();
            probe.directory = option.Substring(prefix.Length);
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            deadline = Time.realtimeSinceStartup + 90;
            Directory.CreateDirectory(directory);
            Application.logMessageReceived += CheckLog;
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0 && !failed)
            {
                object current;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    current = stack.Peek().Current;
                    if (current is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception ex) { Debug.LogException(ex); failed = true; break; }
                yield return current;
            }
            Application.logMessageReceived -= CheckLog;
            Debug.Log("[LustVisual] result=" + (failed ? "FAIL" : "PASS"));
            Application.Quit(failed ? 1 : 0);
        }

        private void CheckLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) failed = true;
        }

        private IEnumerator Until(Func<bool> condition)
        {
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("LustSinner visual stage timed out.");
                yield return null;
            }
        }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var frame = ScreenCapture.CaptureScreenshotAsTexture();
            var pixels = frame.GetPixels32();
            if (pixels.Count(p => p.r > 20 || p.g > 20 || p.b > 20) < pixels.Length / 100 ||
                pixels.Count(p => p.r > 248 && p.g > 248 && p.b > 248) > pixels.Length * .6)
                throw new InvalidOperationException("Blank or opaque-compositor game frame: " + name);
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), frame.EncodeToPNG());
            Destroy(frame);
        }

        private IEnumerator Run()
        {
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            manager.ConfigurePreparationFlow(false);
            if (!manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7998, false, out var error)) throw new InvalidOperationException(error);
            manager.StartHost();
            yield return Until(() => NetworkClient.localPlayer != null && manager.IsGameplayLoaded && NetworkEnemySimulationWorld.Instance.HasEligiblePlayer);
            foreach (var spawner in FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None))
            {
                spawner.Configure(spawner.EnemyPrefab, 5);
                spawner.enabled = false;
            }
            var owner = NetworkClient.localPlayer;
            var origin = owner.transform.position;
            var prefab = manager.spawnPrefabs.Single(p => p.name == "NetworkEnemyLustSinner");
            manager.BeginRun();
            var enemies = new List<EnemyController>();
            foreach (float x in new[] {-4f, 4f})
            {
                var root = Instantiate(prefab, origin + new Vector3(x, 0, 0), Quaternion.identity);
                var agent = root.GetComponent<NetworkEnemySimulationAgent>();
                agent.ConfigureInitialServerTarget(owner.netId);
                NetworkServer.Spawn(root);
                yield return Until(() => agent.ProductEnemyInitialized);
                var controller = root.GetComponent<EnemyController>();
                controller.Movement.StopMovement();
                enemies.Add(controller);
            }
            yield return new WaitForSeconds(.3f);
            yield return Capture("01-left-right-movement");
            foreach (var enemy in enemies) enemy.Attack();
            yield return Until(() => enemies.All(e => e.CurrentAttackPresentationPhase == EnemyAttackPresentationPhase.Warning));
            yield return new WaitForSeconds(.1f);
            yield return Capture("02-left-right-warning");
            yield return Until(() => enemies.All(e => e.CurrentAttackPresentationPhase == EnemyAttackPresentationPhase.Active));
            yield return new WaitForSeconds(.03f);
            yield return Capture("03-left-right-active");
            yield return Until(() => enemies.All(e => e.CurrentAttackPresentationPhase == EnemyAttackPresentationPhase.Recovery));
            yield return Capture("04-left-right-recovery");
            var world = NetworkCombatWorld.Instance;
            world.Gateway.Ledger.RegisterSource(900009, 900009);
            uint sequence = 1;
            foreach (var enemy in enemies)
            {
                ulong eventId = 910000 + sequence;
                var death = world.Gateway.ProcessBatch(900009, new CombatSubmissionBatch {
                    BatchSequence = sequence, Results = new[] { new CombatResult {
                        EventId = eventId, RootEventId = eventId, Sequence = (uint)eventId,
                        SourcePlayerId = 900009, SourceEntityId = 900009,
                        TargetEntityId = enemy.GetComponent<NetworkIdentity>().netId,
                        Damage = 50, DamageSourceId = 6, DamageTags = (ulong)CombatTags.Damage
                    } }
                }, NetworkTime.time);
                sequence++;
                if (death.Entities == null || !death.Entities.Any(e => e.Health == 0))
                    throw new InvalidOperationException("Visual fixture lethal damage was rejected.");
                typeof(NetworkCombatWorld).GetMethod("Broadcast", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(world, new object[] {death});
            }
            yield return Until(() => enemies.All(e => e == null));
            yield return new WaitForSeconds(.2f);
            yield return Capture("05-death-and-drops");
            manager.StopHost();
            yield return Until(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
        }
    }
}
