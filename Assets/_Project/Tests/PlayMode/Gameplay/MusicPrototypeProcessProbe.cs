#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Test assemblies only. Files coordinate phases; every cast/beat uses production owner commands,
    // the real audio clock, Mirror admission, combat settlement and the existing pickup schedule.
    public sealed class MusicPrototypeProcessProbe : MonoBehaviour
    {
        private string role, artifacts, failure;
        private ushort port;
        private double deadline;
        private bool finished, impaired;
        private BootGameplayNetworkManager manager;
        private EnemyDefinitionRuntimeFixture enemies;
        private readonly Dictionary<uint, int> enemyHealth = new Dictionary<uint, int>();
        private readonly HashSet<ulong> acceptedEvents = new HashSet<ulong>();
        private readonly HashSet<uint> killedEnemies = new HashSet<uint>();
        private readonly HashSet<uint> dropDecisions = new HashSet<uint>();
        private readonly HashSet<ulong> spawnedDrops = new HashSet<ulong>();
        private int appliedDamage;
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkPlayerMusic Music => Owner.GetComponent<NetworkPlayerMusic>();
        private NetworkPlayerPrototypeAbilities Abilities => Owner.GetComponent<NetworkPlayerPrototypeAbilities>();
        private NetworkModifierSelection Upgrades => Owner.GetComponent<NetworkModifierSelection>();
        private ModifierSelectionController Selection => Owner.GetComponent<ModifierSelectionController>();
        private PlayerMovement Player => Owner.GetComponent<PlayerMovement>();
        private NetworkPlayerMusic[] ServerMusicians => NetworkServer.connections.Values
            .Where(c => c.identity != null).Select(c => c.identity.GetComponent<NetworkPlayerMusic>()).Where(m => m != null).ToArray();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            const string flag = "--music-role=";
            string selected = args.FirstOrDefault(a => a.StartsWith(flag));
            if (selected == null) return;
            var probe = new GameObject("Music two-process validation").AddComponent<MusicPrototypeProcessProbe>();
            probe.role = selected.Substring(flag.Length);
            probe.artifacts = Path.GetFullPath(args.First(a => a.StartsWith("--music-artifacts=")).Substring(18));
            string selectedPort = args.FirstOrDefault(a => a.StartsWith("--music-port="));
            probe.port = selectedPort == null ? (ushort)7998 : ushort.Parse(selectedPort.Substring(13));
            probe.impaired = args.Contains("--music-impaired");
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 120;
            Application.logMessageReceived += ObserveLog;
            deadline = Time.realtimeSinceStartupAsDouble + 150;
            yield return Guard(Run());
            if (!finished) Finish(true);
        }

        private void Update()
        {
            if (finished || deadline == 0) return;
            if (failure != null) { Finish(false); return; }
            if (Time.realtimeSinceStartupAsDouble > deadline)
            { failure = "Timed out; inspect the last phase markers and role log."; Finish(false); }
            else if (Has("failed-" + (role == "host" ? "client" : "host")))
            { failure = "Peer failed: " + Read("failed-" + (role == "host" ? "client" : "host")); Finish(false); }
        }

        private IEnumerator Guard(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count != 0 && !finished)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    next = stack.Peek().Current;
                    if (next is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error)
                { failure = error.ToString(); Debug.LogException(error); Finish(false); yield break; }
                yield return next;
            }
        }

        private IEnumerator Run()
        {
            Require(role == "host" || role == "client", "Expected --music-role=host or client.");
            Directory.CreateDirectory(artifacts);
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Music validation must start through Boot.");
            manager.ConfigurePreparationFlow(false);
            // Late placement isolates finale range; retaining HP is necessary to audit total damage.
            enemies = new EnemyDefinitionRuntimeFixture(manager, resetOnReposition: false);
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            Require(backend.TryPrepareKcp("127.0.0.1", port, impaired,
                out string error), error);
            if (impaired)
            {
                var simulation = backend.ConfiguredLatencySimulation as LatencySimulation;
                Require(simulation != null, "Latency simulation was not configured.");
                simulation.latency = 100;
                simulation.jitter = .02f;
                simulation.unreliableLoss = 0;
                simulation.unreliableScramble = 0;
            }
            if (role == "host")
            {
                manager.StartHost();
                var gluttony = GluttonyParameters.Defaults;
                gluttony.PassiveEnabled = false;
                NetworkCombatWorld.Instance.ServerConfigureGluttony(gluttony, false);
                Mark("listening");
            }
            else
            {
                while (!Has("listening")) yield return null;
                manager.StartClient();
            }
            StartCoroutine(Guard(RunOwner()));
            if (role == "host")
            {
                yield return RunServer();
                while (!Has("stopped-client")) yield return null;
                manager.StopHost();
            }
            else
            {
                while (!Has("server-verified") || !Has("owner-done-client")) yield return null;
                manager.StopClient();
            }
            while (NetworkClient.active || NetworkServer.active || manager.IsGameplayLoaded || manager.IsGameplayTransitioning)
                yield return null;
            Require(FindObjectsByType<NetworkPlayerMusic>(FindObjectsSortMode.None).Length == 0,
                "A music module survived Gameplay shutdown.");
            Mark("stopped-" + role);
        }

        private IEnumerator RunOwner()
        {
            while (Owner == null || !Abilities.OwnerReady || !Music.Parameters.IsValid || !Selection.IsPresentationReady)
                yield return null;
            Player.SetDirection(Vector2.zero);
            Vector2 position = role == "host" ? new Vector2(-.25f, 0) : new Vector2(.25f, 0);
            Player.body.position = position;
            Player.transform.position = position;
            Physics2D.SyncTransforms();
            yield return Select(PrototypeAbilityId.Music);
            Mark("ready-" + role, Owner.netId.ToString());
            while (!Has("perform")) yield return null;
            uint buildRevision = Upgrades.OwnerBuildRevision;
            Require(Music.RequestStart(), role + ": production owner start was rejected.");
            while (!Music.IsLocalPerforming) yield return null;
            Require(!Music.RequestStart(), "A second start was accepted during one performance.");
            var view = Music.GetComponent<MusicPrototypeView>();
            Require(view.IsPresenting && view.ScheduledSourceCount == Music.Parameters.CountInBeats + Music.Parameters.BeatCount,
                "Owner did not schedule all count-in and judgement tones.");
            int rejectedDuplicates = 0, networkDuplicateRequests = 0;
            var beatCommand = typeof(NetworkPlayerMusic).GetMethod("CmdBeat", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(beatCommand != null, "Cannot resolve the production Mirror beat command for replay validation.");
            for (int beat = 0; beat < Music.Parameters.BeatCount; beat++)
            {
                double when = Music.Parameters.BeatTime(beat);
                while (Music.LocalElapsed < when)
                {
                    AssertRemotePresentationIsSilent();
                    Require(!Upgrades.IsSelecting && !Player.IsUpgradeSelectionLocked && Selection.Offers.Count == 0,
                        "Earned XP opened a selection during an active performance.");
                    yield return null;
                }
                Require(Math.Abs(Music.LocalElapsed - when) <= Music.Parameters.HitWindowSeconds,
                    $"{role}: actual DSP input for beat {beat} missed the hit window at {Music.LocalElapsed:0.000}.");
                double reportedElapsed = Music.LocalElapsed;
                ulong cast = Music.State.CastId;
                Require(Music.RequestBeat(), $"{role}: beat {beat} input was not submitted.");
                // Invoke the woven Command wrapper, not UserCode_CmdBeat or the runtime. A duplicate
                // traverses the same owner/Mirror transport and authoritative admission as the first report.
                beatCommand.Invoke(Music, new object[] { cast, beat, reportedElapsed, null });
                networkDuplicateRequests++;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    Require(!Music.RequestBeat(), "Repeated owner input consumed the same or a future beat.");
                    rejectedDuplicates++;
                }
                if (role == "client" && beat == 3)
                {
                    yield return Select(PrototypeAbilityId.Allure);
                    Require(Music.IsLocalPerforming && view.IsPresenting, "Switching stopped the in-flight performance.");
                    Mark("switched-during-music");
                }
            }
            while (Music.State.Active || Music.State.Hits != 10 || Upgrades.Level < 3) yield return null;
            Require(!Music.State.Cancelled && Music.State.Judged == 10, "Server did not confirm exactly ten successful beats.");
            Require(!Music.IsLocalPerforming && !view.IsPresenting, "Finished performance still presents rhythm.");
            AssertRemotePresentationIsSilent();
            double cooldown = Music.State.CooldownReadyAt;
            while (Selection.Offers.Count == 0) yield return null;
            Require(!Upgrades.OffersDeferred, "Completion retained the reward deferral.");
            while (Upgrades.OwnerBuildRevision < buildRevision + 2 || Upgrades.IsSelecting || Selection.Offers.Count != 0)
            {
                if (Selection.Offers.Count > 0 && !Selection.IsRequestPending)
                    Require(Selection.Select(0).Succeeded, "Resumed reward selection failed.");
                yield return null;
            }
            while (!Abilities.OwnerReady) yield return null;
            if (Abilities.SelectedAbility != PrototypeAbilityId.Allure) yield return Select(PrototypeAbilityId.Allure);
            yield return Select(PrototypeAbilityId.Music);
            Require(Music.State.CooldownReadyAt == cooldown && cooldown > NetworkTime.time,
                "Switching changed the original cooldown deadline.");
            Require(!Music.RequestStart(), "Switching refunded a running cooldown.");
            var result = new OwnerObservation { player = Owner.netId, cast = Music.State.CastId, hits = Music.State.Hits,
                judged = Music.State.Judged, duplicateInputsRejected = rejectedDuplicates, readyAt = cooldown,
                networkDuplicateRequests = networkDuplicateRequests,
                initialBuildRevision = buildRevision, finalBuildRevision = Upgrades.OwnerBuildRevision,
                ownerAudioSources = view.ScheduledSourceCount, switchedDuringPerformance = role == "client" };
            Debug.Log("[MusicProcess] owner=" + role + " " + JsonUtility.ToJson(result));
            Mark("owner-done-" + role, JsonUtility.ToJson(result));
        }

        private IEnumerator RunServer()
        {
            while (!Has("ready-host") || !Has("ready-client") || ServerMusicians.Length != 2 || !manager.CanBeginRun(out _))
                yield return null;
            var gateway = NetworkCombatWorld.Instance.Gateway;
            gateway.CombatResultAccepted += RecordDamage;
            gateway.ConfirmedKillProduced += RecordKill;
            PickupAudit.Recorded += RecordPickup;
            manager.BeginRun();
            while (!enemies.PairReady() || !enemies.PlaceInView()) yield return null;
            foreach (var enemy in enemies.Agents())
            {
                Require(gateway.Ledger.TryGetState(enemy.netId, out var state), "Ordinary enemy has no canonical health.");
                enemyHealth.Add(enemy.netId, state.Health);
            }
            Mark("perform");
            while (ServerMusicians.Any(m => !m.State.Active)) yield return null;
            foreach (var music in ServerMusicians)
            {
                var progression = music.GetComponent<NetworkModifierSelection>();
                float xp = Enumerable.Range(progression.Level, 2).Sum(progression.ExperienceRequiredAtLevel) + .5f;
                Require(progression.TryGrantExperience(xp) && progression.PendingUpgradeCount == 2 &&
                    progression.OffersDeferred && !progression.IsSelecting,
                    "Server did not retain both earned rewards during performance.");
            }
            Mark("xp-queued");
            var finalePrepared = new HashSet<uint>();
            while (!Has("owner-done-host") || !Has("owner-done-client"))
            {
                foreach (var music in ServerMusicians)
                    if (music.State.Active && music.State.Judged >= 9 && finalePrepared.Add(music.netId))
                        foreach (var enemy in enemies.Agents())
                            if (gateway.Ledger.IsAlive(enemy.netId))
                                Require(NetworkEnemySimulationWorld.Instance.RepositionReferenceEnemy(enemy,
                                    (Vector2)music.transform.position + new Vector2(3.5f, 0)),
                                    "Could not place a surviving ordinary enemy in finale range.");
                yield return null;
            }
            var host = JsonUtility.FromJson<OwnerObservation>(Read("owner-done-host"));
            var client = JsonUtility.FromJson<OwnerObservation>(Read("owner-done-client"));
            Require(host.player != client.player && host.cast != client.cast, "Owners shared an identity or cast ID.");
            Require(Has("switched-during-music") && host.hits == 10 && client.hits == 10,
                "Independent performances did not both finish all ten beats.");
            Require(ServerMusicians.All(m => m.State.Hits == 10 && m.State.Judged == 10 && !m.State.Active),
                "Canonical performance state differs from owner receipts.");
            Require(killedEnemies.SetEquals(enemyHealth.Keys), "The two ordinary enemies did not both die from music.");
            Require(appliedDamage == enemyHealth.Values.Sum(), "Canonical music damage was lost or applied more than once.");
            Require(dropDecisions.SetEquals(killedEnemies) && spawnedDrops.Count <= killedEnemies.Count,
                "Kills did not reach the pickup schedule exactly once.");
            string result = $"players=2 hits=20 distinctMusicHits={acceptedEvents.Count} appliedDamage={appliedDamage} " +
                $"kills={killedEnemies.Count} dropDecisions={dropDecisions.Count} spawnedDrops={spawnedDrops.Count}";
            Debug.Log("[MusicProcess] server " + result);
            Mark("server-verified", result);
        }

        private IEnumerator Select(PrototypeAbilityId ability)
        {
            Require(Abilities.RequestSelect(ability), "Ability switch request failed: " + ability);
            while (Abilities.SelectedAbility != ability || Abilities.SelectionPending) yield return null;
        }

        private void AssertRemotePresentationIsSilent()
        {
            foreach (var remote in FindObjectsByType<NetworkPlayerMusic>(FindObjectsSortMode.None))
            {
                if (remote.isOwned) continue;
                var view = remote.GetComponent<MusicPrototypeView>();
                Require(!view.IsPresenting && view.ScheduledSourceCount == 0,
                    "An observer scheduled another player's rhythm audio or judgement ring.");
            }
        }

        private void RecordDamage(CombatResult result, CombatApplyResult applied, double time)
        {
            if (result.AbilityId != ServerCombatGateway.MusicCombatId) return;
            if (!acceptedEvents.Add(result.EventId)) failure = "Duplicate canonical music damage event.";
            appliedDamage += applied.AppliedDamage;
            Debug.Log($"[MusicProcess] damage event={result.EventId} root={result.RootEventId} player={result.SourcePlayerId} " +
                $"enemy={result.TargetEntityId} applied={applied.AppliedDamage} hp={applied.State.Health}");
        }

        private void RecordKill(ConfirmedKill kill)
        {
            if (!enemyHealth.ContainsKey(kill.TargetEntityId)) return;
            if (!killedEnemies.Add(kill.TargetEntityId)) failure = "Ordinary enemy produced duplicate confirmed kills.";
        }

        private void RecordPickup(string kind, string run, ulong drop, string detail)
        {
            if (kind == "drop-decision")
            {
                string field = detail.Split(';').FirstOrDefault(s => s.StartsWith("enemy="));
                if (field != null && uint.TryParse(field.Substring(6), out uint enemy) && enemyHealth.ContainsKey(enemy))
                    if (!dropDecisions.Add(enemy)) failure = "A confirmed music kill reached the pickup schedule twice.";
            }
            if (kind == "spawn" && !spawnedDrops.Add(drop)) failure = "Duplicate pickup spawn identity.";
            if (kind == "drop-decision" || kind == "spawn") Debug.Log($"[MusicProcess] pickup {kind} id={drop} {detail}");
        }

        private bool Has(string name) => File.Exists(Path.Combine(artifacts, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(artifacts, name));
        private void Mark(string name, string text = "ready")
        {
            string destination = Path.Combine(artifacts, name);
            string temporary = destination + "." + role + ".pending";
            File.WriteAllText(temporary, text);
            File.Move(temporary, destination);
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private void ObserveLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                failure ??= message + "\n" + trace;
        }

        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true;
            Application.logMessageReceived -= ObserveLog;
            PickupAudit.Recorded -= RecordPickup;
            if (NetworkCombatWorld.Instance?.Gateway != null)
            {
                NetworkCombatWorld.Instance.Gateway.CombatResultAccepted -= RecordDamage;
                NetworkCombatWorld.Instance.Gateway.ConfirmedKillProduced -= RecordKill;
            }
            enemies?.Dispose();
            string report = passed ? "PASS" : failure ?? "FAIL";
            if (!passed && !Has("failed-" + role)) Mark("failed-" + role, report);
            Mark("result-" + role, report);
            Debug.Log($"[MusicProcess] result={(passed ? "PASS" : "FAIL")} role={role} {report}");
            Application.Quit(passed ? 0 : 1);
        }

        [Serializable]
        private sealed class OwnerObservation
        {
            public uint player, initialBuildRevision, finalBuildRevision;
            public ulong cast;
            public int hits, judged, duplicateInputsRejected, networkDuplicateRequests, ownerAudioSources;
            public double readyAt;
            public bool switchedDuringPerformance;
        }
    }
}
#endif
