using System;
using AstralShift.HellMaiden.AI;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Lossless optional blocks: absence means the complete block is default, not
    // merely inactive. Cancelled attacks and completed knockbacks retain their data.
    public static class EnemyRuntimeSerialization
    {
        public static void WriteEnemySimulationRuntimeState(this NetworkWriter writer, EnemySimulationRuntimeState value)
        {
            byte mask = 0;
            if (HasAction(value.Action)) mask |= 1;
            var motion = value.Knockback;
            if (motion.Active || Nonzero(motion.Start) || Nonzero(motion.End) || Nonzero(motion.LastPosition) ||
                motion.Elapsed != 0 || motion.Duration != 0 || motion.StaggerDuration != 0 ||
                motion.RestoreDefaultMovement || motion.RestorePathMovement || motion.RestoreCanBeStuck) mask |= 2;
            var settings = value.KnockbackSettings;
            if (settings.Distance != 0 || settings.SpeedMultiplier != 0 || settings.StaggerTime != 0 ||
                settings.FixedDirection || Nonzero(settings.Direction) || settings.PreWrapMode != 0 ||
                settings.PostWrapMode != 0 || settings.CurveKeys != null) mask |= 4;
            if (value.KnockbackCommandId != 0 || value.KnockbackDamageEventId != 0 || value.LastHandledKnockbackId != 0) mask |= 8;
            if (value.PredictedKnockbacks != null) mask |= 16;
            writer.WriteByte(mask);
            if ((mask & 1) != 0) writer.WriteEnemyActionState(value.Action);
            if ((mask & 2) != 0) writer.WriteEnemyKnockbackMotionState(value.Knockback);
            if ((mask & 4) != 0) writer.Write(value.KnockbackSettings);
            if ((mask & 8) != 0)
            {
                writer.WriteVarULong(value.KnockbackCommandId);
                writer.WriteVarULong(value.KnockbackDamageEventId);
                writer.WriteVarULong(value.LastHandledKnockbackId);
            }
            if ((mask & 16) != 0) writer.WriteArray(value.PredictedKnockbacks);
        }

        public static EnemySimulationRuntimeState ReadEnemySimulationRuntimeState(this NetworkReader reader)
        {
            byte mask = reader.ReadByte();
            if ((mask & ~31) != 0) throw new FormatException("Unknown enemy runtime block.");
            var value = new EnemySimulationRuntimeState();
            if ((mask & 1) != 0) value.Action = reader.ReadEnemyActionState();
            if ((mask & 2) != 0) value.Knockback = reader.ReadEnemyKnockbackMotionState();
            if ((mask & 4) != 0) value.KnockbackSettings = reader.Read<EnemyKnockbackSettings>();
            if ((mask & 8) != 0)
            {
                value.KnockbackCommandId = reader.ReadVarULong();
                value.KnockbackDamageEventId = reader.ReadVarULong();
                value.LastHandledKnockbackId = reader.ReadVarULong();
            }
            if ((mask & 16) != 0) value.PredictedKnockbacks = reader.ReadArray<EnemyPredictedKnockbackReceipt>();
            return value;
        }

        private static bool Nonzero(Vector2 v) => v.x != 0 || v.y != 0;
        private static bool Nonzero(Vector3 v) => v.x != 0 || v.y != 0 || v.z != 0;

        // Explicit child serializers are required once the generated parent writer is replaced.
        public static void WriteEnemyKnockbackMotionState(this NetworkWriter w, EnemyKnockbackMotionState m)
        {
            w.WriteBool(m.Active); w.WriteVector2(m.Start); w.WriteVector2(m.End); w.WriteVector2(m.LastPosition);
            w.WriteFloat(m.Elapsed); w.WriteFloat(m.Duration); w.WriteFloat(m.StaggerDuration);
            w.WriteBool(m.RestoreDefaultMovement); w.WriteBool(m.RestorePathMovement); w.WriteBool(m.RestoreCanBeStuck);
        }
        public static EnemyKnockbackMotionState ReadEnemyKnockbackMotionState(this NetworkReader r) => new EnemyKnockbackMotionState
        {
            Active = r.ReadBool(), Start = r.ReadVector2(), End = r.ReadVector2(), LastPosition = r.ReadVector2(),
            Elapsed = r.ReadFloat(), Duration = r.ReadFloat(), StaggerDuration = r.ReadFloat(),
            RestoreDefaultMovement = r.ReadBool(), RestorePathMovement = r.ReadBool(), RestoreCanBeStuck = r.ReadBool()
        };
        public static void WriteEnemyWarningStepState(this NetworkWriter w, EnemyWarningStepState s)
        {
            w.WriteBool(s.Enabled); w.WriteByte(s.StartedMask); w.WriteByte(s.CompletedMask); w.WriteDouble(s.SampledAt);
            w.WriteBool(s.Pending); w.WriteVector2(s.RequestedPosition); w.WriteVector2(s.Facing);
        }
        public static EnemyWarningStepState ReadEnemyWarningStepState(this NetworkReader r) => new EnemyWarningStepState
        {
            Enabled = r.ReadBool(), StartedMask = r.ReadByte(), CompletedMask = r.ReadByte(), SampledAt = r.ReadDouble(),
            Pending = r.ReadBool(), RequestedPosition = r.ReadVector2(), Facing = r.ReadVector2()
        };
        public static void WriteEnemyPredictedKnockbackReceipt(this NetworkWriter w, EnemyPredictedKnockbackReceipt receipt)
        { w.WriteVarULong(receipt.DamageEventId); w.WriteDouble(receipt.ExpiresAt); }
        public static EnemyPredictedKnockbackReceipt ReadEnemyPredictedKnockbackReceipt(this NetworkReader r) =>
            new EnemyPredictedKnockbackReceipt { DamageEventId = r.ReadVarULong(), ExpiresAt = r.ReadDouble() };

        private static byte ActionMask(EnemyActionState a)
        {
            byte mask = 0;
            if (a.ProjectileEmitted || Nonzero(a.ProjectileDirection)) mask |= 1;
            if (a.Dash || Nonzero(a.DashStart) || Nonzero(a.DashEnd) || Nonzero(a.DashLastPosition) || Nonzero(a.DashWarningOrigin)) mask |= 2;
            if (a.Explosion || a.ExplosionTriggered || a.SelfDestructPending || Nonzero(a.ExplosionPosition)) mask |= 4;
            if (a.Sequence || a.ComboStartedAt != 0 || Nonzero(a.SequenceWarnings) || Nonzero(a.SequenceActives) ||
                a.StrikeIndex != 0 || a.PoseStrikeIndex != 0 || a.LockedStrikeMask != 0 || a.ExecutedStrikeMask != 0) mask |= 8;
            var step = a.WarningStep;
            if (step.Enabled || step.Pending || step.StartedMask != 0 || step.CompletedMask != 0 ||
                step.SampledAt != 0 || Nonzero(step.RequestedPosition) || Nonzero(step.Facing)) mask |= 16;
            return mask;
        }

        private static bool HasAction(EnemyActionState a) => a.ActionId != 0 || a.Phase != 0 ||
            a.WarningStartedAt != 0 || a.WarningUntil != 0 || a.ActiveUntil != 0 || a.RecoveryUntil != 0 ||
            a.NextAttackAt != 0 || Nonzero(a.Facing) || Nonzero(a.TargetPosition) || ActionMask(a) != 0;

        public static void WriteEnemyActionState(this NetworkWriter w, EnemyActionState a)
        {
            byte mask = ActionMask(a);
            w.WriteByte(mask); w.WriteVarULong(a.ActionId); w.WriteByte((byte)a.Phase);
            w.WriteDouble(a.WarningStartedAt); w.WriteDouble(a.WarningUntil); w.WriteDouble(a.ActiveUntil);
            w.WriteDouble(a.RecoveryUntil); w.WriteDouble(a.NextAttackAt);
            w.WriteVector2(a.Facing); w.WriteVector2(a.TargetPosition);
            if ((mask & 1) != 0) { w.WriteVector2(a.ProjectileDirection); w.WriteBool(a.ProjectileEmitted); }
            if ((mask & 2) != 0)
            {
                w.WriteBool(a.Dash); w.WriteVector2(a.DashStart); w.WriteVector2(a.DashEnd);
                w.WriteVector2(a.DashLastPosition); w.WriteVector2(a.DashWarningOrigin);
            }
            if ((mask & 4) != 0)
            {
                w.WriteBool(a.Explosion); w.WriteBool(a.ExplosionTriggered); w.WriteBool(a.SelfDestructPending);
                w.WriteVector2(a.ExplosionPosition);
            }
            if ((mask & 8) != 0)
            {
                w.WriteBool(a.Sequence); w.WriteDouble(a.ComboStartedAt);
                w.WriteVector3(a.SequenceWarnings); w.WriteVector3(a.SequenceActives);
                w.WriteVarInt(a.StrikeIndex); w.WriteVarInt(a.PoseStrikeIndex);
                w.WriteByte(a.LockedStrikeMask); w.WriteByte(a.ExecutedStrikeMask);
            }
            if ((mask & 16) != 0) w.WriteEnemyWarningStepState(a.WarningStep);
        }

        public static EnemyActionState ReadEnemyActionState(this NetworkReader r)
        {
            byte mask = r.ReadByte();
            if ((mask & ~31) != 0) throw new FormatException("Unknown enemy action block.");
            var a = new EnemyActionState
            {
                ActionId = r.ReadVarULong(), Phase = (EnemyAttackPresentationPhase)r.ReadByte(),
                WarningStartedAt = r.ReadDouble(), WarningUntil = r.ReadDouble(), ActiveUntil = r.ReadDouble(),
                RecoveryUntil = r.ReadDouble(), NextAttackAt = r.ReadDouble(),
                Facing = r.ReadVector2(), TargetPosition = r.ReadVector2()
            };
            if ((mask & 1) != 0) { a.ProjectileDirection = r.ReadVector2(); a.ProjectileEmitted = r.ReadBool(); }
            if ((mask & 2) != 0)
            {
                a.Dash = r.ReadBool(); a.DashStart = r.ReadVector2(); a.DashEnd = r.ReadVector2();
                a.DashLastPosition = r.ReadVector2(); a.DashWarningOrigin = r.ReadVector2();
            }
            if ((mask & 4) != 0)
            {
                a.Explosion = r.ReadBool(); a.ExplosionTriggered = r.ReadBool(); a.SelfDestructPending = r.ReadBool();
                a.ExplosionPosition = r.ReadVector2();
            }
            if ((mask & 8) != 0)
            {
                a.Sequence = r.ReadBool(); a.ComboStartedAt = r.ReadDouble();
                a.SequenceWarnings = r.ReadVector3(); a.SequenceActives = r.ReadVector3();
                a.StrikeIndex = r.ReadVarInt(); a.PoseStrikeIndex = r.ReadVarInt();
                a.LockedStrikeMask = r.ReadByte(); a.ExecutedStrikeMask = r.ReadByte();
            }
            if ((mask & 16) != 0) a.WarningStep = r.ReadEnemyWarningStepState();
            return a;
        }
    }
}
