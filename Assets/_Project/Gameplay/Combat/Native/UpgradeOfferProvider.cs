using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Pure candidate decisions over the existing Build and authored data. No runtime modifiers are created.</summary>
    public sealed class UpgradeOfferProvider
    {
        private readonly IRandomSource random;
        private readonly EquipmentModifierOfferProvider equipment;
        private readonly IReadOnlyList<ModifierRegistry.PerkRegistration> perkRegistrations;

        public UpgradeOfferProvider(IRandomSource random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            equipment = new EquipmentModifierOfferProvider(random);
            perkRegistrations = GeneratedModifierRegistry.Create().PerkRegistrations;
        }

        public IReadOnlyList<ModifierOffer> Generate(PlayerBuildRuntime build, ref PendingUpgradeReward reward,
            UpgradeSelectionRules rules)
        {
            if (build == null || !build.IsBuildActive) throw new InvalidOperationException("An active Build is required.");
            if (reward.Kind == UpgradeRewardKind.Weapon)
            {
                var weapons = EligibleWeapons(build);
                if (weapons.Count > 0)
                    return Sample(weapons, w => w.poolWeight).Select(w => ModifierOffer.WeaponCard(0, w)).ToArray();
                // This decision is retained in the pending queue, including across reconnection.
                reward.Kind = UpgradeRewardKind.Equipment;
            }
            if (reward.Kind == UpgradeRewardKind.Equipment) return equipment.GenerateCards(build);
            if (reward.Kind != UpgradeRewardKind.Perk) throw new InvalidOperationException("Unknown reward kind.");
            return GeneratePerks(build, rules?.PerkWeights, reward.EarnedLevel);
        }

        public IReadOnlyList<ModifierOffer> GetEquipmentTargets(PlayerBuildRuntime build, EquipmentData card) =>
            equipment.GetTargets(build, card);

        public bool IsEligible(PlayerBuildRuntime build, ModifierOffer offer)
        {
            if (build == null || !build.IsBuildActive || offer == null) return false;
            switch (offer.Kind)
            {
                case UpgradeRewardKind.Equipment: return equipment.IsEligible(build, offer);
                case UpgradeRewardKind.Weapon: return EligibleWeapons(build).Contains(offer.Weapon);
                case UpgradeRewardKind.Perk:
                    return build.BuildDatabase?.PerkDB?.Perks != null &&
                        Array.IndexOf(build.BuildDatabase.PerkDB.Perks, offer.Perk) >= 0 &&
                        TryGetNextPerk(build.CaptureState().Perks, offer.Perk, out PerkRarity rarity, out int level) &&
                        rarity == offer.Rarity && level == offer.PerkLevel && CanOfferPerk(offer.Perk, rarity);
                default: return false;
            }
        }

        private List<WeaponData> EligibleWeapons(PlayerBuildRuntime build)
        {
            var result = new List<WeaponData>();
            var owned = new HashSet<uint>();
            for (int slot = 0; slot < PlayerBuildRuntime.HandSlotCount; slot++)
                if (build.GetWeaponAtSlot(slot) != null) owned.Add(build.GetWeaponAtSlot(slot).WeaponData.ID);
            if (build.WeaponCount >= PlayerBuildRuntime.HandSlotCount) return result;
            var database = build.BuildDatabase?.WeaponDB;
            if (database?.Weapons == null) throw new InvalidOperationException("WeaponDB is missing.");
            var ids = new HashSet<uint>();
            foreach (WeaponData data in database.Weapons)
            {
                if (data == null) continue;
                if (!ids.Add(data.ID)) throw new InvalidOperationException($"Duplicate weapon ID {data.ID}.");
                ValidateWeight(data.poolWeight);
                if (owned.Contains(data.ID) || data.poolWeight == 0 || data.HasDependencies) continue;
                data.ValidateNativeGas();
                if (data.WeaponPrefab == null) throw new InvalidOperationException($"Weapon {data.ID} has no prefab.");
                result.Add(data);
            }
            return result;
        }

        // Build already stores one occurrence per acquired increment. Its reconciliation is occurrence-aware.
        public static bool TryGetNextPerk(PlayerBuildPerkSnapshot[] acquired, PerkData data,
            out PerkRarity rarity, out int level)
        {
            rarity = default;
            level = 0;
            if (data == null) return false;
            PerkRarity[] rarities = data.GetAllRarities().Select(r => r.Rarity).OrderBy(r => r).ToArray();
            if (rarities.Length == 0) return false;
            var counts = new Dictionary<PerkRarity, int>();
            foreach (var item in acquired)
                if (item.PerkId == data.ID)
                {
                    counts.TryGetValue(item.Rarity, out int count);
                    counts[item.Rarity] = count + 1;
                    level++;
                }
            int perRarity = rarities.Length == 1 && rarities[0] == PerkRarity.Crystal ? 1 : PerkData.LevelsPerRarity;
            foreach (PerkRarity next in rarities)
            {
                counts.TryGetValue(next, out int count);
                if (count >= perRarity) continue;
                rarity = next;
                level++;
                return true;
            }
            return false;
        }

        private bool CanOfferPerk(PerkData data, PerkRarity rarity)
        {
            ValidateWeight(data.poolWeight);
            if (data.poolWeight == 0 || (data.Dependencies != null && data.Dependencies.Length != 0)) return false;
            data.ValidateNativeGas();
            foreach (var application in data.GetRarity(rarity).Modifiers)
                if (application.Domain != PerkApplicationDomain.WeaponStats ||
                    !perkRegistrations.Any(r => r.Id.Value == application.ModifierIdValue &&
                        r.ParametersType == application.Parameters.GetType())) return false;
            return true;
        }

        private IReadOnlyList<ModifierOffer> GeneratePerks(PlayerBuildRuntime build, PerkDropWeightsData weights, int level)
        {
            if (weights == null) throw new InvalidOperationException("Perk drop weights are missing.");
            if (build.BuildDatabase?.PerkDB?.Perks == null) throw new InvalidOperationException("PerkDB is missing.");
            var acquired = build.CaptureState().Perks;
            var candidates = new List<ModifierOffer>();
            var ids = new HashSet<uint>();
            foreach (PerkData data in build.BuildDatabase.PerkDB.Perks)
            {
                if (data == null) continue;
                if (!ids.Add(data.ID)) throw new InvalidOperationException($"Duplicate Perk ID {data.ID}.");
                if (TryGetNextPerk(acquired, data, out PerkRarity rarity, out int next) && CanOfferPerk(data, rarity))
                    candidates.Add(ModifierOffer.PerkCard(0, data, rarity, next));
            }
            PerkDropTierWeights[] tiers = InterpolateWeights(weights, level);
            int Count(PerkDropTierWeights t) => candidates.Count(c => RarityWeight(t, c.Rarity) > 0);
            int maximum = tiers.Where(t => t.Weight > 0).Select(Count).DefaultIfEmpty(0).Max();
            if (maximum == 0) return Array.Empty<ModifierOffer>();
            int required = Math.Min(3, maximum);
            var usable = tiers.Where(t => t.Weight > 0 && Count(t) >= required).ToList();
            var tier = usable[WeightedIndex(usable, t => t.Weight)];
            candidates.RemoveAll(c => RarityWeight(tier, c.Rarity) <= 0);
            var selected = new List<ModifierOffer>();
            while (selected.Count < 3 && candidates.Count > 0)
            {
                float Weight(ModifierOffer c) => RarityWeight(tier, c.Rarity) /
                    candidates.Count(other => other.Rarity == c.Rarity) * c.Perk.poolWeight;
                int index = WeightedIndex(candidates, Weight);
                selected.Add(candidates[index]);
                candidates.RemoveAt(index);
            }
            return selected.AsReadOnly();
        }

        private static float RarityWeight(PerkDropTierWeights tier, PerkRarity rarity) =>
            tier.RarityWeights.FirstOrDefault(r => r.Rarity == rarity).Weight;

        /// <summary>Reference semantics: union of tier/rarity keys; missing endpoint weights interpolate from zero.</summary>
        public static PerkDropTierWeights[] InterpolateWeights(PerkDropWeightsData data, int level)
        {
            if (data == null || data.LevelThresholdsCount == 0) throw new InvalidOperationException("No Perk weight thresholds.");
            int index = 0;
            int previousLevel = -1;
            for (int i = 0; i < data.LevelThresholdsCount; i++)
            {
                var threshold = data.GetPerLevelThresholdDrop(i);
                if (threshold.Level < 0 || threshold.Level <= previousLevel || threshold.Drop == null)
                    throw new InvalidOperationException("Perk thresholds must be nonnegative and strictly increasing.");
                previousLevel = threshold.Level;
                var tierIds = new HashSet<PerkDropTier>();
                foreach (var tier in threshold.Drop)
                {
                    ValidateWeight(tier.Weight);
                    if (!tierIds.Add(tier.Tier) || tier.RarityWeights == null)
                        throw new InvalidOperationException("Invalid Perk Tier definition.");
                    var rarities = new HashSet<PerkRarity>();
                    foreach (var rarity in tier.RarityWeights)
                    {
                        ValidateWeight(rarity.Weight);
                        if (!rarities.Add(rarity.Rarity)) throw new InvalidOperationException("Duplicate Perk rarity weight.");
                    }
                }
                if (level >= threshold.Level) index = i;
            }
            var a = data.GetPerLevelThresholdDrop(index);
            var b = data.GetPerLevelThresholdDrop(Math.Min(index + 1, data.LevelThresholdsCount - 1));
            float t = a.Level == b.Level ? 0 : Mathf.Clamp01((float)(level - a.Level) / (b.Level - a.Level));
            var result = new List<PerkDropTierWeights>();
            foreach (var id in a.Drop.Select(x => x.Tier).Union(b.Drop.Select(x => x.Tier)).OrderBy(x => x))
            {
                var first = a.Drop.FirstOrDefault(x => x.Tier == id);
                var last = b.Drop.FirstOrDefault(x => x.Tier == id);
                var firstRarities = first.RarityWeights ?? new List<PerkRarityWeight>();
                var lastRarities = last.RarityWeights ?? new List<PerkRarityWeight>();
                var interpolated = new List<PerkRarityWeight>();
                foreach (var rarity in firstRarities.Select(x => x.Rarity).Union(lastRarities.Select(x => x.Rarity)).OrderBy(x => x))
                    interpolated.Add(new PerkRarityWeight(rarity, Mathf.Lerp(
                        firstRarities.FirstOrDefault(x => x.Rarity == rarity).Weight,
                        lastRarities.FirstOrDefault(x => x.Rarity == rarity).Weight, t)));
                result.Add(new PerkDropTierWeights(id, Mathf.Lerp(first.Weight, last.Weight, t), interpolated));
            }
            return result.ToArray();
        }

        private List<T> Sample<T>(List<T> source, Func<T, float> weight)
        {
            var result = new List<T>();
            while (source.Count > 0 && result.Count < 3)
            {
                int index = WeightedIndex(source, weight);
                result.Add(source[index]);
                source.RemoveAt(index);
            }
            return result;
        }

        private int WeightedIndex<T>(List<T> values, Func<T, float> weight)
        {
            float total = 0;
            foreach (T value in values) { float w = weight(value); ValidateWeight(w); total += w; }
            if (total <= 0 || float.IsInfinity(total)) throw new InvalidOperationException("Invalid total offer weight.");
            float sample = random.Next01();
            if (float.IsNaN(sample) || sample < 0 || sample >= 1) throw new InvalidOperationException("Offer RNG must be in [0,1).");
            float remaining = sample * total;
            for (int i = 0; i < values.Count; i++)
            {
                float w = weight(values[i]);
                if (w > 0 && remaining < w) return i;
                remaining -= w;
            }
            return values.Count - 1;
        }

        private static void ValidateWeight(float weight)
        {
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0)
                throw new InvalidOperationException("Offer weights must be finite and nonnegative.");
        }
    }
}
