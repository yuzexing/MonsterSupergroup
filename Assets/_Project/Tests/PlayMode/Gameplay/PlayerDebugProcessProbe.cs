using System;
using System.Collections;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    /// <summary>Opt-in Host + two clients validation; omitted from normal builds with the test assembly.</summary>
    public sealed class PlayerDebugProcessProbe : MonoBehaviour
    {
        private string role, directory;
        private bool host, finished;
        private float deadline;
        private BootGameplayNetworkManager manager;
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private NetworkPlayerDebugPanel Panel => FindFirstObjectByType<NetworkPlayerDebugPanel>();
        private PlayerDebugSnapshot OwnRow => Panel?.Rows.FirstOrDefault(r => r.IsSelf);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string role = Argument("--player-debug-role=");
            if (role == null) return;
            var probe = new GameObject("Player Debug validation").AddComponent<PlayerDebugProcessProbe>();
            probe.role = role; probe.host = role == "host";
            probe.directory = Argument("--player-debug-artifacts=");
            DontDestroyOnLoad(probe.gameObject);
            probe.gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
        }

        private IEnumerator Start()
        {
            deadline = Time.realtimeSinceStartup + 180;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            SceneManager.sceneLoaded += PrepareGameplay;
            var run = Run();
            while (true)
            {
                object next;
                try { if (!run.MoveNext()) break; next = run.Current; }
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
                yield return next;
            }
            Finish(true);
        }

        private void Update()
        {
            if (!finished && deadline > 0 && Time.realtimeSinceStartup > deadline)
            { Debug.LogError("[PlayerDebugProcess] timeout role=" + role); Finish(false); }
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Boot manager missing");
            ushort port = ushort.Parse(Argument("--player-debug-port=") ?? "7988");
            Require(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", port, false, out string error), error);
            if (host) manager.StartHost(); else manager.StartClient();
            while (Owner == null || !Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline || OwnRow == null) yield return null;
            while (!Owner.GetComponent<NetworkModifierSelection>().TryReadDebugState(false, out _)) yield return null;
            int maximum = role == "host" ? 150 : role == "client" ? 200 : 250;
            int health = maximum - 30;
            var combatant = Owner.GetComponent<CombatantBehaviour>();
            combatant.SetMaximumHealthPreservingMissingHealth(maximum); combatant.RestoreHealth(maximum);
            combatant.ReceiveDamage(new DamageInfo(1, maximum - health, false));
            while (OwnRow?.Canonical?.Health != health) yield return null;
            File.WriteAllText(Path.Combine(directory, "participant-" + role), OwnRow.ParticipantId.ToString());
            Mark("ready-" + role);
            if (host)
            {
                while (!Seen("ready-client") || !Seen("ready-client2") || Panel.Rows.Count != 3 ||
                    Panel.Rows.Any(r => !r.Canonical.HasValue || r.Canonical.Value.Health != r.Canonical.Value.MaxHealth - 30)) yield return null;
                Require(Panel.Rows.All(r => r.Source == PlayerDebugSource.ServerRecord), "Host must use the server lane for every participant");
                Require(Panel.Rows.Select(r => r.ParticipantId).Distinct().Count() == 3, "Duplicate participant row");
                Panel.SelectParticipant(OwnRow.ParticipantId);
                Dump("roster"); Mark("topology");
            }
            else
            {
                while (!Seen("topology")) yield return null;
                Require(Panel.Rows.Count == 1 && OwnRow.Source == PlayerDebugSource.OwnerRuntime, "Client must display only itself");
                Require(Panel.SelectedParticipant == OwnRow.ParticipantId, "Client details should start expanded");
                Dump("roster");
            }
            yield return new WaitForSecondsRealtime(.5f);
            Mark("roster-seen-" + role);
            if (host)
            {
                while (!Seen("roster-seen-client") || !Seen("roster-seen-client2")) yield return null;
                foreach (var participant in manager.Session.Participants)
                {
                    var selection = NetworkServer.spawned[participant.AvatarId].GetComponent<NetworkModifierSelection>();
                    float amount = Enumerable.Range(1, 3).Sum(selection.ExperienceRequiredAtLevel) + 5;
                    Require(selection.TryGrantExperience(amount), "XP grant failed");
                }
                Mark("upgrades");
            }
            while (!Seen("upgrades") || OwnRow?.Progression?.PendingUpgradeCount != 3 || OwnRow.Level != 4) yield return null;
            Require(!Panel.AreDetailsVisible && Panel.Expanded, "Selection should hide details, preserving the expanded preference");
            Dump("selection"); Mark("upgrades-seen-" + role);
            if (host)
            {
                while (!Seen("upgrades-seen-client") || !Seen("upgrades-seen-client2")) yield return null;
                foreach (var participant in manager.Session.Participants)
                {
                    var avatar = NetworkServer.spawned[participant.AvatarId];
                    var selection = avatar.GetComponent<NetworkModifierSelection>();
                    int attempts = 0;
                    while (selection.PendingUpgradeCount > 0 && attempts++ < 12)
                        Require(selection.ServerSelect(avatar.connectionToClient, selection.PendingEventId, 0, out error), error);
                    Require(selection.PendingUpgradeCount == 0, "Upgrade queue was not drained");
                    Require(avatar.GetComponent<NetworkPlayerUltimate>().ServerGrantCharge(), "Ultimate charge failed");
                }
                Mark("built");
            }
            while (!Seen("built") || OwnRow?.Progression?.PendingUpgradeCount != 0 || !Panel.AreDetailsVisible || OwnRow.Ultimate?.HasCharge != true) yield return null;
            Require(OwnRow.Experience == 5, "XP remainder lost");
            Require(OwnRow.Details.Contains("Equipment:") && OwnRow.Details.Contains("GAS runtime:"), "Build details missing");
            Dump("build");
            var ultimate = Owner.GetComponent<NetworkPlayerUltimate>();
            Require(ultimate.RequestUse(), "Ultimate request failed");
            while (OwnRow?.Ultimate == null || PlayerDebugSnapshotReader.UltimatePhase(OwnRow.Ultimate.Value, NetworkTime.time) != "Active") yield return null;
            Dump("ultimate");
            while (!ultimate.TryReadDebugState(false, out var ultimateData) || NetworkTime.time < ultimateData.State.ActiveUntil + .3) yield return null;
            var dash = Owner.GetComponent<NetworkPlayerDash>();
            while (!dash.HasOwnerBaseline) yield return null;
            int charges = dash.OwnerRuntime.AvailableCharges;
            Owner.GetComponent<PlayerMovement>().SetDirection(Vector2.right);
            Owner.GetComponent<PlayerMovement>().Dash();
            while (dash.OwnerRuntime.AvailableCharges == charges) yield return null;
            while (OwnRow == null || !OwnRow.Details.Contains("Dash:")) yield return null;
            Panel.SetExpanded(false); Panel.SetExpanded(true);
            Dump("dash");
            Mark("skills-" + role);
            if (host)
            {
                while (!Seen("skills-client") || !Seen("skills-client2")) yield return null;
                Mark("down-client");
                ulong downedParticipant = ulong.Parse(File.ReadAllText(Path.Combine(directory, "participant-client")));
                while (!Panel.Rows.Any(r => r.ParticipantId == downedParticipant && r.Canonical.HasValue && !r.Canonical.Value.Alive)) yield return null;
                Dump("downed"); Mark("down-seen");
                while (!Panel.Rows.Any(r => r.Source == PlayerDebugSource.OfflineCheckpoint)) yield return null;
                var savedRow = Panel.Rows.Single(r => r.Source == PlayerDebugSource.OfflineCheckpoint);
                ulong participantId = savedRow.ParticipantId; uint oldId = savedRow.AvatarId;
                Panel.SelectParticipant(participantId);
                string frozen = savedRow.Details;
                Dump("offline");
                yield return new WaitForSecondsRealtime(.5f);
                Require(Panel.Rows.Single(r => r.ParticipantId == participantId).Details == frozen, "Offline countdown advanced");
                Mark("offline-seen");
                while (!Seen("rejoined") || !Panel.Rows.Any(r => r.ParticipantId == participantId && r.Source == PlayerDebugSource.ServerRecord && r.AvatarId != oldId)) yield return null;
                Require(Panel.Rows.Count == 3, "Reconnect duplicated participant");
                Require(Panel.Rows.Single(r => r.ParticipantId == participantId).Canonical?.Alive == false, "Downed checkpoint was not restored");
                Dump("rejoined"); Mark("complete");
                while (!Seen("done-client") || !Seen("done-client2")) yield return null;
                manager.StopHost();
            }
            else if (role == "client")
            {
                while (!Seen("down-client")) yield return null;
                combatant.ReceiveDamage(new DamageInfo(1, combatant.MaxHealth, false));
                while (!Seen("down-seen")) yield return null;
                ulong participantId = OwnRow.ParticipantId; uint avatarId = Owner.netId;
                uint revision = OwnRow.Progression.Value.BuildRevision;
                manager.StopClient();
                while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
                Require(Panel == null, "Player Debug leaked after scene unload");
                while (!Seen("offline-seen")) yield return null;
                manager.StartClient();
                while (OwnRow?.Progression == null || !OwnRow.Canonical.HasValue) yield return null;
                Require(OwnRow.ParticipantId == participantId && Owner.netId != avatarId, "Incorrect participant/avatar after reconnect");
                Require(OwnRow.Canonical.Value.Health == 0 && !OwnRow.Canonical.Value.Alive, "Downed state not restored");
                Require(OwnRow.Progression.Value.BuildRevision == revision && OwnRow.Level == 4 && OwnRow.Experience == 5, "Progression was not restored");
                Dump("rejoined"); Mark("rejoined");
                while (!Seen("complete")) yield return null;
                manager.StopClient();
            }
            else { while (!Seen("complete")) yield return null; manager.StopClient(); }
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning || NetworkClient.active) yield return null;
            Require(Panel == null, "Player Debug leaked at shutdown");
            Mark("done-" + role);
        }

        private void Dump(string name)
        {
            string text = string.Join("\n\n", Panel.Rows.Select(r => r.Summary + "\n" + r.Details));
            File.WriteAllText(Path.Combine(directory, role + "-" + name + ".txt"), text);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, role + "-" + name + ".png"));
            Debug.Log($"[PlayerDebugProcess] role={role} stage={name} rows={Panel.Rows.Count} development={Debug.isDebugBuild}");
        }
        private void Mark(string name) => File.WriteAllText(Path.Combine(directory, name), "ready");
        private bool Seen(string name) => File.Exists(Path.Combine(directory, name));
        private static string Argument(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix))?.Substring(prefix.Length);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)) spawner.enabled = false;
        }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true; SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log($"[PlayerDebugProcess] result={(passed ? "PASS" : "FAIL")} role={role}");
            Application.Quit(passed ? 0 : 1);
        }
    }
}
