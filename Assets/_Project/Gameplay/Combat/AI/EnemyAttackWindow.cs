using System;

namespace AstralShift.HellMaiden.AI
{
    public readonly struct EnemyContactStamp
    {
        public readonly ulong ActionId;
        public readonly int StrikeIndex;
        public readonly uint Epoch, Generation;
        public readonly double ContactAt;
        public EnemyContactStamp(ulong action, int strike, uint epoch, uint generation, double contactAt)
        { ActionId = action; StrikeIndex = strike; Epoch = epoch; Generation = generation; ContactAt = contactAt; }
    }

    /// <summary>Local collision admission only. It never stores HP or applies damage.</summary>
    public sealed class EnemyAttackWindow
    {
        private readonly Func<double> clock;
        private readonly Func<bool> alive;
        private EnemyActionState action;
        private uint epoch, generation;
        private bool valid;
        public EnemyAttackWindow(Func<double> clock, Func<bool> alive) { this.clock = clock; this.alive = alive; }

        public void Bind(EnemyActionState state, uint assignmentEpoch, uint instanceGeneration)
        {
            action = state; epoch = assignmentEpoch; generation = instanceGeneration;
            valid = state.ActionId != 0 && state.Phase != EnemyAttackPresentationPhase.Cancelled && EnemyActionTimeline.HasPose(state);
        }
        public void Cancel() { valid = false; generation++; }
        public bool TryCapture(out EnemyContactStamp stamp)
        {
            double now = clock();
            stamp = new EnemyContactStamp(action.ActionId, action.StrikeIndex, epoch, generation, now);
            return valid && alive() && action.Phase == EnemyAttackPresentationPhase.Active &&
                now >= action.WarningUntil && now < action.ActiveUntil;
        }
        public bool CanSettle(EnemyContactStamp stamp) => valid && alive() &&
            stamp.ActionId == action.ActionId && stamp.StrikeIndex == action.StrikeIndex &&
            stamp.Epoch == epoch && stamp.Generation == generation &&
            stamp.ContactAt >= action.WarningUntil && stamp.ContactAt < action.ActiveUntil;
    }
}
