using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Traps;
using AstralShift.Managers;
using AstralShift.Pooling;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public struct ReferenceTrapSnapshot
    {
        public uint Round, Target;
        public bool Barrier, Occupied, Collision;
        public BarrierPhase Phase;
        public Vector2 Center;
        public double BeganAt, ReleaseAt;
        public float Radius, Aspect, EntryScale, CameraOffset, CameraDuration;
        public int Count, Visible;
    }

    public sealed partial class NetworkEnemySimulationWorld
    {
        // One authoritative world state, including remaining trap occupancy after B has spawned.
        public readonly SyncDictionary<uint, ReferenceTrapSnapshot> ReferenceTraps = new SyncDictionary<uint, ReferenceTrapSnapshot>();
        [SyncVar] private string referenceTrapRulesKey;
        private GameplayWaveRules trapRules;
        private sealed class TrapObjects
        {
            public BarrierTrap Barrier;
            public readonly List<ParticleSystem> Warnings = new List<ParticleSystem>();
            public ReferenceTrapSnapshot Last;
            public uint SlowMotion;
            public PauseManager Pause;
            public Action Released;
        }
        private readonly Dictionary<uint, TrapObjects> referenceTrapObjects = new Dictionary<uint, TrapObjects>();
        private readonly List<uint> referenceTrapIds = new List<uint>();
        private uint nextReferenceBarrierId = 0x80000000u;
        private uint stoppedTrapRound = uint.MaxValue;
        private double nextTrapPublish;
        private GameplayCameraRig trapCamera;
        private bool trapCameraFramed;

        internal bool TryGetReferenceBarrier(uint id, out BarrierTrap barrier)
        {
            barrier = referenceTrapObjects.TryGetValue(id, out var objects) ? objects.Barrier : null;
            return barrier != null;
        }

        [Server]
        internal void BeginReferenceTraps(GameplayWaveRules rules)
        {
            ClearReferenceTraps(); stoppedTrapRound = uint.MaxValue;
            trapRules = rules; referenceTrapRulesKey = "LimboReference/" + rules.name;
            nextTrapPublish = 0;
        }

        [Server]
        internal void CaptureReferenceFormation(int clip, Vector2 center, int count, ReferenceWaveProgram program, double releaseAt)
        {
            if (trapRules.ReferenceFormationWarning == null) throw new InvalidOperationException("Reference B warning asset missing.");
            uint id = checked((uint)clip + 1);
            var state = new ReferenceTrapSnapshot { Round = CurrentRound, Phase = BarrierPhase.Building, Center = center,
                Count = count, Visible = count, Radius = program.BurstRadius, Aspect = program.BurstAspect,
                BeganAt = EnemySimulationClock.CombatNow, ReleaseAt = releaseAt, Occupied = true };
            ReferenceTraps[id] = state;
            referenceTrapObjects[id] = CreateTrapObjects(state);
            LogReferenceTrap(id, "captured", state);
        }

        [Server]
        internal void FinishReferenceFormation(int clip)
        {
            uint id = checked((uint)clip + 1);
            if (!ReferenceTraps.TryGetValue(id, out var state)) return;
            state.Phase = BarrierPhase.Stopping; ReferenceTraps[id] = state;
            ApplyFormation(referenceTrapObjects[id], state); LogReferenceTrap(id, "spawned-warning-stop", state);
        }

        [Server]
        internal void ReleaseReferenceFormation(int clip)
        {
            uint id = checked((uint)clip + 1);
            if (!ReferenceTraps.TryGetValue(id, out var state)) return;
            state.Occupied = false; ReferenceTraps[id] = state; LogReferenceTrap(id, "slot-released", state);
        }

        [Server]
        internal bool SpawnReferenceBarrier(Vector2 center, uint target, ReferenceBarrierDefinition definition, Action release)
        {
            if (trapRules.ReferenceBarrier == null) return false;
            uint id = ++nextReferenceBarrierId;
            var trap = Instantiate(trapRules.ReferenceBarrier, center, Quaternion.identity);
            trap.SetRadius(definition.minimumRadius, definition.maximumRadius);
            var objects = new TrapObjects { Barrier = trap, Released = release };
            referenceTrapObjects.Add(id, objects);
            var state = new ReferenceTrapSnapshot { Round = CurrentRound, Target = target, Barrier = true, Occupied = true,
                Center = center, Radius = definition.maximumRadius, Phase = BarrierPhase.Framing,
                BeganAt = EnemySimulationClock.CombatNow, EntryScale = trapRules.ReferenceBarrierEntryScale,
                CameraOffset = trap.OnSpawnCameraTargetOffset, CameraDuration = trap.OnSpawnCameraTargetDuration };
            ReferenceTraps[id] = state; objects.Last = state;
            trap.ConfigureNetworkAuthority(center, state.EntryScale, active => SetReferenceTrapEntry(objects, target, active, state.EntryScale));
            trap.Init(); trap.SetShrinkDuration(definition.shrinkDuration);
            trap.onTrapEnd += () => {
                if (!ReferenceTraps.TryGetValue(id, out var ended)) return;
                ended.Occupied = false; ended.Phase = BarrierPhase.Complete; ended.Collision = false;
                LogReferenceTrap(id, "barrier-released", ended);
                release(); objects.Released = null;
                ReferenceTraps.Remove(id); referenceTrapObjects.Remove(id);
                // Native StopCoroutine owns particle returns and destruction.
            };
            LogReferenceTrap(id, "barrier-framing", state);
            return true;
        }

        private void SetReferenceTrapEntry(TrapObjects objects, uint target, bool active, float scale)
        {
            if (active && objects.SlowMotion == 0 && PauseManager.Instance != null)
            {
                objects.Pause = PauseManager.Instance;
                objects.SlowMotion = objects.Pause.StartSlowMo(true, scale);
                NetworkCombatWorld.Instance?.SetPlayerTrapInvulnerable(target, true);
            }
            else if (!active && objects.SlowMotion != 0)
            {
                if (objects.Pause != null) objects.Pause.StopSlowMo(true, objects.SlowMotion);
                objects.SlowMotion = 0; objects.Pause = null;
                if (NetworkServer.active) NetworkCombatWorld.Instance?.SetPlayerTrapInvulnerable(target, false);
            }
        }

        [Server]
        internal void TickReferenceTrapAuthority()
        {
            bool publish = EnemySimulationClock.Now >= nextTrapPublish;
            if (publish) nextTrapPublish = EnemySimulationClock.Now + .05;
            referenceTrapIds.Clear(); referenceTrapIds.AddRange(ReferenceTraps.Keys);
            foreach (uint id in referenceTrapIds)
            {
                if (!referenceTrapObjects.TryGetValue(id, out var objects)) continue;
                var state = ReferenceTraps[id];
                if (state.Barrier)
                {
                    if (objects.Barrier == null) continue;
                    var trap = objects.Barrier;
                    var phase = trap.NetworkPhase;
                    bool changed = phase != state.Phase;
                    state.Phase = phase; state.Collision = trap.NetworkCollisionEnabled;
                    if (state.Phase != BarrierPhase.Framing) state.Radius = trap.NetworkInnerRadius;
                    state.Count = trap.NetworkGroupCount; state.Visible = trap.NetworkVisibleGroups;
                    if (publish || changed) ReferenceTraps[id] = state;
                    if (changed) LogReferenceTrap(id, "barrier-phase", state);
                }
                else if (!state.Occupied && state.Phase == BarrierPhase.Stopping && !objects.Warnings.Exists(ps => ps != null && ps.IsAlive(true)))
                {
                    DisposeTrapObjects(objects); referenceTrapObjects.Remove(id); ReferenceTraps.Remove(id);
                    LogReferenceTrap(id, "formation-disposed", state);
                }
            }
        }

        private TrapObjects CreateTrapObjects(ReferenceTrapSnapshot state)
        {
            var objects = new TrapObjects { Last = state };
            if (state.Barrier)
                objects.Barrier = Instantiate(trapRules.ReferenceBarrier, state.Center, Quaternion.identity);
            else
            {
                var pool = PoolManager.Instance.GetOrCreatePooler(trapRules.ReferenceFormationWarning, 100);
                for (int i = 0; i < state.Count; i++)
                {
                    float angle = 2 * Mathf.PI * i / state.Count;
                    var ps = pool.GetOrCreate(transform, activate: true);
                    ps.transform.position = state.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) / state.Aspect) * state.Radius;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (state.Phase == BarrierPhase.Building) ps.Play(true);
                    objects.Warnings.Add(ps);
                }
            }
            return objects;
        }

        private static void ApplyFormation(TrapObjects objects, ReferenceTrapSnapshot state)
        {
            if (objects.Last.Phase != state.Phase && state.Phase == BarrierPhase.Stopping)
                foreach (var ps in objects.Warnings) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            objects.Last = state;
        }

        private void UpdateReferenceTrapPresentation()
        {
            if (!NetworkClient.active || stoppedTrapRound == CurrentRound) return;
            if (trapRules == null && !string.IsNullOrEmpty(referenceTrapRulesKey)) trapRules = Resources.Load<GameplayWaveRules>(referenceTrapRulesKey);
            if (trapRules == null) return;
            bool framing = false;
            if (!isServer)
            {
                referenceTrapIds.Clear(); referenceTrapIds.AddRange(referenceTrapObjects.Keys);
                foreach (var id in referenceTrapIds)
                    if (!ReferenceTraps.ContainsKey(id)) { DisposeTrapObjects(referenceTrapObjects[id]); referenceTrapObjects.Remove(id); }
            }
            foreach (var pair in ReferenceTraps)
            {
                var state = pair.Value;
                if (state.Round != CurrentRound) continue;
                if (!isServer)
                {
                    if (!referenceTrapObjects.TryGetValue(pair.Key, out var objects))
                        referenceTrapObjects[pair.Key] = objects = CreateTrapObjects(state);
                    if (state.Barrier)
                        objects.Barrier.ApplyNetworkPresentation(state.Center, state.Phase, state.Radius, state.Count,
                            state.Visible, state.EntryScale, state.Collision);
                    else ApplyFormation(objects, state);
                }
                if (state.Barrier && (state.Phase == BarrierPhase.Framing || state.Phase == BarrierPhase.Building))
                {
                    if (trapCamera == null) trapCamera = FindFirstObjectByType<GameplayCameraRig>();
                    trapCamera?.SetReferenceTrapFraming(state.Center, trapRules.ReferenceBarrier.MaxRadius + state.CameraOffset,
                        (float)(EnemySimulationClock.CombatNow - state.BeganAt) / state.CameraDuration);
                    framing = true;
                }
            }
            if (!framing && trapCameraFramed) trapCamera?.ClearReferenceTrapFraming();
            trapCameraFramed = framing;
        }

        private void DisposeTrapObjects(TrapObjects objects)
        {
            if (objects.Barrier != null) { objects.Barrier.CancelNetworkLifecycle(); Destroy(objects.Barrier.gameObject); }
            if (objects.Warnings.Count != 0 && trapRules != null && PoolManager.Instance != null)
            {
                var pool = PoolManager.Instance.GetOrCreatePooler(trapRules.ReferenceFormationWarning, 100);
                foreach (var ps in objects.Warnings)
                    if (ps != null) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); pool.Return(ps); }
            }
            objects.Warnings.Clear(); objects.Released?.Invoke(); objects.Released = null;
        }

        internal void ClearReferenceTraps()
        {
            stoppedTrapRound = CurrentRound;
            foreach (var objects in referenceTrapObjects.Values) DisposeTrapObjects(objects);
            referenceTrapObjects.Clear();
            if (NetworkServer.active) ReferenceTraps.Clear();
            trapCamera?.ClearReferenceTrapFraming(); trapCameraFramed = false; trapRules = null;
        }

        private static void LogReferenceTrap(uint id, string evt, ReferenceTrapSnapshot state) =>
            Debug.Log("[LimboTrap] " + evt + " id=" + id + " combat=" + EnemySimulationClock.CombatNow.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + JsonUtility.ToJson(state));
    }
}
