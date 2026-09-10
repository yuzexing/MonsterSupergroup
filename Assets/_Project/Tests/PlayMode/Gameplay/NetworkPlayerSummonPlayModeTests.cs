using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class NetworkPlayerSummonPlayModeTests
    {
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private readonly List<GameObject> objects = new List<GameObject>();
        private NetworkIdentity Player => NetworkClient.localPlayer;
        private PlayerBuildRuntime Build => Player.GetComponent<PlayerBuildRuntime>();
        private NetworkWeaponCombatAdapter Adapter => Player.GetComponent<NetworkWeaponCombatAdapter>();
        private NetworkModifierSelection Selection => Player.GetComponent<NetworkModifierSelection>();
        private MirrorNetworkCombatBridge Bridge => Player.GetComponent<MirrorNetworkCombatBridge>();
        private SummonPresentationHistory ServerHistory => Field<SummonPresentationHistory>(Adapter, "serverSummonHistory");

        [UnityTest]
        public IEnumerator Host_FreshEquipmentReceivesSixtySecondDeadlineAndCannotSkipCocoonOrBirth()
        {
            yield return StartHostFixture();
            double equippedAt = NetworkTime.time;
            SummonAttackBehaviour weapon = Equip(1);
            var saved = Adapter.CaptureSummonMaturities().Single();
            Assert.That(saved.MaturityAt, Is.EqualTo(equippedAt + weapon.InitialMaturityDelay).Within(0.15d));
            SendBaseline();
            yield return WaitFor(() => weapon.HasSimulationBinding, "The real Build TargetRpc must configure the owned pet clock.");
            Assert.That(weapon.MaturityAt, Is.EqualTo(saved.MaturityAt));
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.Cocoon));
            Assert.That(weapon.CurrentSnapshot, Is.Null);
            yield return WaitFor(() => ServerHistory.PetCount == 1, "Cocoon presentation must reach the server without an attack root.");
            Assert.That(NetworkCombatWorld.Instance.Gateway.Attacks.ActiveCount(Player.netId), Is.Zero);

            int rejected = Adapter.RejectedAttackCount;
            SendAttack(1, Bridge.EventIds.Next());
            yield return WaitFor(() => Adapter.RejectedAttackCount == rejected + 1, "An owned valid identity still cannot skip maturation.");
            Assert.That(Adapter.LastAttackRejection, Is.EqualTo(CombatRejectionReason.InvalidAttackRate));
            var forged = ServerHistory.CaptureStates().Single();
            forged.State.PhaseSequence++;
            forged.State.Phase = SummonPhase.Birth;
            forged.EventNetworkTime = NetworkTime.time;
            int rejectedViews = Adapter.RejectedSummonPresentationCount;
            SendState(forged);
            yield return WaitFor(() => Adapter.RejectedSummonPresentationCount == rejectedViews + 1,
                "A reliable phase message cannot skip the server's source Cocoon deadline.");
            Assert.That(ServerHistory.CaptureStates().Single().State.Phase, Is.EqualTo(SummonPhase.Cocoon));
            Assert.That(Adapter.AcceptedCooldownReportCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Host_RestoredBirthIsSoughtBeforeCooldownAndCheckpointPreparesANewAvatarWithoutRebasingTime()
        {
            yield return StartHostFixture();
            yield return WaitFor(() => NetworkTime.time >= 1.1d, "A nonnegative past maturity is required for this restoration fixture.");
            SummonAttackBehaviour weapon = Equip(1);
            double maturity = NetworkTime.time - 1d;
            SetCanonicalMaturity(1, maturity);
            Adapter.CaptureCooldowns();
            double ready = NetworkTime.time + 4d;
            Field<PlayerWeaponCooldownSnapshot[]>(Adapter, "serverCooldowns")[1] = new PlayerWeaponCooldownSnapshot
            {
                SlotIndex = 1, WeaponId = 402, LastAttackEventId = Bridge.EventIds.Next().Value,
                ServerReceivedAt = NetworkTime.time, ReadyAt = ready, CooldownSeconds = weapon.GetCooldown(), SequenceSeconds = 2.6333334f
            };
            SendBaseline();
            yield return WaitFor(() => weapon.HasSimulationBinding, "The restored Build must configure the clock before restoring its cooldown.");
            weapon.TickNative(0f, 0f);
            Assert.That(weapon.ActiveSummon.Phase, Is.EqualTo(SummonPhase.Birth));
            Assert.That(weapon.ActiveSummon.PhaseElapsedSeconds, Is.EqualTo(NetworkTime.time - maturity).Within(0.05d));
            Assert.That(weapon.ActiveSummon.IsPresentation, Is.False, "This is the real Owner's restored Native pet.");
            ParticleSystem birthBurst = weapon.ActiveSummon.transform.Find("Iso Rotation/Self Rotation/Power_Burst_V3")
                .GetComponent<ParticleSystem>();
            AudioSource birthSound = weapon.ActiveSummon.transform.Find("Iso Rotation/Self Rotation/Power_Burst_V3/OrbitalBeamSmallYellow")
                .GetComponent<AudioSource>();
            Assert.That(birthBurst.time, Is.EqualTo(NetworkTime.time - maturity).Within(.05d),
                "Restoring partway through Birth must seek the real particle burst as well as its animation.");
            Assert.That(birthBurst.isPaused, Is.False);
            Assert.That(birthSound.isPlaying, Is.False, "The historical Birth sound must not restart on reconnect.");
            Assert.That(weapon.CurrentSnapshot, Is.Null);
            Assert.That(weapon.GetCooldown() - weapon.LastAttackElapsedTime,
                Is.EqualTo(ready - NetworkTime.time).Within(0.15d), "ConfigureSimulation cannot overwrite the attack checkpoint's remaining cooldown.");
            Assert.That(weapon.IsAttackReady, Is.False);
            int rejects = Adapter.RejectedAttackCount;
            SendAttack(1, Bridge.EventIds.Next());
            yield return WaitFor(() => Adapter.RejectedAttackCount == rejects + 1, "A saved cooldown cannot bypass the remaining Birth animation.");

            var capture = typeof(BootGameplayNetworkManager).GetMethod("CapturePlayer", BindingFlags.Static | BindingFlags.NonPublic);
            var checkpoint = (PlayerRuntimeCheckpoint)capture.Invoke(null, new object[] { Player });
            Assert.That(checkpoint, Is.Not.Null);
            Assert.That(checkpoint.SummonMaturities.Single().MaturityAt, Is.EqualTo(maturity));
            Assert.That(checkpoint.WeaponCooldowns.Single().ReadyAt, Is.EqualTo(ready));
            yield return new WaitForSeconds(0.2f);
            GameObject restored = Object.Instantiate(manager.playerPrefab);
            objects.Add(restored);
            restored.SetActive(false);
            restored.GetComponent<NetworkModifierSelection>().PrepareServerRestore(checkpoint.Build, checkpoint.Progression);
            var restoredAdapter = restored.GetComponent<NetworkWeaponCombatAdapter>();
            restoredAdapter.PrepareServerRestore(checkpoint.WeaponCooldowns);
            restoredAdapter.PrepareServerSummonRestore(checkpoint.SummonMaturities);
            // The real reconnect coordinator next supplies an authenticated connection before
            // AddPlayerForConnection. An unowned Spawn would violate OwnerFinal ledger authority.
            // This fixture verifies preparation only; process tests cover the socket reconnection.
            Assert.That(restored.GetComponent<NetworkIdentity>().netId, Is.Zero);
            Assert.That(Field<PlayerSummonMaturitySnapshot[]>(restoredAdapter, "serverSummonMaturities")[1].MaturityAt, Is.EqualTo(maturity));
            Assert.That(Field<PlayerWeaponCooldownSnapshot[]>(restoredAdapter, "serverCooldowns")[1].ReadyAt, Is.EqualTo(ready));
            Assert.That(restored.GetComponent<PlayerBuildRuntime>().IsBuildActive, Is.False);
            Assert.That(restored.GetComponentsInChildren<SummonAIBehaviour>(true), Is.Empty,
                "Preparing absolute deadlines must not create presentation or simulation bodies.");
            checkpoint.SummonMaturities[0].MaturityAt += 100d;
            checkpoint.WeaponCooldowns[0].ReadyAt += 100d;
            Assert.That(Field<PlayerSummonMaturitySnapshot[]>(restoredAdapter, "serverSummonMaturities")[1].MaturityAt, Is.EqualTo(maturity));
            Assert.That(Field<PlayerWeaponCooldownSnapshot[]>(restoredAdapter, "serverCooldowns")[1].ReadyAt, Is.EqualTo(ready));
        }

        [UnityTest]
        public IEnumerator Host_AdmittedRootIsSlotAndEpochBoundAndAdapterDisableCancelsBeforeRebind()
        {
            yield return StartHostFixture();
            yield return WaitFor(() => NetworkTime.time >= 9d, "The real 3.8 second Birth and five second initial cooldown must have elapsed.");
            SummonAttackBehaviour weapon = Equip(1);
            SummonAttackBehaviour other = Equip(2);
            SetCanonicalMaturity(1, 0d); SetCanonicalMaturity(2, 0d);
            SendBaseline();
            yield return WaitFor(() => weapon.HasSimulationBinding && other.HasSimulationBinding, "Both same-ID slots need independent baselines.");
            GameObject target = Create("Summon protocol target");
            target.transform.position = weapon.OwnerPlayer.transform.position + Vector3.right * 6f;
            BindFixtureTarget(weapon, target);
            yield return TickUntil(weapon, () => weapon.CurrentSnapshot != null, "Source target selection must start a native root.");
            AttackSnapshot root = weapon.CurrentSnapshot;
            ulong rootId = root.Context.EventId.Value;
            yield return WaitFor(() => NetworkCombatWorld.Instance.Gateway.Attacks.Contains(Player.netId, rootId, 402) &&
                ServerHistory.CaptureStates().Any(state => state.State.AttackEventId == rootId),
                "The actual GAS start and reliable pet edge must reach server admission in order.");

            var crossSlot = ServerHistory.CaptureStates().Single(state => state.State.PetId == weapon.PetId);
            crossSlot.SlotIndex = 2;
            crossSlot.State.PetId = Bridge.EventIds.Next().Value;
            crossSlot.State.PhaseSequence = 1;
            crossSlot.EventNetworkTime = NetworkTime.time;
            int badViews = Adapter.RejectedSummonPresentationCount;
            SendState(crossSlot);
            yield return WaitFor(() => Adapter.RejectedSummonPresentationCount == badViews + 1,
                "The same weapon ID in another slot cannot reuse an admitted attack root.");
            CombatEventId allocated = Bridge.EventIds.Next();
            ushort wrongEpoch = allocated.ConnectionEpoch == ushort.MaxValue ? (ushort)1 : (ushort)(allocated.ConnectionEpoch + 1);
            int badRoots = Adapter.RejectedAttackCount;
            SendAttack(2, CombatEventId.Compose(allocated.SourceSlot, wrongEpoch, allocated.Sequence));
            yield return WaitFor(() => Adapter.RejectedAttackCount == badRoots + 1, "A previous connection epoch cannot admit a fresh summon root.");
            Assert.That(Adapter.LastAttackRejection, Is.EqualTo(CombatRejectionReason.InvalidSequence));

            double cancelledAt = NetworkTime.time;
            Adapter.enabled = false;
            Assert.That(weapon.HasSimulationBinding || other.HasSimulationBinding, Is.False);
            Assert.That(weapon.ActiveSummon, Is.Null);
            Assert.That(root.IsDisposed, Is.True);
            yield return WaitFor(() => ServerHistory.PetCount == 0 &&
                !NetworkCombatWorld.Instance.Gateway.Attacks.Contains(Player.netId, rootId, 402),
                "The disabled adapter must still send termination before PBR retires the root.");
            var cooldown = Adapter.CaptureCooldowns().Single();
            Assert.That(cooldown.ReadyAt, Is.EqualTo(cancelledAt + weapon.GetCooldown()).Within(0.15d));
            Adapter.enabled = true;
            yield return WaitFor(() => weapon.HasSimulationBinding && other.HasSimulationBinding,
                "Re-enabling must request a revision-matched maturity and cooldown baseline.");
            Assert.That(weapon.MaturityAt, Is.Zero);
            Assert.That(weapon.GetCooldown() - weapon.LastAttackElapsedTime,
                Is.EqualTo(cooldown.RemainingAt(NetworkTime.time)).Within(0.15d));
            Assert.That(weapon.CurrentSnapshot, Is.Null);
            Assert.That(Adapter.AcceptedCooldownReportCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Host_SourceTargetLossCompletesEnterAndExitAndStartsFullCooldownAtActualEnd()
        {
            yield return StartHostFixture();
            yield return WaitFor(() => NetworkTime.time >= 9d, "The source initial maturation and cooldown must already be complete.");
            SummonAttackBehaviour weapon = Equip(1);
            SetCanonicalMaturity(1, 0d);
            SendBaseline();
            yield return WaitFor(() => weapon.HasSimulationBinding, "The owned clock must bind before source simulation runs.");
            GameObject target = Create("Shortened summon attack target");
            target.transform.position = weapon.OwnerPlayer.transform.position + Vector3.right * 6f;
            BindFixtureTarget(weapon, target);
            yield return TickUntil(weapon, () => weapon.CurrentSnapshot != null, "A real Enter phase must create the retained root.");
            ulong root = weapon.CurrentSnapshot.Context.EventId.Value;
            yield return WaitFor(() => Adapter.AcceptedCooldownReportCount == 1, "Root admission must finish before cancellation of the target.");
            yield return WaitFor(() => ServerHistory.TryGetState(weapon.PetId, out var edge) &&
                edge.State.Phase == SummonPhase.AttackEnter, "The source Enter edge must exist before capturing an in-flight old pose.");
            ServerHistory.TryGetState(weapon.PetId, out var oldPhase);
            double completedAt = 0d;
            weapon.PresentationStateChanged += state =>
            { if (state.Phase == SummonPhase.Positioning) completedAt = NetworkTime.time; };
            target.SetActive(false);
            // Advance using real frame durations, never compressed synthetic seconds: the server
            // independently enforces the original one second Enter plus 0.6333334 second Exit.
            yield return TickUntil(weapon, () => completedAt > 0d && weapon.CurrentSnapshot == null,
                "Losing a non-boss target must end Main early, then finish the complete source Exit.");
            yield return WaitFor(() => !NetworkCombatWorld.Instance.Gateway.Attacks.Contains(Player.netId, root, 402),
                "Natural completion must publish its final phase before retiring the admitted root.");
            PlayerWeaponCooldownSnapshot cooldown = Adapter.CaptureCooldowns().Single();
            Assert.That(cooldown.SequenceSeconds, Is.GreaterThanOrEqualTo(1.6333333f));
            Assert.That(cooldown.SequenceSeconds, Is.LessThan(weapon.GetAttackSequenceDuration()));
            Assert.That(cooldown.ReadyAt, Is.EqualTo(completedAt + weapon.GetCooldown()).Within(0.15d));
            Assert.That(cooldown.CooldownSeconds, Is.EqualTo(5f).Within(0.001f));
            Assert.That(Adapter.RejectedSummonPresentationCount, Is.Zero);
            Assert.That(Adapter.RejectedAttackCount, Is.Zero);

            ServerHistory.TryGetState(weapon.PetId, out var completedState);
            var oldPose = new NetworkSummonPresentationPose
            {
                SourcePlayerId = Player.netId, EventNetworkTime = NetworkTime.time,
                Sample = new SummonPresentationPose
                {
                    WeaponId = 402, PetId = weapon.PetId, PhaseSequence = oldPhase.State.PhaseSequence,
                    PoseSequence = 900, Pose = oldPhase.State.Pose
                }
            };
            oldPose.Sample.Pose.Position += Vector3.right * 100f;
            int dropped = Adapter.DroppedSummonPoseCount;
            SendPoses(oldPose);
            yield return WaitFor(() => Adapter.DroppedSummonPoseCount == dropped + 1,
                "An unreliable pose crossing a reliable completion edge is a normal dropped sample.");
            Assert.That(Adapter.RejectedSummonPresentationCount, Is.Zero);
            ServerHistory.TryGetState(weapon.PetId, out var afterOldPose);
            Assert.That(afterOldPose.State.PhaseSequence, Is.EqualTo(completedState.State.PhaseSequence));
            Assert.That(afterOldPose.State.Pose.Position, Is.EqualTo(completedState.State.Pose.Position));
            oldPose.Sample.Pose.Position.x = float.NaN;
            SendPoses(oldPose);
            yield return WaitFor(() => Adapter.RejectedSummonPresentationCount == 1,
                "An invalid coordinate still follows protocol rejection, even when its phase is old.");
            Assert.That(Adapter.DroppedSummonPoseCount, Is.EqualTo(dropped + 1));
        }

        private SummonAttackBehaviour Equip(int slot)
        {
            Build.SetWeaponExecutionEnabled(false);
            return (SummonAttackBehaviour)Build.EquipWeaponAtSlot(slot, 402);
        }
        private void SetCanonicalMaturity(int slot, double time)
        {
            Adapter.CaptureSummonMaturities();
            Field<PlayerSummonMaturitySnapshot[]>(Adapter, "serverSummonMaturities")[slot] =
                new PlayerSummonMaturitySnapshot { SlotIndex = slot, WeaponId = 402, MaturityAt = time };
        }
        private void SendBaseline() => typeof(NetworkModifierSelection).GetMethod("SendOwnerState", Private).Invoke(Selection, null);
        private void SendAttack(int slot, CombatEventId id) => typeof(NetworkWeaponCombatAdapter)
            .GetMethod("CmdObserveWeaponAttack", Private).Invoke(Adapter,
                new object[] { slot, 402u, id.Value, NetworkTime.time, Selection.OwnerBuildRevision, null });
        private void SendState(NetworkSummonPresentationState state) => typeof(NetworkWeaponCombatAdapter)
            .GetMethod("CmdSubmitSummonState", Private).Invoke(Adapter, new object[] { state, null });
        private void SendPoses(params NetworkSummonPresentationPose[] poses) => typeof(NetworkWeaponCombatAdapter)
            .GetMethod("CmdSubmitSummonPoses", Private).Invoke(Adapter, new object[] { poses, null });
        private void BindFixtureTarget(SummonAttackBehaviour weapon, GameObject target) => weapon.ConfigureSimulation(
            () => NetworkTime.time, () => Bridge.EventIds.Next().Value,
            (position, minimum, maximum, targets) => { targets.Clear(); if (target.activeInHierarchy) targets.Add(new SummonTarget(target.transform, false)); },
            weapon.MaturityAt);
        private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
        private GameObject Create(string name) { var value = new GameObject(name); objects.Add(value); return value; }

        private IEnumerator StartHostFixture()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootPath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(BootPath, LoadSceneMode.Single);
#endif
            bootRoots = BootSceneFixtureObjects.Capture(BootPath);
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7952, false, out string error), Is.True, error);
            Create("Summon protocol fixture gate").AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += PrepareGameplay;
            manager.StartHost();
            yield return WaitFor(() => manager.IsGameplayLoaded && Player != null && Selection.HasOwnerBaseline,
                "Boot must create the owned Build and deliver its real authoritative baseline.");
        }
        private static void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/_Project/Scenes/Gameplay.unity") return;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)) spawner.enabled = false;
        }
        private static IEnumerator WaitFor(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }
        private static IEnumerator TickUntil(SummonAttackBehaviour weapon, Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                weapon.TickNative(Time.deltaTime, Time.smoothDeltaTime);
                yield return null;
            }
            Assert.That(condition(), Is.True, message);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= PrepareGameplay;
            if (manager != null)
            {
                if (NetworkServer.active && NetworkClient.active) manager.StopHost();
                else if (NetworkServer.active) manager.StopServer();
                else if (NetworkClient.active) manager.StopClient();
                yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Gameplay must unload.");
            }
            for (int index = objects.Count - 1; index >= 0; index--) if (objects[index] != null) Object.Destroy(objects[index]);
            objects.Clear();
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
