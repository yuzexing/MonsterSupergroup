#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class EnemyDefinitionProcessProbe : MonoBehaviour
    {
        private string role, output;
        private BootGameplayNetworkManager manager;
        private EnemyDefinitionRuntimeFixture fixture;
        private GameObject gate;
        private bool finished;
        private float deadline, nextDiagnostic;
        private bool Host => role == "host";
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private static string Arg(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string role = Arg("--enemy-definition-role=");
            if (role == null) return;
            var probe = new GameObject("Enemy definition network verification").AddComponent<EnemyDefinitionProcessProbe>();
            probe.role = role; probe.output = Path.GetFullPath(Arg("--enemy-definition-output=") ?? "Logs/EnemyDefinitions/Process");
            Directory.CreateDirectory(probe.output); DontDestroyOnLoad(probe.gameObject);
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            deadline = Time.realtimeSinceStartup + 210;
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                object next;
                try { var routine = stack.Peek(); if (!routine.MoveNext()) { stack.Pop(); continue; }
                    next = routine.Current; if (next is IEnumerator nested) { stack.Push(nested); continue; } }
                catch (Exception error) { Debug.LogException(error); Finish(false, error.Message); yield break; }
                yield return next;
            }
            Finish(true, "pair birth/damage/reset/death/reconnect/restart");
        }
        private void Update()
        {
            if (!finished && deadline > 0 && Time.realtimeSinceStartup >= deadline) Finish(false, "Process deadline");
            if (finished || manager == null || Time.realtimeSinceStartup < nextDiagnostic) return;
            nextDiagnostic = Time.realtimeSinceStartup + 5;
            string agents = string.Join(";", FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(a => {
                var e = a.GetComponent<AstralShift.HellMaiden.AI.Enemy.EnemyController>();
                return $"net={a.netId} def={a.Birth.DefinitionId} init={a.ProductEnemyInitialized} client={a.isClient} role={a.Authority?.Role} target={e?.Target?.name} appearance={e?.enemyAnimator?.PaletteSwapper?.Appearance?.name}";
            }));
            Debug.Log($"[EnemyDefinitionDiagnostic] {role} phase={manager.RoomSnapshot.Phase} clientCount={NetworkClient.spawned.Count} owner={Owner?.netId} agents=[{agents}]");
        }
        private IEnumerator Run()
        {
            yield return Wait(() => NetworkManager.singleton is BootGameplayNetworkManager, "Boot");
            manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            manager.ConfigurePreparationFlow(true); fixture = new EnemyDefinitionRuntimeFixture(manager);
            gate = new GameObject("Definition probe automatic-attack gate"); gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            yield return manager.EnsureMainMenu();
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            Require(backend.TryPrepareKcp("127.0.0.1", ushort.Parse(Arg("--enemy-definition-port=") ?? "8039"), false, out string error), error);
            if (Host) { NetworkServer.listen = true; manager.StartHost(); Mark("host-listening"); }
            else { yield return Wait(() => Seen("host-listening"), "host socket"); manager.StartClient(); }
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing && manager.RoomSnapshot.SelfId != 0, "admission");
            manager.SetOwnLoadout(6);
            yield return Wait(() => manager.RoomSnapshot.Members.Single(m => m.ParticipantId == manager.RoomSnapshot.SelfId).WeaponId == 6, "loadout");
            manager.SetOwnReady(true);
            if (Host)
            { yield return Wait(() => manager.RoomSnapshot.Members.Length == 2 && manager.RoomSnapshot.Members.All(m => m.Ready), "two ready members"); manager.StartPreparedGame(); }
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame && fixture.PairReady(), "two definition instances");
            yield return null; foreach (var agent in fixture.Agents()) fixture.Verify(agent, true);
            uint low = fixture.Agents().Single(a => a.Birth.DefinitionId == fixture.Definitions[0].Id).netId;
            uint high = fixture.Agents().Single(a => a.Birth.DefinitionId == fixture.Definitions[1].Id).netId;
            string run = manager.RoomSnapshot.RunId; ulong member = manager.RoomSnapshot.SelfId;
            Mark("birth-" + role, low + "," + high); yield return Wait(() => Seen("birth-host") && Seen("birth-client"), "both birth checks");
            Require(Read("birth-host") == Read("birth-client"), "Peers observed different entity IDs.");
            if (Host) { yield return Wait(() => fixture.PlaceInView(), "visible actor placement"); EnemyDefinitionRuntimeFixture.Damage(high, 7); Mark("damage-sent"); }
            yield return Wait(() => Seen("damage-sent") && Health(high) == 53, "same canonical HP"); Mark("damaged-" + role);
            if (Host)
            {
                yield return Wait(() => Seen("damaged-client"), "client damage check");
                NetworkCombatWorld.Instance.ResetReferenceEnemy(Agent(high)); Mark("reset-sent");
            }
            yield return Wait(() => Seen("reset-sent") && Agent(high).ReferenceResetVersion > 0 && Health(high) == 60, "reference reset on both peers");
            yield return null; fixture.Verify(Agent(high), true); Mark("reset-" + role);
            if (Host)
            { yield return Wait(() => Seen("reset-client"), "client reset check"); EnemyDefinitionRuntimeFixture.Damage(low, 20); Mark("death-sent"); }
            yield return Wait(() => Seen("death-sent") && !NetworkClient.spawned.ContainsKey(low), "death presentation cleanup");
            fixture.Verify(Agent(high), true); Mark("survivor-" + role);
            yield return Wait(() => Seen("survivor-host") && Seen("survivor-client"), "both survivor checks");
            if (!Host)
            {
                manager.StopClient();
                yield return Wait(() => !NetworkClient.active && !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "client disconnect cleanup");
                Mark("client-offline"); yield return Wait(() => Seen("resume-allowed"), "host checkpoint");
                manager.StartClient();
                yield return Wait(() => Owner != null && manager.RoomSnapshot.Phase == PreparationPhase.InGame &&
                    Agent(high) != null && Agent(high).ProductEnemyInitialized && Agent(high).GetComponent<AstralShift.HellMaiden.AI.Enemy.EnemyController>().enemyAnimator.PaletteSwapper.Appearance != null, "reconnect definition baseline");
                yield return null; fixture.Verify(Agent(high), true);
                Require(manager.RoomSnapshot.SelfId == member && manager.RoomSnapshot.RunId == run, "Reconnect changed participant/run identity.");
                Require(!NetworkClient.spawned.ContainsKey(low), "Dead enemy returned on reconnect."); Mark("client-rejoined");
            }
            else
            {
                yield return Wait(() => Seen("client-offline") && manager.Session.Participants.Any(p => p.ConnectionState == RunConnectionState.Disconnected), "server disconnect checkpoint");
                fixture.Verify(Agent(high), true); Mark("resume-allowed");
                yield return Wait(() => Seen("client-rejoined"), "client resumed");
            }
            Mark("reconnect-" + role);
            yield return Wait(() => Seen("reconnect-host") && Seen("reconnect-client"), "reconnect barrier");
            yield return Capture("reconnected");
            Owner.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.GameOver, "two players downed");
            Mark("ended-" + role);
            if (Host) { yield return Wait(() => Seen("ended-client"), "client end screen"); manager.ChooseRunEndAction(RunEndAction.Restart); }
            yield return Wait(() => manager.RoomSnapshot.RunId != run && manager.RoomSnapshot.Phase == PreparationPhase.InGame && fixture.PairReady(), "new round");
            yield return null;
            foreach (var agent in fixture.Agents()) { Require(agent.netId != low && agent.netId != high, "Old enemy ID reused in next round."); fixture.Verify(agent, true); }
            Mark("restarted-" + role, manager.RoomSnapshot.RunId);
            yield return Wait(() => Seen("restarted-host") && Seen("restarted-client"), "restart barrier");
            Require(Read("restarted-host") == Read("restarted-client"), "Peers restarted into different runs.");
            yield return Capture("restarted"); Mark("captured-" + role);
            if (Host) { yield return Wait(() => Seen("captured-client"), "client screenshot"); manager.LeavePreparationRoom(); }
            yield return Wait(() => !NetworkClient.active && !NetworkServer.active && !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "final cleanup");
            fixture.Dispose(); fixture = null; Destroy(gate);
        }
        private NetworkEnemySimulationAgent Agent(uint id) => NetworkClient.spawned.TryGetValue(id, out var value) ? value.GetComponent<NetworkEnemySimulationAgent>() : null;
        private int Health(uint id) => Agent(id) != null ? Agent(id).GetComponent<CombatantBehaviour>().CurrentHealth : -1;
        private IEnumerator Capture(string name)
        {
            if (Application.isBatchMode) yield break;
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, role + "-" + name + ".png"), texture.EncodeToPNG()); Destroy(texture);
        }
        private static IEnumerator Wait(Func<bool> condition, string stage) => EnemyDefinitionRuntimeFixture.Wait(condition, stage);
        private static void Require(bool condition, string reason) => EnemyDefinitionRuntimeFixture.Require(condition, reason);
        private bool Seen(string name) => File.Exists(Path.Combine(output, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(output, name));
        private void Mark(string name, string value = "ok") { File.WriteAllText(Path.Combine(output, name), value); Debug.Log("[EnemyDefinitionProcess] " + role + " " + name + " " + value); }
        private void Finish(bool success, string detail)
        {
            if (finished) return; finished = true;
            File.WriteAllText(Path.Combine(output, role + "-result.json"), JsonUtility.ToJson(new Result { passed = success, role = role, detail = detail }, true));
            Debug.Log("[EnemyDefinitionProcess] " + role + " " + (success ? "PASS" : "FAIL") + " " + detail);
            Application.Quit(success ? 0 : 1);
        }
        private void OnDestroy() => fixture?.Dispose();
        [Serializable] private class Result { public bool passed; public string role, detail; }
    }
}
#endif
