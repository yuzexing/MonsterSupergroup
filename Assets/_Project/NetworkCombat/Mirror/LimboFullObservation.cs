using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Explicit full-flow technical inputs and passive evidence. No changes to enemy data or production RNG.
    public sealed class LimboFullObservation : MonoBehaviour
    {
        public static string Case => LimboReferenceLaunch.Argument("--limbo-full-case=") ?? "idle";
        public static string FixtureRulesName => Case switch { "barrier-first" => "FullBarrierFirst", "burst-first" => "FullBurstFirst", "slime-b" => "FullSlimeBurst", _ => "FullFixture" };
        private StreamWriter log;
        private NetworkGameplayEnemySpawner spawner;
        private string run, lastState;
        private float nextCard, nextFrame, holdUntil;
        private bool technical, fixture, completed, disconnected, deathRequested;
        private float SelectionHoldSeconds => fixture && Case == "cancel" ? float.PositiveInfinity : Case == "reconnect" ? 18 : 8;
        private double requestObserved;
        private AstralShift.HellMaiden.Data.Cards.EquipmentDB fixtureEquipment;
        private AstralShift.HellMaiden.Data.Cards.EquipmentData[] originalEquipment;
        private readonly HashSet<string> pictures = new();
        private void Start()
        {
            fixture = LimboReferenceLaunch.Profile == "full-fixture";
            technical = fixture || LimboReferenceLaunch.Profile == "full-validation";
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log = new StreamWriter(Path.Combine(LimboReferenceLaunch.OutputDirectory, "full.jsonl")) { AutoFlush = true };
            Write("configuration", technical ? "TECHNICAL: optional normal auto-walk, first legal card, heal below 300 except downed case. Fixtures disable weapon execution and may queue real rewards; never pressure evidence." : "PLAYABLE: observation only.");
        }
        private void Update()
        {
            var world = NetworkCombatWorld.Instance;
            if (world == null) return;
            var snapshot = world.GetComponent<NetworkWaveProgress>().Snapshot;
            if (string.IsNullOrEmpty(snapshot.RunId)) return;
            if (run != snapshot.RunId)
            {
                if (spawner != null) spawner.ReferenceTransitionRequested -= Requested;
                run = snapshot.RunId; completed = disconnected = deathRequested = false; requestObserved = 0; lastState = null;
                holdUntil = 0; pictures.Clear(); spawner = FindFirstObjectByType<NetworkGameplayEnemySpawner>();
                if (NetworkServer.active && spawner != null) spawner.ReferenceTransitionRequested += Requested;
                Write("round", run);
            }
            var local = NetworkClient.localPlayer;
            if (snapshot.TransitionRequestedAt > 0 && requestObserved == 0)
            {
                requestObserved = Time.realtimeSinceStartupAsDouble;
                if (technical && (Case == "busy" || Case == "chain" || Case == "disconnect" || Case == "reconnect" || fixture && Case == "cancel"))
                    holdUntil = Time.realtimeSinceStartup + SelectionHoldSeconds;
                Write("request-observed", "Existing combat continues; selection hold is explicit test input.", snapshot); Capture("request");
            }
            if (local != null && technical && !BootGameplayNetworkManager.CombatHasEnded)
            {
                var player = local.GetComponent<PlayerMovement>();
                if (Case != "downed" && local.GetComponent<CombatantBehaviour>().CurrentHealth < 300)
                { player.IncreaseHealth(500); Write("test-heal", "IncreaseHealth 500; maximum and source values unchanged."); }
                if (fixture) local.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(false);
                var selection = local.GetComponent<ModifierSelectionController>();
                if (selection != null && selection.Offers.Count > 0 && selection.IsPresentationReady && !selection.IsRequestPending &&
                    Time.realtimeSinceStartup >= nextCard && Time.realtimeSinceStartup >= holdUntil)
                {
                    Write("test-card", "index=0 offer=" + selection.Offers[0].OfferId);
                    selection.Select(0); nextCard = Time.realtimeSinceStartup + .15f;
                }
                if (fixture && Case == "downed" && snapshot.Phase == WavePhase.Running && snapshot.StageEndTime > 0 && snapshot.Elapsed >= snapshot.StageEndTime - .3 && !deathRequested)
                { deathRequested = true; Write("test-downed", "Explicit owner health change through existing PlayerMovement; not natural death evidence."); player.DecreaseHealth(10000); }
                if (fixture && !NetworkServer.active && requestObserved > 0 && !disconnected &&
                    (Case == "disconnect" || Case == "reconnect") && Time.realtimeSinceStartupAsDouble - requestObserved > 2)
                { disconnected = true; StartCoroutine(Reconnect(Case == "reconnect")); }
            }
            string token = snapshot.Phase + "/" + snapshot.TransitionWaitCount + "/" + snapshot.TransitionWaitReason;
            if (token != lastState || Time.realtimeSinceStartup >= nextFrame)
            {
                lastState = token; nextFrame = Time.realtimeSinceStartup + 1;
                var selection = local != null ? local.GetComponent<NetworkModifierSelection>() : null;
                Write("frame", token, new Frame { snapshot = snapshot, selecting = selection != null && selection.IsSelecting,
                    eventId = selection != null ? selection.PendingEventId : 0, pending = selection != null ? selection.PendingUpgradeCount : 0,
                    health = local != null ? local.GetComponent<CombatantBehaviour>().CurrentHealth : -1, timeScale = Time.timeScale });
            }
            foreach (int at in fixture ? new[] { 10, 20, 25, 35 } : new[] { 599, 605, 621, 635, 640, 655, 660, 676, 696, 715, 720 })
                if (snapshot.Elapsed >= at) Capture("time" + at);
            if (BootGameplayNetworkManager.CombatHasEnded && !completed)
            {
                RestoreEquipment();
                completed = true; Capture("end");
                Write("completed", "Existing run-end lifecycle", snapshot);
            }
        }
        private void Requested()
        {
            if (!technical || !(Case == "busy" || Case == "chain" || Case == "disconnect" || Case == "reconnect" || Case == "unavailable" || fixture && Case == "cancel")) return;
            holdUntil = Time.realtimeSinceStartup + SelectionHoldSeconds;
            if (Case == "cancel") Write("test-exit-hold", "Selection remains open until the operator uses the existing exit UI.");
            foreach (var connection in NetworkServer.connections.Values)
            {
                if (connection.identity == null) continue;
                // Reconnect keeps the Host busy too, so the departed Client can rejoin before completion.
                if (NetworkServer.connections.Count > 1 && connection.identity.isLocalPlayer && Case != "reconnect") continue;
                if (Case == "unavailable" && fixtureEquipment == null)
                {
                    fixtureEquipment = connection.identity.GetComponent<PlayerBuildRuntime>().BuildDatabase.EquipmentDB;
                    originalEquipment = fixtureEquipment.Equipments; fixtureEquipment.Equipments = Array.Empty<AstralShift.HellMaiden.Data.Cards.EquipmentData>();
                    Write("test-empty-candidates", "Temporary in-memory equipment offers only; source files untouched. Retained reward must not block transition.");
                }
                connection.identity.GetComponent<NetworkModifierSelection>().ServerQueueUpgrades(Case == "chain" || Case == "reconnect" ? 2 : 1);
                var selection = connection.identity.GetComponent<NetworkModifierSelection>();
                Write("test-queue", "player=" + connection.identity.netId + " rewards=" + selection.PendingUpgradeCount + " event=" + selection.PendingEventId + " selecting=" + selection.IsSelecting);
            }
        }
        private IEnumerator Reconnect(bool reconnect)
        {
            var manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            Write("test-disconnect", reconnect ? "Original member reconnect follows" : "Disconnected member must not block Host");
            manager.StopClient();
            if (!reconnect) yield break;
            yield return new WaitForSecondsRealtime(4);
            while (manager.IsGameplayTransitioning) yield return null;
            manager.StartClient(); Write("test-reconnect", "Same process/member, existing authentication and restoration");
        }
        private void Capture(string name)
        {
            if (!pictures.Add(name)) return;
            StartCoroutine(CaptureFrame(Path.Combine(LimboReferenceLaunch.OutputDirectory, "full-" + run + "-" + name + ".png")));
        }
        private IEnumerator CaptureFrame(string path)
        {
            // Other observers request screenshots on the same milestone. Capture this frame directly
            // so Unity's single pending file capture cannot silently replace the transition evidence.
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Destroy(texture); }
        }
        private void Write(string kind, string detail, object payload = null) => log?.WriteLine(JsonUtility.ToJson(new Row {
            kind = kind, detail = detail, run = run, realtime = Time.realtimeSinceStartupAsDouble,
            combat = EnemySimulationClock.CombatNow, payload = payload == null ? null : JsonUtility.ToJson(payload) }));
        private void RestoreEquipment() { if (fixtureEquipment != null) fixtureEquipment.Equipments = originalEquipment; fixtureEquipment = null; originalEquipment = null; }
        private void OnDestroy() { RestoreEquipment(); if (spawner != null) spawner.ReferenceTransitionRequested -= Requested; log?.Dispose(); }
        [Serializable] private class Row { public string kind, detail, run, payload; public double realtime, combat; }
        [Serializable] private class Frame { public WaveProgressSnapshot snapshot; public bool selecting; public ulong eventId; public int pending, health; public float timeScale; }
    }
}
