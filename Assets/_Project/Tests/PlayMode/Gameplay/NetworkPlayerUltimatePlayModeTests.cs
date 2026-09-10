using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player;
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
using ReInput = global::Rewired.ReInput;
using KeyboardMap = global::Rewired.KeyboardMap;
using ActionElementMap = global::Rewired.ActionElementMap;
using InputActionType = global::Rewired.InputActionType;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class NetworkPlayerUltimatePlayModeTests
    {
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private readonly List<GameObject> objects = new List<GameObject>();
        private NetworkIdentity Player => NetworkClient.localPlayer;
        private NetworkPlayerUltimate Ultimate => Player.GetComponent<NetworkPlayerUltimate>();
        private PlayerMovement Movement => Player.GetComponent<PlayerMovement>();
        private PlayerBuildRuntime Build => Player.GetComponent<PlayerBuildRuntime>();
        private MirrorNetworkCombatBridge Bridge => Player.GetComponent<MirrorNetworkCombatBridge>();
        private NetworkModifierSelection Selection => Player.GetComponent<NetworkModifierSelection>();
        private uint AbilityId => UltimateNativeDefinitionAdapter.EncodeAbilityId(Ultimate.Definition.Id);
        private bool RootExists(ulong root) => NetworkCombatWorld.Instance.Gateway.Attacks.Contains(Player.netId, root, AbilityId);

        [UnityTest]
        public IEnumerator Boot_RewiredNormalMapIncludesAbilitiesAndPreservesMovementAndInteract()
        {
            yield return StartHostFixture();
            Assert.That(ReInput.isReady, Is.True);
            var map = ReInput.players.GetPlayer(0).controllers.maps.GetMap<KeyboardMap>(0, 1, 0);
            Assert.That(map, Is.Not.Null);
            Assert.That(map.enabled, Is.True);
            AssertAction(14, "R_Trigger", KeyCode.LeftShift, KeyCode.RightShift);
            AssertAction(4, "Button2", KeyCode.Q);
            AssertAction(51, "DebugAction_2", KeyCode.F6);
            AssertKeys(1, KeyCode.A, KeyCode.D);
            AssertKeys(3, KeyCode.W, KeyCode.S);
            AssertAction(0, "Button1", KeyCode.E);

            void AssertAction(int id, string name, params KeyCode[] keys)
            {
                var action = ReInput.mapping.GetAction(id);
                Assert.That(action.name, Is.EqualTo(name));
                Assert.That(action.type, Is.EqualTo(InputActionType.Button));
                Assert.That(action.categoryId, Is.EqualTo(1));
                AssertKeys(id, keys);
            }
            void AssertKeys(int id, params KeyCode[] keys)
            {
                var maps = new List<ActionElementMap>();
                map.GetElementMapsWithAction(id, maps);
                Assert.That(maps.Select(m => (int)m.keyboardKeyCode), Is.EquivalentTo(keys.Select(k => (int)k)));
            }
        }

        [UnityTest]
        public IEnumerator DebugCharge_HostRequestsUseRealCommandGrantOnceAndKeepActiveDeadline()
        {
            yield return StartHostFixture();
            uint initial = Field<NetworkUltimateState>(Ultimate, "state").Revision;
            Assert.That(Movement.RequestDebugUltimateCharge(), Is.True);
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False, "No speculative Host grant before the Command is processed.");
            Assert.That(Movement.RequestDebugUltimateCharge(), Is.True);
            PumpServerMessages();
            Assert.That(Ultimate.HasCharge, Is.True);
            Assert.That(Field<NetworkUltimateState>(Ultimate, "state").Revision, Is.EqualTo(initial + 1));
            Assert.That(Ultimate.RequestUse(), Is.True);
            yield return WaitFor(() => Ultimate.OwnerAttack.IsNativeActive, "The real Ultimate must consume the debug-granted resource.");
            var spent = Ultimate.CaptureServerState();
            Assert.That(Movement.RequestDebugUltimateCharge(), Is.True);
            PumpServerMessages();
            var refilled = Ultimate.CaptureServerState();
            Assert.That(refilled.HasCharge, Is.True);
            Assert.That(refilled.ActiveUntil, Is.EqualTo(spent.ActiveUntil));
            Assert.That(refilled.InvulnerableUntil, Is.EqualTo(spent.InvulnerableUntil));
            Assert.That(Ultimate.RequestUse(), Is.False);
            Assert.That(Ultimate.AcceptedUseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DebugCharge_ServerRejectsSelectingDeadDisabledAndNonCurrentAvatarEvenIfClientSends()
        {
            yield return StartHostFixture();
            var world = NetworkCombatWorld.Instance;
            world.SetPlayerUpgradeSelectionState(Player.netId, true);
            AssertRejected("selecting");
            world.SetPlayerUpgradeSelectionState(Player.netId, false);
            var alive = world.Gateway.Ledger.CaptureEntityState(Player.netId);
            var dead = alive.State;
            dead.Health = 0; dead.Alive = false;
            world.Gateway.Ledger.RestoreEntityState(Player.netId, new ServerEntityCheckpoint(dead, false));
            AssertRejected("dead");
            world.Gateway.Ledger.RestoreEntityState(Player.netId, alive);
            Ultimate.enabled = false;
            AssertRejected("server-not-ready");
            Ultimate.enabled = true;
            var connection = Player.connectionToClient;
            var avatar = connection.identity;
            try
            {
                typeof(NetworkConnection).GetProperty("identity").SetValue(connection, null);
                AssertRejected("not-current-owner");
            }
            finally { typeof(NetworkConnection).GetProperty("identity").SetValue(connection, avatar); }
            LogAssert.Expect(LogType.Log, $"[UltimateDebug] player={Player.netId} result=rejected reason=not-current-owner");
            // Mirror normally rejects this before dispatch; also exercise the receiver's explicit sender check.
            typeof(NetworkPlayerUltimate).GetMethods(Private).Single(m => m.Name.StartsWith("UserCode_CmdDebugUltimateCharge"))
                .Invoke(Ultimate, new object[] { new NetworkConnectionToClient(99) { isReady = true } });
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False);
            Assert.That(Movement.RequestDebugUltimateCharge(), Is.True);
            PumpServerMessages();
            Assert.That(Ultimate.HasCharge, Is.True, "Rejected intents must not poison later valid requests.");

            void AssertRejected(string reason)
            {
                uint revision = Field<NetworkUltimateState>(Ultimate, "state").Revision;
                LogAssert.Expect(LogType.Log, $"[UltimateDebug] player={Player.netId} result=rejected reason={reason}");
                SendDebugCharge();
                PumpServerMessages();
                Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False);
                Assert.That(Field<NetworkUltimateState>(Ultimate, "state").Revision, Is.EqualTo(revision));
            }
        }

        [UnityTest]
        public IEnumerator DebugCharge_OwnerGatesAndBindingReleaseDoNotLeaveAnOldRequestDelegate()
        {
            yield return StartHostFixture();
            Movement.enabled = false;
            Assert.That(Movement.RequestDebugUltimateCharge(), Is.False);
            Assert.That(Ultimate.RequestDebugUltimateCharge(), Is.False);
            Movement.enabled = true;
            typeof(NetworkPlayerUltimate).GetField("hasBaseline", Private).SetValue(Ultimate, false);
            Assert.That(Ultimate.RequestDebugUltimateCharge(), Is.False);
            typeof(NetworkPlayerUltimate).GetField("hasBaseline", Private).SetValue(Ultimate, true);
            Selection.ServerQueueUpgrades(1);
            yield return WaitFor(() => Movement.IsUpgradeSelectionLocked, "The actual selection lock must reach the Owner.");
            Assert.That(Movement.RequestDebugUltimateCharge(), Is.False);
            Assert.That(Ultimate.RequestDebugUltimateCharge(), Is.False);
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False);
            Ultimate.enabled = false;
            Assert.That(Field<Func<bool>>(Movement, "_requestDebugUltimateCharge"), Is.Null);
            Ultimate.enabled = true;
            Assert.That(Field<Func<bool>>(Movement, "_requestDebugUltimateCharge"), Is.Not.Null);
            Ultimate.OnStopAuthority();
            Assert.That(Field<Func<bool>>(Movement, "_requestDebugUltimateCharge"), Is.Null);
            Ultimate.OnStartAuthority();
            Assert.That(Field<Func<bool>>(Movement, "_requestDebugUltimateCharge"), Is.Not.Null);
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Stop must release the old avatar.");
            Assert.That(NetworkClient.localPlayer, Is.Null);
        }

        private void SendDebugCharge() => typeof(NetworkPlayerUltimate).GetMethod("CmdDebugUltimateCharge", Private)
            .Invoke(Ultimate, new object[] { null });

        [UnityTest]
        public IEnumerator Host_OneServerChargeUsesTheAdmittedIdentityAndExistingGasUntilBothWavesComplete()
        {
            yield return StartHostFixture();
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False);
            Assert.That(Ultimate.HasCharge, Is.False);
            Assert.That(Ultimate.RequestUse(), Is.False, "Entering Gameplay does not grant a free Ultimate.");
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            Assert.That(Ultimate.ServerGrantCharge(), Is.False, "Only one held charge is available.");
            Assert.That(Ultimate.HasCharge, Is.True);
            Assert.That(Field<PlayerUltimateRuntime>(Ultimate, "serverRuntime"),
                Is.Not.SameAs(Field<PlayerUltimateRuntime>(Ultimate, "ownerView")), "Host uses separate authority and owner state instances.");
            int handCount = Build.WeaponCount;
            Assert.That(Ultimate.RequestUse(), Is.True);
            ulong requested = Field<ulong>(Ultimate, "pendingUseId");
            Assert.That(requested, Is.Not.Zero);
            Assert.That(Ultimate.RequestUse(), Is.False, "The same input cannot submit twice while awaiting permission.");
            yield return WaitFor(() => Ultimate.AcceptedUseCount == 1 && Ultimate.OwnerAttack != null && Ultimate.OwnerAttack.IsNativeActive,
                "The reliable permission must start the original Dante attack on its owned GAS runtime.");
            DanteUltimateAttack attack = Ultimate.OwnerAttack;
            AttackSnapshot snapshot = Field<AttackSnapshot>(attack, "activeSnapshot");
            Assert.That(attack.ActiveUseId, Is.EqualTo(requested));
            Assert.That(snapshot.Context.EventId.Value, Is.EqualTo(requested));
            Assert.That(snapshot.Context.AbilityId, Is.EqualTo(AbilityId));
            Assert.That(RootExists(requested), Is.True);
            Assert.That(attack.NativeRuntime.SourcePlayerId, Is.EqualTo(Player.netId));
            Assert.That(attack.NativeRuntime.SourceEntityId, Is.EqualTo(Player.netId));
            Assert.That(attack.NativeRuntime.ModifierCount, Is.EqualTo(1), "The source intrinsic burn uses the existing GAS modifier container.");
            Assert.That(attack.NativeRuntime.GlobalMultipliers, Is.SameAs(Build.PerkMultipliers));
            Assert.That(Build.WeaponCount, Is.EqualTo(handCount), "The Ultimate is not an extra Hand weapon slot.");
            Assert.That(Ultimate.HasCharge || Ultimate.CaptureServerState().HasCharge, Is.False);

            const uint targetId = 0x7ffd0101;
            var target = Create("Ultimate canonical GAS target").AddComponent<CombatantBehaviour>();
            target.Initialize(10000);
            target.ConfigureEntityId(targetId);
            NetworkCombatWorld.Instance.Gateway.Ledger.RegisterEntity(targetId, 10000,
                CombatEntityKind.Enemy, CombatEntityAuthority.ServerCanonical);
            Bridge.enabled = false; // Completion, not a periodic flush, must deliver pending outcomes before retirement.
            CombatResolution result = attack.NativeRuntime.ResolveHitDetailed(snapshot, target);
            Assert.That(result.DamageContext.RootEventId.Value, Is.EqualTo(requested));
            Assert.That(result.ResolvedDamage.Value, Is.GreaterThan(0));
            Assert.That(Bridge.Collector.PendingResultCount, Is.GreaterThan(0));
            yield return WaitFor(() => !attack.IsNativeActive && !RootExists(requested),
                "Both source waves must finish, then all GAS outcomes must flush before the admitted root is retired.");
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(Bridge.Collector.PendingResultCount, Is.Zero);
            Assert.That(NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(targetId, out var canonical), Is.True);
            Assert.That(canonical.Health, Is.LessThanOrEqualTo(10000 - result.ResolvedDamage.Value));
            Assert.That(Ultimate.AcceptedUseCount, Is.EqualTo(1));
            Assert.That(Ultimate.RejectedUseCount, Is.Zero);
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False);
            Assert.That(Build.WeaponCount, Is.EqualTo(handCount));
        }

        [UnityTest]
        public IEnumerator Host_UniformApiRejectsDisabledMovementAndCommandsRejectStaleBuildDuplicateAndActiveRoot()
        {
            yield return StartHostFixture();
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            Movement.enabled = false;
            Assert.That(Ultimate.RequestUse(), Is.False, "Future UI calls must share the movement execution gate.");
            Assert.That(Field<ulong>(Ultimate, "pendingUseId"), Is.Zero);
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.True);
            Movement.enabled = true;
            SendUse(Bridge.EventIds.Next().Value, Selection.OwnerBuildRevision + 1u);
            yield return WaitFor(() => Ultimate.RejectedUseCount == 1, "A stale Build must be rejected over the woven Command.");
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.True);
            Assert.That(Ultimate.RequestUse(), Is.True);
            yield return WaitFor(() => Ultimate.AcceptedUseCount == 1 && Ultimate.OwnerAttack.IsNativeActive, "One valid use must be admitted.");
            ulong root = Ultimate.OwnerAttack.ActiveUseId;
            PlayerUltimateSnapshot consumed = Ultimate.CaptureServerState();
            SendUse(root, Selection.OwnerBuildRevision);
            yield return WaitFor(() => Ultimate.RejectedUseCount == 2, "A duplicate ID cannot consume or replay an Ultimate.");
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            Assert.That(Ultimate.RequestUse(), Is.False, "A newly granted charge cannot overlap the owner's active wave root.");
            SendUse(Bridge.EventIds.Next().Value, Selection.OwnerBuildRevision);
            yield return WaitFor(() => Ultimate.RejectedUseCount == 3, "A fresh ID cannot bypass the server's active root gate.");
            var held = Ultimate.CaptureServerState();
            Assert.That(held.HasCharge, Is.True);
            Assert.That(held.ActiveUntil, Is.EqualTo(consumed.ActiveUntil));
            Assert.That(held.InvulnerableUntil, Is.EqualTo(consumed.InvulnerableUntil));
            Assert.That(RootExists(root), Is.True);
            Assert.That(Ultimate.OwnerAttack.Cancel(root), Is.True);
            yield return WaitFor(() => !RootExists(root), "Cancellation must retire only its admitted root.");
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.True, "Cancellation neither spends nor refunds the independently held charge.");
        }

        [UnityTest]
        public IEnumerator Host_DisablingBeforeAcceptanceKeepsChargeAndAfterAcceptanceRetiresTheOrphanAcknowledgement()
        {
            yield return StartHostFixture();
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            Assert.That(Ultimate.RequestUse(), Is.True);
            Ultimate.enabled = false;
            PumpServerMessages();
            Assert.That(Ultimate.AcceptedUseCount, Is.Zero);
            Assert.That(Ultimate.RejectedUseCount, Is.EqualTo(1));
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.True);
            yield return null;
            Ultimate.enabled = true;
            yield return WaitFor(() => Ultimate.HasCharge, "Rebinding must retain the server's unspent charge.");
            Assert.That(Ultimate.RequestUse(), Is.True);
            ulong orphan = Field<ulong>(Ultimate, "pendingUseId");
            PumpServerMessages();
            Assert.That(Ultimate.AcceptedUseCount, Is.EqualTo(1));
            Assert.That(RootExists(orphan), Is.True);
            Assert.That(Ultimate.OwnerAttack.IsNativeActive, Is.False, "Only the server queue has run; the permission ACK is still pending.");
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False);
            Ultimate.enabled = false;
            Assert.That(Field<ulong>(Ultimate, "pendingUseId"), Is.Zero);
            Assert.That(Ultimate.OwnerAttack, Is.Null);
            Assert.That(NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(Player.netId, out var disabled), Is.True);
            Assert.That(disabled.AbsoluteInvulnerable, Is.False);
            yield return WaitFor(() => !RootExists(orphan), "A late accepted ACK must retire its orphan root despite a cleared pending input.");
            Assert.That(Field<ulong>(Ultimate, "serverRootId"), Is.Zero);
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False, "The accepted use stays spent after cancellation.");
            yield return WaitFor(() => NetworkTime.time > Ultimate.CaptureServerState().InvulnerableUntil + 0.05d,
                "The protection deadline must pass while the coordinator is disabled.");
            NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(Player.netId, out var expired);
            Assert.That(expired.AbsoluteInvulnerable, Is.False, "Disabling Update cannot leave a permanent Ledger protection reason.");
        }

        [UnityTest]
        public IEnumerator Host_DisablingAnActiveUseReleasesGasButPreservesIndependentUpgradeProtection()
        {
            yield return StartHostFixture();
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            Assert.That(Ultimate.RequestUse(), Is.True);
            yield return WaitFor(() => Ultimate.OwnerAttack != null && Ultimate.OwnerAttack.IsNativeActive, "The real attack must start before cancellation.");
            var attack = Ultimate.OwnerAttack;
            ulong root = attack.ActiveUseId;
            var snapshot = Field<AttackSnapshot>(attack, "activeSnapshot");
            var intrinsic = attack.NativeRuntime.RuntimeModifiers;
            var global = Build.PerkMultipliers;
            int handCount = Build.WeaponCount;
            global.damage = 0.25f;
            NetworkCombatWorld.Instance.SetPlayerUpgradeSelectionState(Player.netId, true);
            Ultimate.enabled = false;
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(Ultimate.OwnerAttack, Is.Null);
            Assert.That(intrinsic.Count, Is.Zero);
            Assert.That(Build.PerkMultipliers, Is.SameAs(global));
            Assert.That(global.damage, Is.EqualTo(0.25f), "Disposing this ability cannot clear its player's external perk container.");
            Assert.That(Build.WeaponCount, Is.EqualTo(handCount));
            NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(Player.netId, out var choosing);
            Assert.That(choosing.AbsoluteInvulnerable, Is.True);
            Assert.That(NetworkCombatWorld.Instance.Gateway.Ledger.IsPlayerSelectingUpgrade(Player.netId), Is.True);
            NetworkCombatWorld.Instance.SetPlayerUpgradeSelectionState(Player.netId, false);
            NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(Player.netId, out var cleared);
            Assert.That(cleared.AbsoluteInvulnerable, Is.False, "Only the independent Upgrade reason remained after ability disable.");
            yield return WaitFor(() => !RootExists(root), "The termination and collector flush must finish even while the coordinator is disabled.");
            Assert.That(Field<NetworkUltimatePresentationSpawn?>(Ultimate, "serverPresentation"), Is.Null);
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False);
        }

        [UnityTest]
        public IEnumerator Host_ServerCancellationStopsTheOwnerThroughRpcWithoutLocalComponentDisposal()
        {
            yield return StartHostFixture();
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            Assert.That(Ultimate.RequestUse(), Is.True);
            yield return WaitFor(() => Ultimate.OwnerAttack != null && Ultimate.OwnerAttack.IsNativeActive &&
                Field<NetworkUltimatePresentationSpawn?>(Ultimate, "serverPresentation").HasValue,
                "The owner and server must both have the real admitted use before server cancellation.");
            DanteUltimateAttack attack = Ultimate.OwnerAttack;
            ulong root = attack.ActiveUseId;
            var snapshot = Field<AttackSnapshot>(attack, "activeSnapshot");
            PlayerUltimateSnapshot spent = Ultimate.CaptureServerState();
            // Exercise the server half while keeping the Host owner component and GAS instance enabled.
            // The process fixture separately toggles the actual dedicated-server component.
            typeof(NetworkPlayerUltimate).GetMethod("CancelServerExecution", Private).Invoke(Ultimate, new object[] { true });
            Assert.That(Ultimate.enabled, Is.True);
            Assert.That(Ultimate.OwnerAttack, Is.SameAs(attack));
            Assert.That(attack.IsNativeActive, Is.True, "A queued TargetRpc, rather than shared-instance disposal, must stop the Owner.");
            Assert.That(RootExists(root), Is.False, "Authority must retire immediately, before the client cancellation arrives.");
            Assert.That(Field<ulong>(Ultimate, "serverRootId"), Is.Zero);
            Assert.That(Field<NetworkUltimatePresentationSpawn?>(Ultimate, "serverPresentation"), Is.Null);
            yield return WaitFor(() => !attack.IsNativeActive && Field<bool>(Ultimate, "ownerServerExecutionSuspended"),
                "The reliable server execution state must cancel the independently live owned runtime.");
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(Field<ulong>(Ultimate, "pendingUseId"), Is.Zero);
            yield return WaitFor(() => !Player.GetComponent<CombatantBehaviour>().IsInvulnerable,
                "Neither the Owner deadline tick nor canonical replication may reinstate disabled Ultimate protection.");
            Assert.That(Ultimate.CaptureServerState().ActiveUntil, Is.EqualTo(spent.ActiveUntil));
            Assert.That(Ultimate.CaptureServerState().InvulnerableUntil, Is.EqualTo(spent.InvulnerableUntil));
            Assert.That(Ultimate.CaptureServerState().HasCharge, Is.False);
            Assert.That(Ultimate.RequestUse(), Is.False);
            typeof(NetworkPlayerUltimate).GetMethod("OnEnable", Private).Invoke(Ultimate, null);
            yield return WaitFor(() => !Field<bool>(Ultimate, "ownerServerExecutionSuspended"), "Server resume must restore only execution permission.");
            Assert.That(attack.IsNativeActive, Is.False);
            Assert.That(RootExists(root), Is.False);
            Assert.That(Ultimate.CaptureServerState().ActiveUntil, Is.EqualTo(spent.ActiveUntil));
            Assert.That(Ultimate.RequestUse(), Is.False);
        }

        [UnityTest]
        public IEnumerator Host_ReenableDoesNotReplaceNewerOwnerStateWithALaggingSyncVarBaseline()
        {
            yield return StartHostFixture();
            NetworkUltimateState oldBaseline = Field<NetworkUltimateState>(Ultimate, "state");
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            NetworkUltimateState newest = Field<NetworkUltimateState>(Ultimate, "state");
            Assert.That(Ultimate.HasCharge, Is.True);
            // Host normally shares the newest SyncVar. Reproduce the remote-client window where
            // an RPC has delivered a newer owner state and its SyncVar baseline is still in flight.
            typeof(NetworkPlayerUltimate).GetField("state", Private).SetValue(Ultimate, oldBaseline);
            try
            {
                Ultimate.enabled = false;
                Ultimate.enabled = true;
                Assert.That(Field<uint>(Ultimate, "lastOwnerStateRevision"), Is.EqualTo(newest.Revision));
                Assert.That(Ultimate.HasCharge, Is.True, "OnEnable may rebind input but must not reset revision ordering.");
            }
            finally { typeof(NetworkPlayerUltimate).GetField("state", Private).SetValue(Ultimate, newest); }
            yield return null;
            yield return null;
            Assert.That(Ultimate.HasCharge, Is.True, "Queued older cancellation/resume snapshots cannot overwrite the latest resource state.");
            Assert.That(Field<uint>(Ultimate, "lastOwnerStateRevision"), Is.EqualTo(newest.Revision));
        }

        [UnityTest]
        public IEnumerator Host_OlderAcceptedAckCannotOverwriteANewerChargeGrantOrRestartTheActiveRoot()
        {
            yield return StartHostFixture();
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            Assert.That(Ultimate.RequestUse(), Is.True);
            ulong root = Field<ulong>(Ultimate, "pendingUseId");
            PumpServerMessages();
            Assert.That(Ultimate.AcceptedUseCount, Is.EqualTo(1));
            NetworkUltimateState ackState = Field<NetworkUltimateState>(Ultimate, "state");
            Assert.That(ackState.Snapshot.HasCharge, Is.False);
            Assert.That(Ultimate.OwnerAttack.IsNativeActive, Is.False);
            Assert.That(Ultimate.ServerGrantCharge(), Is.True);
            NetworkUltimateState granted = Field<NetworkUltimateState>(Ultimate, "state");
            Assert.That(granted.Revision, Is.GreaterThan(ackState.Revision));
            Assert.That(Ultimate.HasCharge, Is.True, "Host immediately observes the newer server grant.");
            yield return WaitFor(() => Ultimate.OwnerAttack.IsNativeActive, "The queued earlier ACK must still start its already-admitted use.");
            Assert.That(Ultimate.OwnerAttack.ActiveUseId, Is.EqualTo(root));
            Assert.That(Ultimate.HasCharge, Is.True, "The old accepted ACK cannot overwrite the newer held charge.");
            Assert.That(Field<uint>(Ultimate, "lastOwnerStateRevision"), Is.EqualTo(granted.Revision));
            // A repeated reliable result travels through the actual TargetRpc serializer too.
            typeof(NetworkPlayerUltimate).GetMethod("TargetUseResult", Private).Invoke(Ultimate,
                new object[] { Player.connectionToClient, root, true, ackState });
            yield return null;
            yield return null;
            Assert.That(Ultimate.HasCharge, Is.True);
            Assert.That(Ultimate.OwnerAttack.ActiveUseId, Is.EqualTo(root));
            Assert.That(RootExists(root), Is.True, "A duplicate ACK for the current active use must not retire that root as an orphan.");
            Assert.That(Ultimate.RequestUse(), Is.False);
            Assert.That(Ultimate.AcceptedUseCount, Is.EqualTo(1));
        }

        private void SendUse(ulong useId, uint revision) => typeof(NetworkPlayerUltimate).GetMethod("CmdUseUltimate", Private)
            .Invoke(Ultimate, new object[] { useId, revision, null });
        private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Private).GetValue(value);
        private static void PumpServerMessages()
        {
            // Keep the real Mirror Host transport, serializer, sender and Command wrapper.
            // Advance only its server-bound queue so the client ACK remains pending until the next frame.
            Assert.That(NetworkServer.localConnection, Is.Not.Null);
            typeof(LocalConnectionToClient).GetMethod("Update", Private).Invoke(NetworkServer.localConnection, null);
        }
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
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7953, false, out string error), Is.True, error);
            Create("Ultimate protocol fixture gate").AddComponent<WeaponAttackAdmissionFixtureGate>();
            SceneManager.sceneLoaded += PrepareGameplay;
            manager.StartHost();
            yield return WaitFor(() => manager.IsGameplayLoaded && Player != null && Selection.HasOwnerBaseline &&
                Ultimate != null && Ultimate.Definition != null && Field<bool>(Ultimate, "hasBaseline") &&
                Ultimate.OwnerAttack != null,
                "Boot must spawn the configured Ultimate component, authoritative resource and owned GAS services.");
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
