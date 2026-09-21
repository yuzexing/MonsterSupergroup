using System.Collections.Generic;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkPlayerGluttony
    {
        private readonly List<Collider2D> hits = new List<Collider2D>(128);
        private readonly List<uint> candidates = new List<uint>(64);
        private readonly HashSet<uint> markedThisScan = new HashSet<uint>();
        private readonly Dictionary<uint, ulong> pendingTargets = new Dictionary<uint, ulong>();
        private ulong pendingCast, pendingPassive;
        private double nextScan;
        public bool OwnerReady => isActiveAndEnabled && isOwned && NetworkClient.active && baseline && parameters.Enabled &&
            bridge != null && bridge.EventIds != null && player != null && player.IsRuntimeInitialized && player.IsLocalOwnerBound &&
            !player.IsMenuInputBlocked && !player.IsUpgradeSelectionLocked && !player.IsRunLoadingLocked &&
            build != null && build.IsBuildActive && selection != null && selection.HasOwnerBaseline && combatant.IsAlive &&
            !BootGameplayNetworkManager.CombatHasEnded;
        private bool RequestMarkAtPointer()
        {
            if (!Application.isFocused || PanelOpen || player.InputCamera == null) return false;
            Vector2 point = Input.mousePosition;
            if (!player.InputCamera.pixelRect.Contains(point)) return false;
            Vector2 ground = GameplayCameraGeometry.OnGround(player.InputCamera.ScreenPointToRay(point));
            return RequestMark(ground - (Vector2)transform.position);
        }
        public bool RequestMark(Vector2 direction)
        {
            if (!OwnerReady || !parameters.ActiveEnabled || pendingCast != 0 ||
                NetworkTime.time < State.ActiveReadyAt || !GluttonyGeometry.Finite(direction) || direction.sqrMagnitude < .0001f) return false;
            direction.Normalize(); Vector2 origin = transform.position;
            Physics2D.OverlapBox(origin + direction * parameters.Length * .5f, new Vector2(parameters.Length, parameters.Width),
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, new ContactFilter2D { useTriggers = true }, hits);
            GluttonyGeometry.Candidates(hits, candidates, origin);
            if (candidates.Count > parameters.MaximumTargets) candidates.RemoveRange(parameters.MaximumTargets, candidates.Count - parameters.MaximumTargets);
            ulong id = bridge.EventIds.Next().Value; pendingCast = id;
            view?.ShowRectangle(id, origin, direction, parameters);
            CmdMark(id, configRevision, selection.OwnerBuildRevision, origin, direction, candidates.ToArray());
            return true;
        }
        public bool RequestDevour(uint target, bool marked)
        {
            if (!OwnerReady || pendingTargets.ContainsKey(target) ||
                (marked ? !parameters.ActiveEnabled || !HasMark(target) : !parameters.PassiveEnabled ||
                    pendingPassive != 0 || NetworkTime.time < State.PassiveReadyAt || HasMark(target))) return false;
            ulong id = bridge.EventIds.Next().Value;
            pendingTargets.Add(target, id); if (!marked) pendingPassive = id;
            CmdDevour(id, configRevision, selection.OwnerBuildRevision, target, marked, marked ? State.CastId : 0);
            return true;
        }
        private void UpdateOwner()
        {
            if (!OwnerReady || NetworkTime.time < nextScan) return;
            nextScan = NetworkTime.time + parameters.ScanInterval;
            Physics2D.OverlapCircle(transform.position, parameters.Radius, new ContactFilter2D { useTriggers = true }, hits);
            GluttonyGeometry.Candidates(hits, candidates, transform.position);
            markedThisScan.Clear();
            foreach (uint id in candidates) if (HasMark(id)) markedThisScan.Add(id);
            foreach (uint id in markedThisScan) RequestDevour(id, true);
            if (parameters.PassiveEnabled && pendingPassive == 0 && NetworkTime.time >= State.PassiveReadyAt)
                foreach (uint id in candidates)
                    if (!markedThisScan.Contains(id) && RequestDevour(id, false)) break;
        }
        [TargetRpc]
        private void TargetMarkResult(NetworkConnectionToClient target, ulong id, bool accepted, GluttonyReplica snapshot)
        {
            if (!isOwned) return;
            if (pendingCast == id) pendingCast = 0;
            ApplyOwnerSnapshot(snapshot);
            LastResult = accepted ? "Marked " + snapshot.State.Marked : "Mark rejected";
        }
        [TargetRpc]
        private void TargetDevourResult(NetworkConnectionToClient target, ulong id, uint enemy, bool accepted, string reason, GluttonyReplica snapshot)
        {
            if (!isOwned) return;
            if (pendingTargets.TryGetValue(enemy, out var pending) && pending == id) pendingTargets.Remove(enemy);
            if (pendingPassive == id) pendingPassive = 0;
            ApplyOwnerSnapshot(snapshot); LastResult = accepted ? "Devoured" : reason;
        }
        private void ApplyOwnerSnapshot(GluttonyReplica snapshot)
        {
            if (GluttonyReplica.IsNewer(snapshot.State.Revision, ownerReceipt.State.Revision)) ownerReceipt = snapshot;
        }
        [ClientRpc]
        private void RpcRectangle(ulong id, Vector2 origin, Vector2 direction, GluttonyParameters settings)
        {
            if (!isOwned) view?.ShowRectangle(id, origin, direction, settings);
        }
        [ClientRpc]
        private void RpcDevoured(ulong id, Vector2 position)
        {
            view?.ShowDevour(id, position, parameters.FeedbackVolume);
        }
    }
}
