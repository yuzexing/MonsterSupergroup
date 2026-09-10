using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.Combat.Hand.Data;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Opt-in test assembly fixture. Files synchronize assertions, never production rewards or Build data.
    public sealed class UpgradeSelectionProcessProbe : MonoBehaviour
    {
        private string role, directory;
        private ushort port;
        private bool dedicated, capture, finished;
        private float deadline;
        private BootGameplayNetworkManager manager;
        private WeaponAttackAdmissionFixtureGate gate;
        private GameObject enemyPrefab, assetCopies;
        private readonly HashSet<uint> attacked = new HashSet<uint>();
        private readonly HashSet<uint> stationaryTargets = new HashSet<uint>();
        private string Other => dedicated ? "client2" : "host";
        private bool IsServer => role == "host" || role == "server";
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkModifierSelection Selection => Owner.GetComponent<NetworkModifierSelection>();
        private ModifierSelectionController View => Owner.GetComponent<ModifierSelectionController>();
        private PlayerBuildRuntime Build => Owner.GetComponent<PlayerBuildRuntime>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            string value = args.FirstOrDefault(a => a.StartsWith("--m4-role="));
            if (value == null) return;
            var probe = new GameObject("M4 formal upgrade validation").AddComponent<UpgradeSelectionProcessProbe>();
            probe.role = value.Substring(10);
            probe.directory = args.First(a => a.StartsWith("--m4-artifacts=")).Substring(15);
            probe.port = ushort.Parse(args.First(a => a.StartsWith("--m4-port=")).Substring(10));
            probe.dedicated = args.Contains("--m4-dedicated");
            probe.capture = args.Contains("--m4-capture");
            DontDestroyOnLoad(probe.gameObject);
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true;
            deadline = Time.realtimeSinceStartup + 220;
            SceneManager.sceneLoaded += Prepare;
            yield return Guard(Run());
            if (!finished) Finish(true);
        }
        private IEnumerator Guard(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            while (stack.Count > 0 && !finished)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    next = stack.Peek().Current;
                    if (next is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
                yield return next;
            }
        }
        private void Update()
        {
            if (!finished && deadline > 0 && Time.realtimeSinceStartup > deadline)
            { Debug.LogError("[M4Process] timeout role=" + role + " attacks=" + string.Join(",", attacked)); Finish(false); }
            foreach (var agent in FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None))
                if (agent.ProductEnemyInitialized && stationaryTargets.Add(agent.netId))
                {
                    agent.GetComponent<AstralShift.HellMaiden.AI.Enemy.EnemyController>().Movement.StopMovement();
                    var body = agent.GetComponent<Rigidbody2D>();
                    body.gravityScale = 0;
                    body.linearVelocity = Vector2.zero;
                }
        }
        private void Prepare(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true))
                { enemyPrefab = spawner.EnemyPrefab; spawner.enabled = false; }
        }
        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "M4 must start through Boot.");
            gate = gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            ConfigureFixtureAudio();
            Require(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", port, false, out string error), error);
            if (role == "host") manager.StartHost(); else if (role == "server") manager.StartServer(); else manager.StartClient();
            if (role != "server") StartCoroutine(Guard(ClientScenario()));
            if (IsServer)
            {
                Mark("listening");
                yield return ServerScenario();
                while (!Has("owner-done-" + Other) || !Has("owner-done-client")) yield return null;
                Mark("stop");
                while (!Has("stopped-client") || (dedicated && !Has("stopped-client2"))) yield return null;
                if (role == "host") manager.StopHost(); else manager.StopServer();
            }
            else
            {
                while (!Has("stop") || !Has("owner-done-" + role)) yield return null;
                manager.StopClient();
                while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
                Mark("stopped-" + role);
            }
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active || NetworkServer.active) yield return null;
            Require(FindObjectsByType<CardPickMenu>(FindObjectsSortMode.None).Length == 0, "Menu leaked after Gameplay unload.");
            Require(FindObjectsByType<ModifierSelectionController>(FindObjectsSortMode.None).Length == 0, "Selection survived scene cleanup.");
        }
        private IEnumerator ServerScenario()
        {
            while (!Has("ready-client") || !Has("ready-" + Other)) yield return null;
            var players = Players();
            Require(players.Length == 2, "Expected two formal players.");
            foreach (var player in players)
            {
                var selection = player.GetComponent<NetworkModifierSelection>();
                // Fix only RNG for repeatable coverage, keeping the real rules, six definitions and XP entry.
                typeof(NetworkModifierSelection).GetField("provider", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(selection, new UpgradeOfferProvider(new FirstRandom()));
            }
            Mark("debug-level-client-enabled");
            while (!Has("debug-level-client")) yield return null;
            uint debugClientId = uint.Parse(Read("ready-client"));
            Require(players.Single(p => p.netId != debugClientId).GetComponent<NetworkModifierSelection>().Level == 1,
                "One client's debug request advanced another player.");
            Mark("debug-level-other-enabled");
            while (!Has("debug-level-" + Other)) yield return null;
            foreach (var player in players)
            {
                var selection = player.GetComponent<NetworkModifierSelection>();
                Require(selection.Level == 2 && selection.PendingUpgradeCount == 1 && selection.BuildRevision == 1,
                    "The F5 owner request must queue exactly one level without applying a reward.");
                selection.ServerGrantExperience(selection.ExperiencePerLevel * 16);
                var progress = selection.CaptureProgression();
                Require(progress.Rewards.Select(r => r.EarnedLevel).SequenceEqual(Enumerable.Range(2, 17)), "Bulk XP lost individual earned levels.");
                Require(progress.Rewards.Where(r => r.Kind == UpgradeRewardKind.Weapon).Select(r => r.EarnedLevel).SequenceEqual(new[] { 4, 12, 18 }), "Wrong weapon schedule.");
                Require(progress.Rewards.Count(r => r.Kind == UpgradeRewardKind.Perk) == 6, "Wrong Perk schedule.");
            }
            Debug.Log("[M4Process] event=debug-level-owner-commands-verified players=2");
            Mark("bulk-xp-queued");
            foreach (string phase in new[] { "reward", "target" })
            {
                while (!Has("pause-" + phase)) yield return null;
                uint avatar = uint.Parse(Read("pause-" + phase));
                var player = NetworkServer.spawned[avatar];
                var selection = player.GetComponent<NetworkModifierSelection>();
                ulong participant = player.GetComponent<NetworkRunParticipant>().ParticipantId;
                var before = selection.CaptureProgression();
                int equipmentCount = player.GetComponent<PlayerBuildRuntime>().EquipmentCount;
                ulong old = selection.PendingEventId;
                var other = Players().Single(p => p != player);
                Require(!selection.ServerSelect(other.connectionToClient, old, 0, out _), "Cross-player request was accepted.");
                Require(!selection.ServerSelect(player.connectionToClient, old + 123, 0, out _), "Stale event was accepted.");
                Mark("disconnect-" + phase);
                while (NetworkServer.spawned.ContainsKey(avatar)) yield return null;
                var retained = manager.Session.Participants.Single(p => p.Id == participant);
                Require(retained.Checkpoint != null && retained.Checkpoint.Progression.PendingUpgradeCount == 17, "Pending rewards lost on disconnect.");
                Require(retained.Checkpoint.Progression.Stage == before.Stage, "Checkpoint lost selection stage.");
                Require(Signature(retained.Checkpoint.Progression.Offers) == Signature(before.Offers), "Checkpoint changed issued offers.");
                Require(Signature(retained.Checkpoint.Progression.OriginalOffers) == Signature(before.OriginalOffers), "Checkpoint lost original cards.");
                Mark("checkpoint-" + phase);
                while (!Has("restored-" + phase)) yield return null;
                var restored = NetworkServer.spawned[uint.Parse(Read("restored-" + phase))];
                var resumed = restored.GetComponent<NetworkModifierSelection>();
                typeof(NetworkModifierSelection).GetField("provider", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(resumed, new UpgradeOfferProvider(new FirstRandom()));
                Require(resumed.PendingEventId != old && Signature(resumed.CaptureProgression().Offers) == Signature(before.Offers), "Reconnect rerolled or reused an event.");
                Require(restored.GetComponent<PlayerBuildRuntime>().EquipmentCount == equipmentCount, "Reconnect applied the unfinished Equipment.");
                if (phase == "target")
                {
                    while (!Has("choices-done-" + Other)) yield return null;
                    Require(resumed.IsSelecting && other.GetComponent<NetworkModifierSelection>().PendingUpgradeCount == 0,
                        "One player's target choice blocked the other player's progression.");
                }
                Mark("continue-" + phase);
                Debug.Log("[M4Process] event=reconnect-verified phase=" + phase);
            }
            while (!Has("choices-done-client") || !Has("choices-done-" + Other)) yield return null;
            var finalPlayers = Players();
            var ids = finalPlayers.SelectMany(p => p.GetComponent<PlayerBuildRuntime>().CaptureState().Weapons).Select(w => w.WeaponId).Distinct().OrderBy(i => i);
            Require(ids.SequenceEqual(new uint[] { 1, 2, 3, 6, 8, 402 }), "Selections did not exercise all six weapon definitions.");
            foreach (var player in finalPlayers)
            {
                var selection = player.GetComponent<NetworkModifierSelection>();
                Require(selection.Level == 18 && selection.PendingUpgradeCount == 0 && selection.BuildRevision == 18, "Reward applied zero or multiple times.");
                var snapshot = player.GetComponent<PlayerBuildRuntime>().CaptureState();
                Require(snapshot.Weapons.Length == 4 && snapshot.Weapons.Select(w => w.WeaponId).Distinct().Count() == 4, "Weapon slots duplicate or overflow.");
                Require(snapshot.Perks.Length == 6, "Perk increments were lost or duplicated.");
                File.WriteAllText(Path.Combine(directory, "server-build-" + player.netId + ".json"), JsonUtility.ToJson(snapshot, true));
                selection.ServerQueueUpgrades(); // Extra Equipment reward tests four targets without advancing Level.
                NetworkCombatWorld.Instance.Gateway.Ledger.SetAbsoluteInvulnerable(player.netId, true);
                player.GetComponent<CombatantBehaviour>().SetCanonicalInvulnerable(true);
                // Ovid's authored minimum detection radius is five. Keep living targets in its 5..15 range.
                var enemy = Instantiate(enemyPrefab, player.transform.position + Vector3.right * 7, Quaternion.identity);
                var agent = enemy.GetComponent<NetworkEnemySimulationAgent>();
                agent.ConfigureRuntimeMinimumHealthOverride(1000000);
                agent.ConfigureInitialServerTarget(player.netId);
                SceneManager.MoveGameObjectToScene(enemy, player.gameObject.scene);
                NetworkServer.Spawn(enemy);
            }
            Mark("builds-verified");
            Debug.Log("[M4Process] event=canonical-builds-verified weapons=6 level=18 rewards=17-per-player");
            while (!Has("extra-done-client") || !Has("extra-done-" + Other)) yield return null;
            foreach (var player in Players())
            {
                var selection = player.GetComponent<NetworkModifierSelection>();
                Require(selection.Level == 18 && selection.BuildRevision == 19 && selection.PendingUpgradeCount == 0,
                    "Extra Equipment reward advanced level or applied twice.");
                File.WriteAllText(Path.Combine(directory, "server-build-" + player.netId + ".json"), JsonUtility.ToJson(player.GetComponent<PlayerBuildRuntime>().CaptureState(), true));
            }
        }
        private IEnumerator ClientScenario()
        {
            yield return WaitOwner();
            Mark("ready-" + role, Owner.netId.ToString());
            while (!Has(role == "client" ? "debug-level-client-enabled" : "debug-level-other-enabled")) yield return null;
            Require(Selection.RequestDebugLevelUp(), "F5 owner request was unavailable in the development player.");
            while (Selection.Level < 2) yield return null;
            Require(Selection.Level == 2, "One F5 request advanced more than one level.");
            Mark("debug-level-" + role);
            while (!Has("bulk-xp-queued")) yield return null;
            while (View.Offers.Count == 0) yield return null;
            if (role == "client")
            {
                yield return Reconnect("reward");
                ulong old = Selection.LocalEventId;
                Submit(1, false);
                yield return WaitEvent(old);
                Require(View.Stage == UpgradeSelectionStage.EquipmentTarget && Build.EquipmentCount == 0, "First stage applied Equipment.");
                yield return Reconnect("target");
                var original = View.Offers[0].ContentId;
                old = Selection.LocalEventId;
                Require(View.Back().Succeeded, "Back intent failed.");
                yield return WaitEvent(old);
                Require(View.Stage == UpgradeSelectionStage.Reward && View.Offers.Any(o => o.ContentId == original), "Back lost original cards.");
            }
            int completed = 0;
            while (completed < 17)
            {
                while (View.Offers.Count == 0) yield return null;
                int earned = View.EarnedLevel;
                var kind = View.Offers[0].Kind;
                Require(earned == completed + 2, "Owner reward queue skipped/reordered a level.");
                Require(Owner.GetComponent<PlayerMovement>().IsUpgradeSelectionLocked && Time.timeScale == 1, "Owner lock or independent world time failed.");
                uint revision = Selection.OwnerBuildRevision;
                ulong old = Selection.LocalEventId;
                int option = PickIndex(kind, earned);
                var selected = View.Offers[option];
                if (capture && (kind != UpgradeRewardKind.Equipment || earned == 2)) yield return Capture("level-" + earned + "-" + kind);
                Submit(option, completed % 2 == 0);
                yield return WaitEvent(old);
                if (kind == UpgradeRewardKind.Equipment)
                {
                    Require(View.Stage == UpgradeSelectionStage.EquipmentTarget && Selection.OwnerBuildRevision == revision, "Equipment card choice consumed reward.");
                    Require(View.Offers.All(o => o.ContentId == selected.ContentId), "Target phase changed selected card.");
                    if (capture && (earned == 2 || View.Offers.Count == 4)) yield return Capture("targets-level-" + earned);
                    old = Selection.LocalEventId;
                    int target = role == "client" ? View.Offers.Count - 1 : 0;
                    Submit(target, completed % 2 != 0);
                    yield return WaitEvent(old);
                }
                Require(Selection.OwnerBuildRevision == revision + 1, "Host/Owner applied a reward more than once.");
                Require(!View.SelectOffer(selected.OfferId).Succeeded, "Old stage option remained usable.");
                completed++;
            }
            Require(!Owner.GetComponent<PlayerMovement>().IsUpgradeSelectionLocked && View.Offers.Count == 0, "Selection did not release the Owner.");
            Mark("choices-done-" + role);
            while (!Has("builds-verified")) yield return null;
            while (View.Offers.Count == 0) yield return null;
            ulong extraEvent = Selection.LocalEventId;
            Submit(role == "client" ? 0 : 1, false);
            yield return WaitEvent(extraEvent);
            Require(View.Stage == UpgradeSelectionStage.EquipmentTarget && View.Offers.Count == 4, "Extra Equipment must show all four legal targets.");
            if (capture) yield return Capture("four-targets");
            extraEvent = Selection.LocalEventId;
            Submit(3, role != "client");
            yield return WaitEvent(extraEvent);
            Require(Selection.Level == 18 && Selection.OwnerBuildRevision == 19, "Extra Equipment changed level or revision incorrectly.");
            Mark("extra-done-" + role);
            var baseline = JsonUtility.ToJson(Build.CaptureState());
            // Re-request a real authority baseline twice: occurrence-aware Perks must stay at six.
            Selection.OnStartAuthority(); Selection.OnStartAuthority();
            yield return new WaitForSecondsRealtime(.5f);
            Require(JsonUtility.ToJson(Build.CaptureState()) == baseline && Build.PerkCount == 6, "Repeated baseline duplicated Build entries.");
            if (capture) yield return Capture("build-complete");
            File.WriteAllText(Path.Combine(directory, "owner-build-" + role + ".json"), JsonUtility.ToJson(Build.CaptureState(), true));
            yield return VerifyCombat();
            Debug.Log("[M4Process] event=owner-build-verified role=" + role);
            Mark("owner-done-" + role);
        }
        private IEnumerator Reconnect(string phase)
        {
            string contents = Signature(View.Offers);
            var before = JsonUtility.ToJson(Build.CaptureState());
            ulong old = Selection.LocalEventId;
            Mark("pause-" + phase, Owner.netId.ToString());
            while (!Has("disconnect-" + phase)) yield return null;
            manager.StopClient();
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
            Require(FindObjectsByType<CardPickMenu>(FindObjectsSortMode.None).Length == 0, "Disconnected menu survived.");
            while (!Has("checkpoint-" + phase)) yield return null;
            manager.StartClient();
            yield return WaitOwner();
            while (View.Offers.Count == 0) yield return null;
            Require(Selection.LocalEventId != old && Signature(View.Offers) == contents, "Owner reconnect changed candidate content.");
            Require(JsonUtility.ToJson(Build.CaptureState()) == before, "Reconnect changed Build before final confirmation.");
            Mark("restored-" + phase, Owner.netId.ToString());
            while (!Has("continue-" + phase)) yield return null;
        }
        private int PickIndex(UpgradeRewardKind kind, int earned)
        {
            if (kind != UpgradeRewardKind.Weapon) return role == "client" ? Math.Min(1, View.Offers.Count - 1) : 0;
            uint wanted = role == "client" ? (earned == 4 ? 3u : earned == 12 ? 8u : 402u) : (earned == 4 ? 2u : earned == 12 ? 1u : 3u);
            for (int i = 0; i < View.Offers.Count; i++) if (View.Offers[i].ContentId == wanted) return i;
            throw new InvalidOperationException("Deterministic fixture did not issue weapon " + wanted);
        }
        private void Submit(int index, bool button)
        {
            if (button)
            {
                var menu = FindFirstObjectByType<CardPickMenu>();
                var control = menu.GetComponentsInChildren<Button>(true).Single(b => b.name == "Option" + index);
                Require(control.gameObject.activeSelf && control.interactable, "Issued button unavailable.");
                control.onClick.Invoke();
            }
            else Require(View.Select(index).Succeeded, "Numbered selection intent failed.");
            Require(!View.Select(index).Succeeded, "Duplicate submit escaped the pending gate.");
        }
        private IEnumerator WaitOwner()
        {
            while (Owner == null || !Selection.HasOwnerBaseline || !Build.IsBuildActive || !View.IsPresentationReady) yield return null;
        }
        private IEnumerator WaitEvent(ulong previous)
        {
            while (Selection.LocalEventId == previous || View.IsRequestPending) yield return null;
        }
        private IEnumerator VerifyCombat()
        {
            Build.NativeAttackStarted += OnAttack;
            try
            {
                gate.enabled = false;
                // Let Ovid mature before other weapons push the stationary targets outside its authored radius.
                // This is test ordering; the authored deadline and attack/query code remain intact.
                Build.SetWeaponExecutionEnabled(false);
                if (role == "client")
                {
                    var summon = (SummonAttackBehaviour)Build.GetWeaponAtSlot(3);
                    summon.enabled = true;
                    while (!attacked.Contains(402)) yield return null;
                    Mark("ovid-started");
                }
                else while (!Has("ovid-started")) yield return null;
                Build.SetWeaponExecutionEnabled(true);
                float dashAt = 0;
                float traceAt = 0;
                while (Build.CaptureState().Weapons.Any(w => !attacked.Contains(w.WeaponId)))
                {
                    if (role == "client" && Time.realtimeSinceStartup >= traceAt)
                    {
                        traceAt = Time.realtimeSinceStartup + 10;
                        var summon = Build.GetWeaponAtSlot(3) as SummonAttackBehaviour;
                        var targets = new List<SummonTarget>();
                        if (summon != null && summon.ActiveSummon != null)
                            summon.QueryTargets(summon.ActiveSummon.transform.position, 0, 10000, targets);
                        Debug.Log($"[M4Process] combat-wait owner={Owner.transform.position} summon={summon?.ActiveSummon?.transform.position} phase={summon?.ActiveSummon?.Phase} maturity={summon?.MaturityAt} now={NetworkTime.time} ready={summon?.IsAttackReady} targets={string.Join(",", targets.Select(t => t.Position))}");
                    }
                    if (role == "client" && !attacked.Contains(8) && Time.realtimeSinceStartup >= dashAt)
                    {
                        dashAt = Time.realtimeSinceStartup + 3;
                        Owner.GetComponent<PlayerMovement>().SetDirection(Vector2.up);
                        Owner.GetComponent<PlayerMovement>().Dash();
                    }
                    if (role == "client" && attacked.Contains(8)) Owner.GetComponent<PlayerMovement>().SetDirection(Vector2.zero);
                    yield return null;
                }
                Mark("attacks-" + role);
                while (!Has("attacks-client") || !Has("attacks-" + Other)) yield return null;
                var remote = NetworkClient.spawned.Values.Single(p => p != Owner && p.GetComponent<NetworkRunParticipant>() != null);
                var adapter = remote.GetComponent<NetworkWeaponCombatAdapter>();
                Debug.Log($"[M4Process] observer role={role} remote={remote.netId} projectile={adapter.ReplicaSpawnCount} melee={adapter.ReplicaMeleeSpawnCount} beam={adapter.ReplicaBeamSpawnCount} orbit={adapter.ReplicaOrbSpawnCount} trail={adapter.ReplicaTrailSpawnCount} summon={adapter.ReplicaSummonPoseCount} rejectTrail={adapter.RejectedTrailPresentationCount} rejectSummon={adapter.RejectedSummonPresentationCount}");
                bool observed = false;
                int dashDirection = -1;
                while (!Has("observer-client") || !Has("observer-" + Other))
                {
                    bool ready = role == "client" ? adapter.ReplicaSpawnCount > 0 && adapter.ReplicaMeleeSpawnCount > 0 &&
                        adapter.ReplicaBeamSpawnCount > 0 && adapter.ReplicaOrbSpawnCount > 0 :
                        adapter.ReplicaOrbSpawnCount > 0 && adapter.ReplicaTrailSpawnCount > 0 && adapter.ReplicaSummonPoseCount > 0;
                    if (ready && !observed) { observed = true; Mark("observer-" + role); }
                    // Keep exercising real dash input until both observers have verified their views.
                    // Each attempt is a new use; never replay an old use or relax the presentation age policy.
                    if (role == "client" && !Has("observer-" + Other) && Time.realtimeSinceStartup >= dashAt)
                    {
                        dashAt = Time.realtimeSinceStartup + 3;
                        dashDirection = -dashDirection;
                        Owner.GetComponent<PlayerMovement>().SetDirection(Vector2.up * dashDirection);
                        Owner.GetComponent<PlayerMovement>().Dash();
                    }
                    if (role == "client") Owner.GetComponent<PlayerMovement>().SetDirection(Vector2.zero);
                    yield return null;
                }
                Require(remote.GetComponent<ModifierSelectionController>().BoundBuild == null, "Observer received another owner's menu.");
                if (capture) yield return Capture("combat-six-weapons");
                Debug.Log("[M4Process] event=attacks-and-observer-verified role=" + role + " owned=" + string.Join(",", attacked));
            }
            finally { Build.NativeAttackStarted -= OnAttack; }
        }
        private void OnAttack(int slot, uint weapon, CombatEventId root)
        {
            attacked.Add(weapon);
            Debug.Log($"[M4Process] attack role={role} slot={slot} weapon={weapon} root={root.Value}");
        }
        private void ConfigureFixtureAudio()
        {
            // Same runtime-only Circling audio isolation as M3: the exported event bank is absent.
            // Keep all weapon stats, Physics2D and attack/presentation components unchanged.
            var db = FindFirstObjectByType<RuntimeDB>();
            assetCopies = new GameObject("M4 runtime audio copies");
            assetCopies.SetActive(false); DontDestroyOnLoad(assetCopies);
            var weapons = Instantiate(db.WeaponDB);
            weapons.Configure(weapons.Weapons.Select(definition =>
            {
                if (definition.ID != 6) return definition;
                var copy = Instantiate(definition);
                var emitter = Instantiate((CirclingAttackBehaviour)definition.WeaponPrefab, assetCopies.transform);
                emitter.attackPrefab = Instantiate(emitter.attackPrefab, assetCopies.transform);
                foreach (string name in new[] { "startSound", "loopSound", "endSound", "hitSound" })
                {
                    var field = typeof(AnimatedAttack).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                    field.SetValue(emitter.attackPrefab, Activator.CreateInstance(field.FieldType));
                }
                foreach (var component in emitter.attackPrefab.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component != null && component.GetType().Namespace == "FMODUnity") DestroyImmediate(component);
                copy.WeaponPrefab = emitter;
                return copy;
            }).ToArray());
            db.ConfigureWeaponDatabase(weapons);
        }
        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, role + "-" + name + ".png"));
            yield return null;
        }
        private static string Signature(IEnumerable<ModifierOffer> offers) => string.Join(";", offers.Select(o => $"{o.Kind}:{o.ContentId}:{o.LevelIndex}:{o.TargetSlotIndex}:{o.Rarity}:{o.PerkLevel}"));
        private static string Signature(IEnumerable<PlayerUpgradeOfferSnapshot> offers) => string.Join(";", offers.Select(o => $"{o.Kind}:{o.ContentId}:{o.LevelIndex}:{o.SlotIndex}:{o.Rarity}:{o.PerkLevel}"));
        private static NetworkIdentity[] Players() => NetworkServer.spawned.Values.Where(p => p != null && p.GetComponent<NetworkRunParticipant>() != null).ToArray();
        private bool Has(string name) => File.Exists(Path.Combine(directory, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(directory, name));
        private void Mark(string name, string value = "ready") => File.WriteAllText(Path.Combine(directory, name), value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void Finish(bool success)
        {
            if (finished) return;
            finished = true;
            SceneManager.sceneLoaded -= Prepare;
            Debug.Log("[M4Process] result=" + (success ? "PASS" : "FAIL") + " role=" + role);
            Application.Quit(success ? 0 : 1);
        }
        private sealed class FirstRandom : IRandomSource { public float Next01() => 0; }
    }
}
