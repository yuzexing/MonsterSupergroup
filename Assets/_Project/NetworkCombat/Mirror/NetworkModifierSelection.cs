using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public struct UpgradeOptionMessage
    {
        public ulong OptionId;
        public uint EquipmentId;
        public int LevelIndex;
        public int SlotIndex;
    }

    /// <summary>Per-player authoritative offers. Only identifiers cross Mirror; Build owns all effects.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerBuildRuntime), typeof(ModifierSelectionController))]
    public sealed class NetworkModifierSelection : NetworkBehaviour
    {
        private PlayerBuildRuntime build;
        private ModifierSelectionController presentation;
        private PlayerMovement player;
        private NetworkCombatWorld world;
        private EquipmentModifierOfferProvider provider;
        private IReadOnlyList<ModifierOffer> serverOffers = Array.Empty<ModifierOffer>();
        private WeaponBehaviour serverWeapon;
        private uint sequence;
        private uint buildRevision;
        private uint receivedRevision;
        private uint ownerAttackBuildRevision;
        private ulong localEventId;
        private bool ownerReady;
        private bool ownerCanSelect;
        private bool diagnosedUnavailable;
        private float nextUnavailableRetry;
        private float nextEligibilityCheck;
        private PlayerBuildSnapshot preparedBuild;
        private PlayerProgressionSnapshot preparedProgression;
        private PlayerUpgradeOfferSnapshot[] restoredOffers = Array.Empty<PlayerUpgradeOfferSnapshot>();

        [SyncVar(hook = nameof(OnSelectingChanged))] private bool selecting;
        [SyncVar] private int level = 1;
        [SyncVar] private float experience;
        [SerializeField, Min(1)] private int experiencePerLevel = 2;

        public bool IsSelecting => selecting;
        public int Level => level;
        public float Experience => experience;
        public int ExperiencePerLevel => Mathf.Max(1, experiencePerLevel);
        public int PendingUpgradeCount { get; private set; }
        public ulong PendingEventId { get; private set; }
        public ulong LocalEventId => localEventId;
        public uint BuildRevision => buildRevision;
        public uint OwnerBuildRevision => ownerAttackBuildRevision;
        public bool HasOwnerBaseline => receivedRevision != 0;
        public IReadOnlyList<ModifierOffer> ServerOffers => serverOffers;

        /// <summary>Called by the server spawn coordinator before AddPlayerForConnection invokes callbacks.</summary>
        public void PrepareServerRestore(PlayerBuildSnapshot buildState, PlayerProgressionSnapshot progressionState)
        {
            if (netIdentity != null && netId != 0)
                throw new InvalidOperationException("Restoration must be prepared before the avatar spawns.");
            if (buildState == null) throw new ArgumentNullException(nameof(buildState));
            ValidateProgression(progressionState);
            preparedBuild = buildState.Copy();
            preparedProgression = progressionState.Copy();
        }

        public PlayerProgressionSnapshot CaptureProgression()
        {
            var offers = new PlayerUpgradeOfferSnapshot[serverOffers.Count];
            for (int i = 0; i < offers.Length; i++)
                offers[i] = new PlayerUpgradeOfferSnapshot
                {
                    PreviousOfferId = serverOffers[i].OfferId,
                    EquipmentId = serverOffers[i].EquipmentId,
                    LevelIndex = serverOffers[i].LevelIndex,
                    SlotIndex = serverOffers[i].TargetSlotIndex,
                    UpgradesExistingEquipment = serverOffers[i].ExistingEquipmentHandle.IsValid
                };
            return new PlayerProgressionSnapshot
            {
                Level = level,
                Experience = experience,
                PendingUpgradeCount = PendingUpgradeCount,
                BuildRevision = buildRevision,
                OfferSequence = sequence,
                Offers = offers.Length == 0 ? (PlayerUpgradeOfferSnapshot[])restoredOffers.Clone() : offers
            };
        }

        [Server]
        public void RestoreProgression(PlayerProgressionSnapshot state)
        {
            ValidateProgression(state);
            level = state.Level;
            experience = state.Experience;
            PendingUpgradeCount = state.PendingUpgradeCount;
            buildRevision = Math.Max(1u, state.BuildRevision);
            sequence = state.OfferSequence;
            restoredOffers = (PlayerUpgradeOfferSnapshot[])state.Offers.Clone();
            PendingEventId = 0;
            serverOffers = Array.Empty<ModifierOffer>();
            SetSelecting(false);
            // Do not draw or send TargetRpc before Mirror has sent the spawn and the Owner is ready.
        }

        private static void ValidateProgression(PlayerProgressionSnapshot state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Level < 1 || float.IsNaN(state.Experience) || float.IsInfinity(state.Experience) ||
                state.Experience < 0 || state.PendingUpgradeCount < 0 || state.Offers == null ||
                state.Offers.Length > 3 || (state.Offers.Length > 0 && state.PendingUpgradeCount == 0) ||
                state.OfferSequence >= 0x3ffffffeu)
                throw new ArgumentException("Invalid saved player progression.", nameof(state));
        }

        private void Awake()
        {
            build = GetComponent<PlayerBuildRuntime>();
            presentation = GetComponent<ModifierSelectionController>();
            player = GetComponent<PlayerMovement>();
            presentation.CancelRequested += CancelLocalOffer;
            presentation.PresentationReady += ResumeLocalOffers;
        }

        private void CancelLocalOffer()
        {
            if (isOwned && NetworkClient.active) CmdSetSelectionAvailable(false);
        }

        private void ResumeLocalOffers()
        {
            if (isOwned && NetworkClient.active && isActiveAndEnabled && presentation.isActiveAndEnabled)
                CmdRequestCurrentState(presentation.IsPresentationReady);
        }

        private void OnDestroy()
        {
            if (presentation != null)
            {
                presentation.CancelRequested -= CancelLocalOffer;
                presentation.PresentationReady -= ResumeLocalOffers;
            }
        }

        public override void OnStartServer()
        {
            provider = new EquipmentModifierOfferProvider(new ServerRandom());
            EnsureServerBuild();
            world = NetworkCombatWorld.Instance;
            if (world != null) world.Gateway.ConfirmedKillProduced += OnConfirmedKill;
        }

        [Server]
        public void EnsureServerBuild()
        {
            player.EnsureRuntimeInitialized();
            build.SetWeaponExecutionEnabled(isOwned);
            if (preparedBuild != null)
            {
                RuntimeDB database = GetComponent<NetworkPlayerBootstrap>().ResolveSharedRuntimeDatabase();
                build.RestoreState(database, preparedBuild);
                RestoreProgression(preparedProgression);
                preparedBuild = null;
                preparedProgression = null;
            }
            else if (!build.IsBuildActive)
            {
                RuntimeDB database = GetComponent<NetworkPlayerBootstrap>().ResolveSharedRuntimeDatabase();
                build.StartInitialBuild(database);
                buildRevision++;
            }
            serverWeapon = build.InitialWeapon;
            GetComponent<NetworkWeaponCombatAdapter>()?.CaptureSummonMaturities();
        }

        public override void OnStartAuthority()
        {
            ownerReady = true;
            build.SetWeaponExecutionEnabled(isServer || HasOwnerBaseline);
            presentation.Bind(build);
            player.SetUpgradeSelectionLocked(selecting);
            CmdRequestCurrentState(presentation.IsPresentationReady && presentation.isActiveAndEnabled);
        }

        private void OnEnable()
        {
            // Unity may re-enable a spawned component without another Mirror
            // authority callback. Restore only the current Owner's presentation.
            if (isOwned && NetworkClient.active && build != null && build.IsBuildActive)
                OnStartAuthority();
        }

        [Command]
        private void CmdRequestCurrentState(bool canSelect)
        {
            ownerCanSelect = canSelect;
            if (!canSelect && PendingEventId != 0) ServerCancelPending();
            else SendOwnerState();
            TryOpenNextOffer();
        }

        [Command]
        private void CmdSetSelectionAvailable(bool available)
        {
            ownerCanSelect = available;
            if (!available) ServerCancelPending();
        }

        /// <summary>Called by authoritative progression/reward code, never by a client XP command.</summary>
        [Server]
        public void ServerGrantExperience(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0 ||
                !build.IsBuildActive) return;
            experience += amount;
            int threshold = ExperiencePerLevel;
            int gained = Mathf.FloorToInt(experience / threshold);
            if (gained <= 0) return;
            experience -= gained * threshold;
            level += gained;
            ServerQueueUpgrades(gained);
        }

        [Server]
        public void ServerQueueUpgrades(int count = 1)
        {
            if (count <= 0 || !build.IsBuildActive) return;
            if (world != null && !world.Gateway.Ledger.IsAlive(netId)) return;
            PendingUpgradeCount = checked(PendingUpgradeCount + count);
            TryOpenNextOffer();
        }

        [Server]
        private void TryOpenNextOffer()
        {
            if (!isActiveAndEnabled || !ownerCanSelect || PendingEventId != 0 || PendingUpgradeCount == 0 ||
                !build.IsBuildActive || connectionToClient == null) return;
            IReadOnlyList<ModifierOffer> generated;
            try { generated = restoredOffers.Length > 0 ? ResolveRestoredOffers() : provider.Generate(build); }
            catch (Exception exception)
            {
                if (!diagnosedUnavailable)
                    Debug.LogError($"[UpgradeSelection] player={netId}: {exception.Message}", this);
                diagnosedUnavailable = true;
                return;
            }
            if (generated.Count == 0)
            {
                if (!diagnosedUnavailable)
                    Debug.LogWarning($"[UpgradeSelection] player={netId}: {provider.Diagnostic}", this);
                diagnosedUnavailable = true;
                return;
            }
            diagnosedUnavailable = false;
            if (++sequence >= 0x3fffffffu) throw new InvalidOperationException("Upgrade sequence exhausted.");
            PendingEventId = ((ulong)netId << 32) | sequence;
            var offers = new ModifierOffer[generated.Count];
            for (int i = 0; i < offers.Length; i++)
            {
                ModifierOffer candidate = generated[i];
                ulong optionId = ((ulong)netId << 32) | (sequence * 4u + (uint)i);
                offers[i] = new ModifierOffer(optionId, candidate.Equipment, candidate.LevelIndex,
                    candidate.TargetSlotIndex, candidate.ExistingEquipmentHandle);
            }
            serverOffers = Array.AsReadOnly(offers);
            restoredOffers = Array.Empty<PlayerUpgradeOfferSnapshot>();
            SetSelecting(true);
            SendOwnerState();
        }

        private IReadOnlyList<ModifierOffer> ResolveRestoredOffers()
        {
            var offers = new ModifierOffer[restoredOffers.Length];
            IReadOnlyList<PlayerBuildEquipmentState> equipment = build.GetEquipmentStates();
            for (int i = 0; i < offers.Length; i++)
            {
                PlayerUpgradeOfferSnapshot saved = restoredOffers[i];
                PlayerBuildEquipmentHandle handle = default;
                if (saved.UpgradesExistingEquipment)
                    foreach (PlayerBuildEquipmentState item in equipment)
                        if (item.EquipmentId == saved.EquipmentId && item.SourceSlotIndex == saved.SlotIndex &&
                            item.LevelIndex + 1 == saved.LevelIndex) { handle = item.Handle; break; }
                var offer = new ModifierOffer(0, ResolveEquipment(build.BuildDatabase, saved.EquipmentId),
                    saved.LevelIndex, saved.SlotIndex, handle);
                if (!provider.IsEligible(build, offer))
                    throw new InvalidOperationException($"Saved upgrade {saved.EquipmentId} cannot be restored; content/build mismatch.");
                offers[i] = offer;
            }
            return Array.AsReadOnly(offers);
        }

        private bool SubmitLocalSelection(ulong optionId)
        {
            if (!isOwned || !ownerReady || !NetworkClient.active || localEventId == 0) return false;
            int index = -1;
            for (int i = 0; i < presentation.Offers.Count; i++)
                if (presentation.Offers[i].OfferId == optionId) index = i;
            if (index < 0) return false;
            CmdSelect(localEventId, index);
            return true;
        }

        [Command]
        private void CmdSelect(ulong eventId, int index, NetworkConnectionToClient sender = null)
        {
            if (!ServerSelect(sender, eventId, index, out string error))
                TargetSelectionRejected(sender, eventId, error);
        }

        [Server]
        public bool ServerSelect(NetworkConnectionToClient sender, ulong eventId, int index, out string error)
        {
            error = null;
            if (!isActiveAndEnabled || !ownerCanSelect || sender == null || sender != connectionToClient || sender.identity != netIdentity ||
                eventId == 0 || eventId != PendingEventId || (uint)index >= serverOffers.Count ||
                !build.IsBuildActive || build.InitialWeapon != serverWeapon ||
                (world != null && !world.Gateway.Ledger.IsAlive(netId)))
            {
                error = "The selection is invalid, expired, or belongs to another player.";
                return false;
            }
            ModifierOffer offer = serverOffers[index];
            if (!provider.IsEligible(build, offer))
            {
                error = "The build changed and this option is no longer eligible.";
                RefreshInvalidOffer();
                return false;
            }
            try
            {
                if (offer.ExistingEquipmentHandle.IsValid)
                    build.UpgradeEquipment(offer.ExistingEquipmentHandle, offer.LevelIndex);
                else
                    build.AddEquipment(offer.TargetSlotIndex, offer.Equipment, offer.LevelIndex);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            // Consume before any acknowledgement or next offer, including the host's local RPC.
            PendingEventId = 0;
            serverOffers = Array.Empty<ModifierOffer>();
            PendingUpgradeCount--;
            buildRevision++;
            SetSelecting(false);
            SendOwnerState();
            TryOpenNextOffer();
            return true;
        }

        [TargetRpc]
        private void TargetSelectionRejected(NetworkConnectionToClient target, ulong eventId, string error)
        {
            if (isOwned && localEventId == eventId) presentation.CompleteRequest(error);
        }

        [Server]
        private void SendOwnerState()
        {
            if (connectionToClient == null) return;
            if (!build.IsBuildActive)
            {
                TargetClearSelection(connectionToClient);
                return;
            }
            var options = new UpgradeOptionMessage[serverOffers.Count];
            for (int i = 0; i < options.Length; i++)
                options[i] = new UpgradeOptionMessage { OptionId = serverOffers[i].OfferId,
                    EquipmentId = serverOffers[i].EquipmentId, LevelIndex = serverOffers[i].LevelIndex,
                    SlotIndex = serverOffers[i].TargetSlotIndex };
            TargetReceiveState(connectionToClient, buildRevision, build.CaptureState(),
                GetComponent<NetworkWeaponCombatAdapter>()?.CaptureCooldowns() ?? Array.Empty<PlayerWeaponCooldownSnapshot>(),
                GetComponent<NetworkWeaponCombatAdapter>()?.CaptureSummonMaturities() ?? Array.Empty<PlayerSummonMaturitySnapshot>(),
                PendingEventId, options);
        }

        [TargetRpc]
        private void TargetReceiveState(NetworkConnectionToClient target, uint revision,
            PlayerBuildSnapshot snapshot, PlayerWeaponCooldownSnapshot[] cooldowns,
            PlayerSummonMaturitySnapshot[] summonMaturities, ulong eventId, UpgradeOptionMessage[] options)
        {
            if (!isOwned || !ownerReady) return;
            try
            {
                RuntimeDB database = GetComponent<NetworkPlayerBootstrap>().ResolveSharedRuntimeDatabase();
                if (!isServer && revision != receivedRevision)
                {
                    build.SetWeaponExecutionEnabled(false);
                    build.ReconcileState(database, snapshot);
                }
                var attacks = GetComponent<NetworkWeaponCombatAdapter>();
                attacks?.ApplyOwnerSummonBaseline(summonMaturities);
                attacks?.ApplyOwnerCooldownBaseline(cooldowns, NetworkTime.time);
                ownerAttackBuildRevision = revision;
                build.SetWeaponExecutionEnabled(true);
                receivedRevision = revision;
                presentation.Bind(build);
                localEventId = eventId;
                if (eventId == 0)
                {
                    presentation.ClearOffers();
                    player.SetUpgradeSelectionLocked(false);
                    return;
                }
                if (options.Length < 1 || options.Length > 3)
                    throw new InvalidOperationException("Server sent an invalid offer size.");
                var localOffers = new ModifierOffer[options.Length];
                for (int i = 0; i < options.Length; i++)
                    localOffers[i] = new ModifierOffer(options[i].OptionId,
                        ResolveEquipment(database, options[i].EquipmentId), options[i].LevelIndex, options[i].SlotIndex);
                player.SetUpgradeSelectionLocked(true);
                presentation.ReceiveOffers(localOffers, SubmitLocalSelection);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                localEventId = 0;
                presentation.ClearOffers();
                player.SetUpgradeSelectionLocked(false);
                build.SetWeaponExecutionEnabled(false);
                if (eventId != 0) CmdAbortUnresolvableOffer(eventId);
            }
        }

        private static EquipmentData ResolveEquipment(RuntimeDB database, uint id)
        {
            foreach (EquipmentData item in database.EquipmentDB.Equipments)
                if (item != null && item.ID == id) return item;
            throw new InvalidOperationException($"Local EquipmentDB cannot resolve card {id}.");
        }

        [Command]
        private void CmdAbortUnresolvableOffer(ulong eventId)
        {
            // A configuration failure may cancel a choice but can never award an upgrade.
            if (eventId != 0 && eventId == PendingEventId) ServerCancelPending();
        }

        [TargetRpc]
        private void TargetClearSelection(NetworkConnectionToClient target)
        {
            if (!isOwned) return;
            localEventId = 0;
            presentation.ClearOffers();
            player.SetUpgradeSelectionLocked(false);
        }

        [Server]
        public void ServerCancelPending()
        {
            PendingEventId = 0;
            PendingUpgradeCount = 0;
            serverOffers = Array.Empty<ModifierOffer>();
            restoredOffers = Array.Empty<PlayerUpgradeOfferSnapshot>();
            SetSelecting(false);
            SendOwnerState();
        }

        [Server]
        private void SetSelecting(bool value)
        {
            selecting = value;
            player.SetUpgradeSelectionLocked(value);
            NetworkCombatWorld.Instance?.SetPlayerUpgradeSelectionState(netId, value);
        }

        private void OnSelectingChanged(bool previous, bool current) => player?.SetUpgradeSelectionLocked(current);

        private void Update()
        {
            if (isOwned && NetworkClient.active)
            {
                if (!presentation.isActiveAndEnabled && ownerReady)
                {
                    CmdSetSelectionAvailable(false);
                    ReleaseOwner();
                    build.SetWeaponExecutionEnabled(true);
                }
                else if (presentation.isActiveAndEnabled && !ownerReady && build.IsBuildActive)
                    OnStartAuthority();
            }
            if (!isServer) return;
            if (PendingEventId != 0 && (connectionToClient == null || connectionToClient.identity != netIdentity))
                ServerCancelPending();
            if (build.InitialWeapon != serverWeapon)
            {
                serverWeapon = build.InitialWeapon;
                buildRevision++;
                build.SetWeaponExecutionEnabled(isOwned);
                ServerCancelPending();
            }
            if (selecting) NetworkCombatWorld.Instance?.SetPlayerUpgradeSelectionState(netId, true);
            if (PendingEventId != 0 && Time.unscaledTime >= nextEligibilityCheck)
            {
                nextEligibilityCheck = Time.unscaledTime + 1f;
                for (int i = 0; i < serverOffers.Count; i++)
                {
                    if (provider.IsEligible(build, serverOffers[i])) continue;
                    RefreshInvalidOffer();
                    break;
                }
            }
            if (!diagnosedUnavailable || Time.unscaledTime >= nextUnavailableRetry)
            {
                nextUnavailableRetry = Time.unscaledTime + 1f;
                TryOpenNextOffer();
            }
        }

        private void OnConfirmedKill(ConfirmedKill kill)
        {
            if (kill.TargetEntityId == netId)
            {
                ServerCancelPending();
                return;
            }
            if (kill.KillerPlayerId == netId &&
                NetworkServer.spawned.TryGetValue(kill.TargetEntityId, out NetworkIdentity target))
            {
                EnemyController enemy = target.GetComponent<EnemyController>();
                if (enemy != null) ServerGrantExperience(enemy.stats.XP);
            }
        }

        private void OnDisable()
        {
            if (isServer && NetworkServer.active) ServerCancelPending();
            if (isOwned && NetworkClient.active) CmdSetSelectionAvailable(false);
            ReleaseOwner();
            if (isOwned && NetworkClient.active) build?.SetWeaponExecutionEnabled(true);
        }

        [Server]
        private void RefreshInvalidOffer()
        {
            PendingEventId = 0;
            serverOffers = Array.Empty<ModifierOffer>();
            SetSelecting(false);
            SendOwnerState();
            TryOpenNextOffer();
        }

        public override void OnStopAuthority()
        {
            ownerAttackBuildRevision = 0;
            ReleaseOwner();
        }
        public override void OnStopClient()
        {
            ownerAttackBuildRevision = 0;
            ReleaseOwner();
        }

        private void ReleaseOwner()
        {
            ownerReady = false;
            localEventId = 0;
            receivedRevision = 0;
            presentation?.Unbind();
            player?.SetUpgradeSelectionLocked(false);
            build?.SetWeaponExecutionEnabled(false);
        }

        public override void OnStopServer()
        {
            if (world != null)
            {
                world.Gateway.ConfirmedKillProduced -= OnConfirmedKill;
                world.SetPlayerUpgradeSelectionState(netId, false);
            }
            PendingEventId = 0;
            PendingUpgradeCount = 0;
            serverOffers = Array.Empty<ModifierOffer>();
            restoredOffers = Array.Empty<PlayerUpgradeOfferSnapshot>();
            selecting = false;
            player?.SetUpgradeSelectionLocked(false);
            build?.ClearBuild();
        }

        private sealed class ServerRandom : IRandomSource
        {
            private readonly System.Random random = new System.Random();
            public float Next01() => (float)(random.NextDouble() * 0.99999994d);
        }
    }
}
