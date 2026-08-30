using System;

namespace MonsterSupergroup.GAS
{
    internal sealed class RuntimeModifierExecutionSnapshot : IDisposable
    {
        private RuntimeEquipmentModifiers owner;
        private RuntimeEquipmentModifiers.Entry[] leasedEntries;

        internal RuntimeModifierExecutionSnapshot(
            RuntimeEquipmentModifiers owner,
            RuntimeEquipmentModifiers.Entry[] leasedEntries,
            DynamicOnDamageModifier[] dynamicOnDamageModifiers,
            OnHitModifier[] onHitModifiers,
            OnPredictedLethalHitModifier[] predictedLethalHitModifiers)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.leasedEntries = leasedEntries ?? throw new ArgumentNullException(nameof(leasedEntries));
            DynamicOnDamageModifiers = dynamicOnDamageModifiers ??
                throw new ArgumentNullException(nameof(dynamicOnDamageModifiers));
            OnHitModifiers = onHitModifiers ?? throw new ArgumentNullException(nameof(onHitModifiers));
            PredictedLethalHitModifiers = predictedLethalHitModifiers ??
                throw new ArgumentNullException(nameof(predictedLethalHitModifiers));
        }

        internal DynamicOnDamageModifier[] DynamicOnDamageModifiers { get; }
        internal OnHitModifier[] OnHitModifiers { get; }
        internal OnPredictedLethalHitModifier[] PredictedLethalHitModifiers { get; }

        public void Dispose()
        {
            RuntimeEquipmentModifiers currentOwner = owner;
            RuntimeEquipmentModifiers.Entry[] currentEntries = leasedEntries;
            owner = null;
            leasedEntries = null;
            currentOwner?.ReleaseExecutionSnapshot(currentEntries);
        }
    }
}
