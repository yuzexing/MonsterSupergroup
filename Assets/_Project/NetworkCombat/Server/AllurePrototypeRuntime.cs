using System;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum AllureAction : byte { Throw, Take, Decoy }

    [Serializable]
    public struct AllureParameters
    {
        public float ThrowCooldown, TakeCooldown, DecoyCooldown, DecoyDuration;
        public int MaximumTargets;
        public static AllureParameters Defaults => new AllureParameters
        { ThrowCooldown = 20, TakeCooldown = 20, DecoyCooldown = 20, DecoyDuration = 5, MaximumTargets = 10 };
        public bool IsValid => Range(ThrowCooldown, .1f, 120) && Range(TakeCooldown, .1f, 120) &&
            Range(DecoyCooldown, .1f, 120) && Range(DecoyDuration, .1f, 30) && MaximumTargets >= 1 && MaximumTargets <= 32;
        public float Cooldown(AllureAction action) => action == AllureAction.Throw ? ThrowCooldown :
            action == AllureAction.Take ? TakeCooldown : DecoyCooldown;
        private static bool Range(float value, float minimum, float maximum) =>
            MusicTiming.Finite(value) && value >= minimum && value <= maximum;
    }

    [Serializable]
    public struct AllureSnapshot
    {
        public uint Revision;
        public ulong LastCastId, DecoyCastId;
        public AllureAction LastAction;
        public int LastAffectedCount;
        public double ThrowReadyAt, TakeReadyAt, DecoyReadyAt, DecoyExpiresAt;
        public Vector2 DecoyPosition;
        public double ReadyAt(AllureAction action) => action == AllureAction.Throw ? ThrowReadyAt :
            action == AllureAction.Take ? TakeReadyAt : DecoyReadyAt;
    }

    /// <summary>Independent cooldowns and one decoy; selection changes never mutate this state.</summary>
    public sealed class AllurePrototypeRuntime
    {
        private AllureSnapshot state;
        public AllureSnapshot State => state;
        public static bool IsKnown(AllureAction action) => action >= AllureAction.Throw && action <= AllureAction.Decoy;
        public bool CanBegin(AllureAction action, AllureParameters parameters, double now) =>
            IsKnown(action) && parameters.IsValid && MusicTiming.Finite(now) && now >= state.ReadyAt(action);
        public bool Accept(ulong castId, AllureAction action, AllureParameters parameters, double now,
            int affectedCount, Vector2 decoyPosition, double decoyExpiresAt)
        {
            if (castId == 0 || castId == state.LastCastId || affectedCount <= 0 || affectedCount > parameters.MaximumTargets ||
                !CanBegin(action, parameters, now)) return false;
            if (action == AllureAction.Decoy && (!MusicTiming.Finite(decoyPosition.x) || !MusicTiming.Finite(decoyPosition.y) ||
                !MusicTiming.Finite(decoyExpiresAt) || decoyExpiresAt <= now)) return false;
            state.LastCastId = castId; state.LastAction = action; state.LastAffectedCount = affectedCount;
            double readyAt = now + parameters.Cooldown(action);
            switch (action)
            {
                case AllureAction.Throw: state.ThrowReadyAt = readyAt; break;
                case AllureAction.Take: state.TakeReadyAt = readyAt; break;
                case AllureAction.Decoy:
                    state.DecoyReadyAt = readyAt; state.DecoyCastId = castId;
                    state.DecoyPosition = decoyPosition; state.DecoyExpiresAt = decoyExpiresAt; break;
            }
            Changed(); return true;
        }
        public bool ClearDecoy(ulong castId)
        {
            if (castId == 0 || state.DecoyCastId != castId) return false;
            state.DecoyCastId = 0; state.DecoyExpiresAt = 0; Changed(); return true;
        }
        public void ResetCooldowns()
        { state.ThrowReadyAt = state.TakeReadyAt = state.DecoyReadyAt = 0; Changed(); }
        private void Changed() => state.Revision = state.Revision == uint.MaxValue ? 1 : state.Revision + 1;
    }
}
