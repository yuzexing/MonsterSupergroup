using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        // Uses only a server-admitted existing knockback command. No client cancellation RPC.
        private void BroadcastAcceptedSequenceInterrupt(NetworkEnemySimulationAgent enemy,EnemyKnockbackCommand command)
        {
            if(enemy.GetComponent<EnemyController>() is not EnemyController controller || controller.attackScript?.SupportsSharedTimeline != true ||
                !controller.CancelsAttackOnKnockback || controller.IsImmune ||
                (command.Kind == EnemyKnockbackKind.OrdinaryHit || command.Kind == EnemyKnockbackKind.Music
                    ? controller.stats.KnockBackMultiplier <= 0 : controller.attackScript.OverrideKnockback) ||
                !Registry.TryGetLatestSnapshot(enemy.netId,out var snapshot))return;
            var action=snapshot.Runtime.Action;var phase=action.PhaseAt(EnemySimulationClock.CombatNow);
            if(action.ActionId==0||command.InterruptedActionId!=action.ActionId)return;
            if(phase!=EnemyAttackPresentationPhase.Warning&&phase!=EnemyAttackPresentationPhase.Active&&phase!=EnemyAttackPresentationPhase.Recovery&&phase!=EnemyAttackPresentationPhase.Cancelled)return;
            var motion=snapshot.Runtime.Knockback;
            if(motion.Active&&snapshot.Runtime.KnockbackDamageEventId!=command.DamageEventId)return;
            if(!enemy.TryMarkSequenceInterruptBroadcast(action.ActionId))return;
            action.Phase=EnemyAttackPresentationPhase.Cancelled;enemy.RememberSequenceCancellation(action);
            snapshot.Runtime.Action=action;Registry.RecordCheckpoint(new EnemySimulationCheckpoint{Movement=snapshot});
            RpcSequenceInterrupted(enemy.netId,action.ActionId,command.CommandId,command.DamageEventId);
        }
        [ClientRpc] private void RpcSequenceInterrupted(uint enemyId,ulong actionId,ulong commandId,ulong damageEventId)
        {
            if(enemies.TryGetValue(enemyId,out var enemy)&&enemy!=null)enemy.ApplyAcceptedSequenceInterrupt(actionId);
            if(LimboReferenceLaunch.Enabled)UnityEngine.Debug.Log($"[EnemyAttackInterrupt] enemy={enemyId} action={actionId} command={commandId} hit={damageEventId}");
        }
    }
}
