using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MonsterSupergroup.GAS
{
    public sealed class RuntimeEquipmentModifiers
    {
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<ModifierHandle, Entry> entriesByHandle =
            new Dictionary<ModifierHandle, Entry>();

        private readonly List<StaticStatModifier> staticModifiers = new List<StaticStatModifier>();
        private readonly List<DynamicStatModifier> dynamicModifiers = new List<DynamicStatModifier>();
        private readonly List<DynamicOnDamageModifier> dynamicOnDamageModifiers =
            new List<DynamicOnDamageModifier>();
        private readonly List<OnHitModifier> onHitModifiers = new List<OnHitModifier>();
        private readonly List<OnPredictedLethalHitModifier> predictedLethalHitModifiers =
            new List<OnPredictedLethalHitModifier>();
        private readonly List<OnKillModifier> onKillModifiers = new List<OnKillModifier>();
        private readonly ReadOnlyCollection<StaticStatModifier> readOnlyStaticModifiers;
        private readonly ReadOnlyCollection<DynamicStatModifier> readOnlyDynamicModifiers;
        private readonly ReadOnlyCollection<DynamicOnDamageModifier> readOnlyDynamicOnDamageModifiers;
        private readonly ReadOnlyCollection<OnHitModifier> readOnlyOnHitModifiers;
        private readonly ReadOnlyCollection<OnPredictedLethalHitModifier>
            readOnlyPredictedLethalHitModifiers;
        private readonly ReadOnlyCollection<OnKillModifier> readOnlyOnKillModifiers;

        private long nextHandle = 1;
        private long nextInsertionSequence;

        public RuntimeEquipmentModifiers()
        {
            readOnlyStaticModifiers = staticModifiers.AsReadOnly();
            readOnlyDynamicModifiers = dynamicModifiers.AsReadOnly();
            readOnlyDynamicOnDamageModifiers = dynamicOnDamageModifiers.AsReadOnly();
            readOnlyOnHitModifiers = onHitModifiers.AsReadOnly();
            readOnlyPredictedLethalHitModifiers = predictedLethalHitModifiers.AsReadOnly();
            readOnlyOnKillModifiers = onKillModifiers.AsReadOnly();
        }

        public IReadOnlyList<StaticStatModifier> StaticModifiers => readOnlyStaticModifiers;

        public IReadOnlyList<DynamicStatModifier> DynamicModifiers => readOnlyDynamicModifiers;

        public IReadOnlyList<DynamicOnDamageModifier> DynamicOnDamageModifiers => readOnlyDynamicOnDamageModifiers;

        public IReadOnlyList<OnHitModifier> OnHitModifiers => readOnlyOnHitModifiers;

        public IReadOnlyList<OnPredictedLethalHitModifier> PredictedLethalHitModifiers =>
            readOnlyPredictedLethalHitModifiers;

        /// <summary>Compatibility view containing only legacy OnKillModifier instances.</summary>
        public IReadOnlyList<OnKillModifier> OnKillModifiers => readOnlyOnKillModifiers;

        public bool HasModifiers => entries.Count > 0;

        public int Count => entries.Count;

        public ModifierHandle Add(RuntimeEquipmentModifier modifier)
        {
            if (modifier == null)
            {
                throw new ArgumentNullException(nameof(modifier));
            }

            EnsureSupportedStage(modifier);
            for (int i = 0; i < entries.Count; i++)
            {
                if (ReferenceEquals(entries[i].Modifier, modifier))
                {
                    throw new InvalidOperationException(
                        "The same modifier instance cannot be owned by a container more than once.");
                }
            }

            var handle = new ModifierHandle(nextHandle++);
            var entry = new Entry(handle, nextInsertionSequence++, modifier);
            entries.Add(entry);
            entriesByHandle.Add(handle, entry);
            RebuildStageLists();
            return handle;
        }

        public bool Remove(ModifierHandle handle)
        {
            if (!entriesByHandle.TryGetValue(handle, out Entry entry))
            {
                return false;
            }

            entriesByHandle.Remove(handle);
            entries.Remove(entry);
            RebuildStageLists();
            entry.Modifier.Dispose();
            return true;
        }

        public void Clear()
        {
            var ownedModifiers = new RuntimeEquipmentModifier[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                ownedModifiers[i] = entries[i].Modifier;
            }

            entries.Clear();
            entriesByHandle.Clear();
            staticModifiers.Clear();
            dynamicModifiers.Clear();
            dynamicOnDamageModifiers.Clear();
            onHitModifiers.Clear();
            predictedLethalHitModifiers.Clear();
            onKillModifiers.Clear();

            for (int i = 0; i < ownedModifiers.Length; i++)
            {
                ownedModifiers[i].Dispose();
            }
        }

        private static void EnsureSupportedStage(RuntimeEquipmentModifier modifier)
        {
            if (!(modifier is StaticStatModifier) &&
                !(modifier is DynamicStatModifier) &&
                !(modifier is DynamicOnDamageModifier) &&
                !(modifier is OnHitModifier) &&
                !(modifier is OnPredictedLethalHitModifier))
            {
                throw new ArgumentException(
                    $"Modifier type {modifier.GetType().FullName} does not belong to a supported execution stage.",
                    nameof(modifier));
            }
        }

        private void RebuildStageLists()
        {
            staticModifiers.Clear();
            dynamicModifiers.Clear();
            dynamicOnDamageModifiers.Clear();
            onHitModifiers.Clear();
            predictedLethalHitModifiers.Clear();
            onKillModifiers.Clear();

            var staticEntries = new List<Entry>();
            var dynamicEntries = new List<Entry>();
            var onDamageEntries = new List<Entry>();
            var onHitEntries = new List<Entry>();
            var predictedLethalEntries = new List<Entry>();

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                switch (entry.Modifier)
                {
                    case StaticStatModifier _:
                        staticEntries.Add(entry);
                        break;
                    case DynamicStatModifier _:
                        dynamicEntries.Add(entry);
                        break;
                    case DynamicOnDamageModifier _:
                        onDamageEntries.Add(entry);
                        break;
                    case OnHitModifier _:
                        onHitEntries.Add(entry);
                        break;
                    case OnPredictedLethalHitModifier _:
                        predictedLethalEntries.Add(entry);
                        break;
                }
            }

            staticEntries.Sort(CompareStandard);
            dynamicEntries.Sort(CompareStandard);
            onDamageEntries.Sort(CompareStandard);
            onHitEntries.Sort(CompareOnHit);
            predictedLethalEntries.Sort(ComparePredictedLethalHit);

            CopyModifiers(staticEntries, staticModifiers);
            CopyModifiers(dynamicEntries, dynamicModifiers);
            CopyModifiers(onDamageEntries, dynamicOnDamageModifiers);
            CopyModifiers(onHitEntries, onHitModifiers);
            CopyModifiers(predictedLethalEntries, predictedLethalHitModifiers);
            for (int i = 0; i < predictedLethalEntries.Count; i++)
            {
                if (predictedLethalEntries[i].Modifier is OnKillModifier onKill)
                {
                    onKillModifiers.Add(onKill);
                }
            }
        }

        private static int CompareStandard(Entry left, Entry right)
        {
            int priority = left.Modifier.GetSortPriority().CompareTo(right.Modifier.GetSortPriority());
            return priority != 0 ? priority : left.InsertionSequence.CompareTo(right.InsertionSequence);
        }

        private static int CompareOnHit(Entry left, Entry right)
        {
            int priority = CompareStagePriority(left, right);
            if (priority != 0)
            {
                return priority;
            }

            float leftRollPriority = ((OnHitModifier)left.Modifier).GetRollPriority();
            float rightRollPriority = ((OnHitModifier)right.Modifier).GetRollPriority();
            int rollPriority = rightRollPriority.CompareTo(leftRollPriority);
            return rollPriority != 0 ? rollPriority : left.InsertionSequence.CompareTo(right.InsertionSequence);
        }

        private static int ComparePredictedLethalHit(Entry left, Entry right)
        {
            int priority = CompareStagePriority(left, right);
            if (priority != 0)
            {
                return priority;
            }

            float leftRollPriority =
                ((OnPredictedLethalHitModifier)left.Modifier).GetRollPriority();
            float rightRollPriority =
                ((OnPredictedLethalHitModifier)right.Modifier).GetRollPriority();
            int rollPriority = rightRollPriority.CompareTo(leftRollPriority);
            return rollPriority != 0 ? rollPriority : left.InsertionSequence.CompareTo(right.InsertionSequence);
        }

        private static int CompareStagePriority(Entry left, Entry right)
        {
            return left.Modifier.GetSortPriority().CompareTo(right.Modifier.GetSortPriority());
        }

        private static void CopyModifiers<TModifier>(IReadOnlyList<Entry> source, ICollection<TModifier> destination)
            where TModifier : RuntimeEquipmentModifier
        {
            for (int i = 0; i < source.Count; i++)
            {
                destination.Add((TModifier)source[i].Modifier);
            }
        }

        private sealed class Entry
        {
            public Entry(ModifierHandle handle, long insertionSequence, RuntimeEquipmentModifier modifier)
            {
                Handle = handle;
                InsertionSequence = insertionSequence;
                Modifier = modifier;
            }

            public ModifierHandle Handle { get; }

            public long InsertionSequence { get; }

            public RuntimeEquipmentModifier Modifier { get; }
        }
    }
}
