using System.Collections.Generic;
using Mirror;
using MonsterSupergroup.GAS;
using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkPlayerGluttony
    {
        private uint lastRequestSequence;
        private bool ValidateRequest(NetworkConnectionToClient sender, ulong id, uint revision, uint buildRevision)
        {
            var world = NetworkCombatWorld.Instance;
            if (sender == null || sender != connectionToClient || sender.identity != netIdentity || !sender.isReady ||
                !sender.isAuthenticated || world == null || bridge == null) return false;
            uint sequence = new CombatEventId(id).Sequence;
            if (sequence <= lastRequestSequence || !world.Gateway.ClientIdentities.Validate(netId, id, sequence)) return false;
            lastRequestSequence = sequence;
            return isActiveAndEnabled && baseline && parameters.Enabled && IsPrototypeEnabled && revision == configRevision &&
                selection != null && selection.isActiveAndEnabled && buildRevision != 0 && buildRevision == selection.BuildRevision &&
                player != null && player.IsRuntimeInitialized && !player.IsRunLoadingLocked && build != null && build.IsBuildActive &&
                !BootGameplayNetworkManager.CombatHasEnded && !world.Gateway.CombatStopped &&
                world.Gateway.Ledger.IsAlive(netId) && !world.Gateway.Ledger.IsPlayerSelectingUpgrade(netId);
        }
        [Command(channel = Channels.Reliable)]
        private void CmdMark(ulong id, uint revision, uint buildRevision, uint abilityRevision, Vector2 origin, Vector2 direction,
            uint[] targets, NetworkConnectionToClient sender = null)
        {
            bool valid = ValidateRequest(sender,id,revision,buildRevision) && abilities.ServerCanBegin(AbilityId, abilityRevision) &&
                runtime.CanCast(parameters,NetworkTime.time) &&
                targets != null && targets.Length <= parameters.MaximumTargets && GluttonyGeometry.Finite(origin) &&
                GluttonyGeometry.Finite(direction) && Mathf.Abs(direction.sqrMagnitude-1) < .01f &&
                Vector2.Distance(origin,transform.position) <= 2f;
            if (!valid)
            {
                if (sender != null && sender == connectionToClient) TargetMarkResult(sender,id,false,wireState);
                return;
            }
            var accepted = new List<uint>(parameters.MaximumTargets);
            foreach (uint target in targets)
                if (!accepted.Contains(target) && GluttonyGeometry.ServerTarget(target,out var center,out var extent) &&
                    GluttonyGeometry.InRectangle(center,extent,origin,direction,parameters.Length,parameters.Width,.65f))
                    accepted.Add(target);
            EndBatch("replaced");
            runtime.TryCast(id,accepted,parameters,NetworkTime.time); Publish();
            RpcRectangle(id,origin,direction,parameters);
            TargetMarkResult(sender,id,true,wireState); LogSummary("empty-cast");
        }
        [Command(channel = Channels.Reliable)]
        private void CmdDevour(ulong id, uint revision, uint buildRevision, uint abilityRevision, uint target, bool marked,
            ulong cast, NetworkConnectionToClient sender = null)
        {
            double now = NetworkTime.time;
            bool valid = ValidateRequest(sender,id,revision,buildRevision) && (marked || abilities.ServerCanBegin(AbilityId, abilityRevision)) &&
                runtime.CanConsume(target,marked,cast,parameters,now);
            string reason = "Not ready / cooldown / expired mark";
            Vector2 position = default;
            if (valid)
            {
                valid = GluttonyGeometry.ServerTarget(target,out position,out var extent) &&
                    GluttonyGeometry.InCircle(position,extent,transform.position,parameters.Radius+.65f);
                reason = "Target unavailable / out of range";
            }
            if (valid)
            {
                var result = NetworkCombatWorld.Instance.ServerDevour(netId,bridge.SourceEntityId,target,id);
                valid = result.Accepted; reason = result.Rejection.ToString();
                if (valid)
                {
                    runtime.CommitConsume(target,marked,parameters,now); Publish();
                    RpcDevoured(id,position); LogSummary("collected");
                }
            }
            if (sender != null && sender == connectionToClient)
                TargetDevourResult(sender,id,target,valid,reason,wireState);
        }
    }
}
