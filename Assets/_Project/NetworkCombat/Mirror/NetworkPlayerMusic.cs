using System;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerPrototypeAbilities), typeof(MusicPrototypeView), typeof(MusicSpeedModifier))]
    public sealed class NetworkPlayerMusic : NetworkBehaviour, IPrototypeAbilityModule
    {
        [SerializeField] private MusicPrototypeConfig config;
        [SyncVar] private MusicParameters parameters;
        [SyncVar] private MusicSnapshot wireState;
        [SyncVar] private double speedUntil;
        [SyncVar] private float speedBonus;
        [SyncVar(hook = nameof(OnEffectsReset))] private uint effectsResetVersion;
        private MusicSnapshot receipt;
        private readonly MusicPrototypeRuntime runtime = new MusicPrototypeRuntime();
        private NetworkPlayerPrototypeAbilities abilities;
        private NetworkModifierSelection upgrades;
        private MirrorNetworkCombatBridge bridge;
        private MusicPrototypeView view;
        private MusicSpeedModifier speed;
        private uint lastRequestSequence;
        private ulong pendingStart, localCast, releasedCast;
        private double localDspStart;
        private MusicParameters localParameters;
        private uint localJudged;
        private bool localPlaying;
        private NetworkConnectionToClient serverOwner;
        public PrototypeAbilityId AbilityId => PrototypeAbilityId.Music;
        public MusicParameters Parameters => parameters;
        public MusicSnapshot State => isOwned && GluttonyReplica.IsNewer(receipt.Revision, wireState.Revision) ? receipt : wireState;
        public bool IsPerforming => State.Active || localPlaying;
        public bool IsLocalPerforming => localPlaying;
        public double LocalElapsed => AudioSettings.dspTime - localDspStart - (view != null ? view.InputCalibrationSeconds : 0);
        public double SpeedRemaining => Math.Max(0, speedUntil - NetworkTime.time);
        public string LastResult { get; private set; } = "R: start music / Space: rhythm";
        public string StatusText => IsPerforming ? $"Space: rhythm {State.Hits}/{parameters.BeatCount} | {State.Judged} judged" :
            $"R Music: {Math.Max(0, State.CooldownReadyAt - NetworkTime.time):0.0}s\n" +
            (SpeedRemaining > 0 ? $"Speed +{speedBonus:P0}: {SpeedRemaining:0.0}s\n" : "") + LastResult;
        private void Awake()
        {
            abilities = GetComponent<NetworkPlayerPrototypeAbilities>(); upgrades = GetComponent<NetworkModifierSelection>();
            bridge = GetComponent<MirrorNetworkCombatBridge>(); view = GetComponent<MusicPrototypeView>();
            speed = GetComponent<MusicSpeedModifier>();
        }
        public override void OnStartServer()
        {
            serverOwner = connectionToClient;
            parameters = NetworkCombatWorld.Instance.GetMusicParameters(config);
        }
        public bool TryHandleAction(PrototypeAbilityAction action) => action == PrototypeAbilityAction.Primary && RequestStart();
        public bool TryHandleOngoingAction(PrototypeAbilityAction action)
        {
            if (action != PrototypeAbilityAction.Rhythm || !localPlaying) return false;
            RequestBeat(); return true;
        }
        public bool RequestStart()
        {
            if (!isActiveAndEnabled || abilities == null || !abilities.OwnerCanBegin(AbilityId) || !parameters.IsValid || pendingStart != 0 ||
                IsPerforming || NetworkTime.time < State.CooldownReadyAt) return false;
            pendingStart = bridge.EventIds.Next().Value;
            CmdBegin(pendingStart, abilities.SelectionRevision);
            return true;
        }
        public bool RequestBeat()
        {
            if (!isOwned || !localPlaying || !abilities.OwnerReady) return false;
            double elapsed = LocalElapsed;
            int index = MusicTiming.InputIndex(localParameters, elapsed);
            if (index < 0 || (localJudged & (1u << index)) != 0) return false;
            SubmitLocalBeat(index, elapsed); return true;
        }
        private void SubmitLocalBeat(int index, double elapsed)
        {
            localJudged |= 1u << index;
            bool hit = MusicTiming.IsHit(localParameters, index, elapsed);
            view.ShowJudgement(index, hit);
            CmdBeat(localCast, index, elapsed);
            if (MusicTiming.Count(localJudged) == localParameters.BeatCount) StopLocalPerformance();
        }
        private bool ValidSender(NetworkConnectionToClient sender) => sender != null && sender == connectionToClient &&
            sender.identity == netIdentity && sender.isReady && sender.isAuthenticated;
        [Command(channel = Channels.Reliable)]
        private void CmdBegin(ulong id, uint revision, NetworkConnectionToClient sender = null)
        {
            if (!ValidSender(sender)) return;
            var world = NetworkCombatWorld.Instance;
            uint sequence = new CombatEventId(id).Sequence;
            bool valid = world != null && sequence > lastRequestSequence && world.Gateway.ClientIdentities.Validate(netId, id, sequence);
            if (valid) lastRequestSequence = sequence;
            valid &= isActiveAndEnabled && abilities.ServerCanBegin(AbilityId, revision) && runtime.CanBegin(parameters, NetworkTime.time);
            if (valid) valid = upgrades.ServerDeferOffers(id);
            if (valid) valid = runtime.TryBegin(id, parameters, NetworkTime.time);
            Publish();
            TargetBegin(sender, id, valid, wireState, parameters);
        }
        [TargetRpc]
        private void TargetBegin(NetworkConnectionToClient target, ulong id, bool accepted, MusicSnapshot snapshot, MusicParameters settings)
        {
            if (!isOwned) return;
            bool expected = pendingStart == id;
            if (expected) pendingStart = 0;
            Receive(snapshot);
            if (!accepted || !expected || !State.Active || State.CastId != id)
            { LastResult = "Music not ready"; return; }
            localCast = id; localParameters = settings; localJudged = 0; localPlaying = true;
            // Both clocks sampled together once. Subsequent hit times use only the audio clock.
            localDspStart = AudioSettings.dspTime + .2d;
            // Render time is buffered by snapshot interpolation. Input timestamps use Mirror's predicted timeline.
            double networkStart = NetworkTime.predictedTime + .2d;
            view.Begin(id, localDspStart, settings);
            CmdSchedule(id, networkStart);
            LastResult = "Count in... Space on each beat";
        }
        [Command(channel = Channels.Reliable)]
        private void CmdSchedule(ulong cast, double start, NetworkConnectionToClient sender = null)
        {
            if (!ValidSender(sender)) return;
            // Replayed schedules are inert; they can neither restart nor cancel an admitted performance.
            if (runtime.TrySchedule(cast, start, NetworkTime.time)) Publish();
        }
        [Command(channel = Channels.Reliable)]
        private void CmdBeat(ulong cast, int index, double elapsed, NetworkConnectionToClient sender = null)
        {
            if (!ValidSender(sender) || !ServerPerformanceValid()) return;
            bool wasActive = runtime.State.Active;
            bool accepted = runtime.TryJudge(cast, index, elapsed, NetworkTime.time, out bool hit);
            if (accepted && hit) ApplyEffect((MusicEffect)UnityEngine.Random.Range(0, 3));
            if (accepted && wasActive && !runtime.State.Active) FinishServerPerformance();
            Publish();
            TargetJudgement(sender, cast, index, accepted, hit, wireState);
        }
        private bool ServerPerformanceValid()
        {
            var world = NetworkCombatWorld.Instance;
            return isServer && isActiveAndEnabled && abilities.PrototypeEnabled && upgrades != null &&
                upgrades.isActiveAndEnabled && upgrades.OffersDeferred && !upgrades.IsSelecting && world != null &&
                !world.Gateway.CombatStopped && !BootGameplayNetworkManager.CombatHasEnded &&
                world.Gateway.Ledger.IsAlive(netId) && connectionToClient == serverOwner;
        }
        [TargetRpc]
        private void TargetJudgement(NetworkConnectionToClient target, ulong cast, int index, bool accepted, bool hit, MusicSnapshot snapshot)
        {
            if (!isOwned) return;
            Receive(snapshot);
            if (cast != localCast) return;
            if (!accepted) LastResult = "Beat rejected (duplicate / expired / timing claim)";
            else LastResult = hit ? $"Beat {index + 1}: hit" : $"Beat {index + 1}: miss";
        }
        private void ApplyEffect(MusicEffect effect)
        {
            var world = NetworkCombatWorld.Instance;
            var p = runtime.Parameters;
            if (effect == MusicEffect.Speed)
            {
                speedBonus = p.SpeedBonus; speedUntil = NetworkTime.time + p.SpeedDuration;
                RpcEffect(effect, Array.Empty<Vector2>(), 1);
                return;
            }
            var result = world.ServerApplyMusicEffect(netId, bridge.SourceEntityId, world.Gateway.NextServerEventId(), effect, p);
            RpcEffect(effect, result.Positions, result.AffectedCount);
        }
        [ClientRpc]
        private void RpcEffect(MusicEffect effect, Vector2[] positions, int affected)
        {
            view.ShowEffect(effect, positions ?? Array.Empty<Vector2>(), affected);
        }
        private void FinishServerPerformance()
        {
            var state = runtime.State;
            if (state.CastId == 0 || state.CastId == releasedCast) return;
            releasedCast = state.CastId;
            if (!state.Cancelled && state.Hits == runtime.Parameters.BeatCount) ApplyEffect(MusicEffect.Finale);
            upgrades.ServerResumeOffers(state.CastId);
            Debug.Log($"[Music] player={netId} cast={state.CastId} hits={state.Hits}/{runtime.Parameters.BeatCount} cancelled={state.Cancelled}");
        }
        private void Publish() => wireState = runtime.State;
        private void Receive(MusicSnapshot snapshot)
        {
            if (GluttonyReplica.IsNewer(snapshot.Revision, receipt.Revision)) receipt = snapshot;
            if (localPlaying && State.CastId == localCast && !State.Active)
            { StopLocalPerformance(); if (State.Cancelled) view?.Clear(); }
        }
        private void Update()
        {
            if (isServer)
            {
                if (runtime.State.Active && !ServerPerformanceValid()) ServerCancelEffects();
                else if (runtime.Advance(NetworkTime.time))
                {
                    if (!runtime.State.Active) FinishServerPerformance();
                    Publish();
                }
            }
            if (localPlaying)
            {
                if (!isOwned || !abilities.PrototypeEnabled || (State.CastId == localCast && !State.Active))
                { StopLocalPerformance(); if (!isOwned || !abilities.PrototypeEnabled || State.Cancelled) view?.Clear(); }
                else
                {
                    double elapsed = LocalElapsed;
                    for (int i = 0; i < localParameters.BeatCount && localPlaying; i++)
                        if ((localJudged & (1u << i)) == 0 && elapsed > localParameters.BeatTime(i) + localParameters.HitWindowSeconds)
                            SubmitLocalBeat(i, localParameters.BeatTime(i) + localParameters.HitWindowSeconds + .00001d);
                }
            }
            // The owner simulates movement. The modifier is separate from equipment/build snapshots.
            if (isOwned && SpeedRemaining > 0) speed.Apply(speedBonus, speedUntil);
            else speed.Clear();
        }
        [Server]
        public void ApplyServerParameters(MusicParameters settings, bool resetCooldowns = false)
        {
            if (!settings.IsValid) throw new ArgumentException("Invalid Music parameters.");
            ServerCancelEffects(); parameters = settings;
            if (resetCooldowns) runtime.ResetCooldown();
            Publish();
        }
        [Server]
        public void ServerCancelEffects()
        {
            if (runtime.Cancel(NetworkTime.time)) FinishServerPerformance();
            speedUntil = 0; speedBonus = 0; speed?.Clear(); Publish();
            effectsResetVersion = effectsResetVersion == uint.MaxValue ? 1 : effectsResetVersion + 1;
        }
        private void OnEffectsReset(uint previous, uint current)
        { StopLocalPerformance(); view?.Clear(); speed?.Clear(); }
        private void StopLocalPerformance() { localPlaying = false; view?.End(); }
        private void ReleaseOwner()
        {
            StopLocalPerformance(); view?.Clear(); speed?.Clear(); pendingStart = 0; localCast = 0; receipt = default;
        }
        public override void OnStopAuthority() => ReleaseOwner();
        public override void OnStopClient() => ReleaseOwner();
        public override void OnStopServer() => ServerCancelEffects();
        private void OnDisable() { if (isServer) ServerCancelEffects(); ReleaseOwner(); }
    }
}
