using System;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.GAS.Unity;
using UnityEngine;
using GasAttackStats = MonsterSupergroup.GAS.AttackStats;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>One transient compatibility view per bound Ultimate; never a Build slot or a database entry.</summary>
    public static class UltimateNativeDefinitionAdapter
    {
        public const uint UltimateAbilityNamespace = 0x80000000u;

        public static uint EncodeAbilityId(uint sourceId)
        {
            if ((sourceId & UltimateAbilityNamespace) != 0)
                throw new ArgumentOutOfRangeException(nameof(sourceId), "Ultimate source IDs must fit in the low 31 bits.");
            return UltimateAbilityNamespace | sourceId;
        }

        public static GasAttackStats ToNativeBaseStats(UltimateData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            EncodeAbilityId(source.Id);
            var stats = source.BaseStats;
            if (!Enum.IsDefined(typeof(MonsterSupergroup.GAS.DamageType), (int)stats.damageType))
                throw new InvalidOperationException("The Ultimate source damage type is unsupported.");
            return new GasAttackStats
            {
                damage = stats.damage, critRate = stats.critRate, critMultiplier = stats.critMultiplier,
                speed = stats.speed, size = stats.size, duration = stats.duration, projectileCount = stats.projectileCount,
                knockbackDistance = stats.knockbackSettings != null ? stats.knockbackSettings.distance : 0f,
                damageType = (MonsterSupergroup.GAS.DamageType)(int)stats.damageType
            };
        }

        public static WeaponData Create(UltimateData source)
        {
            GasAttackStats stats = ToNativeBaseStats(source);
            WeaponData view = ScriptableObject.CreateInstance<WeaponData>();
            try
            {
                view.name = source.name + " (Ultimate runtime view)";
                view.hideFlags = HideFlags.HideAndDontSave;
                view.ID = EncodeAbilityId(source.Id);
                view.WeaponPrefab = source.ultimateAttackWeaponBehaviour;
                var presentation = new WeaponPresentationSettings();
                presentation.Configure(source.BaseStats.cameraShakeSettings, source.BaseStats.cameraShakePerLevelIncrement,
                    source.BaseStats.knockbackSettings);
                view.ConfigureNativeGas(stats, CombatTags.Attack, presentation);
                return view;
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(view);
                else UnityEngine.Object.DestroyImmediate(view);
                throw;
            }
        }

        /// <summary>The caller owns the returned container, just as PlayerBuildRuntime owns each weapon's container.</summary>
        public static RuntimeEquipmentModifiers CreateIntrinsicModifiers(UltimateData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!(source.ultimateAttackWeaponBehaviour is DanteUltimateAttack dante))
                throw new NotSupportedException("Only the audited Dante Ultimate has a Native intrinsic definition.");
            if (float.IsNaN(dante.burnRate) || float.IsInfinity(dante.burnRate) || dante.burnRate <= 0f ||
                float.IsNaN(dante.burnDuration) || float.IsInfinity(dante.burnDuration) || dante.burnDuration <= 0f)
                throw new InvalidOperationException("The source Dante burn timing is invalid.");
            var definition = new EquipmentDataModifier(new EquipmentModifierID(MonsterSupergroup.GAS.OnHitBurnModifier.ModifierIdValue),
                new OnHitBurnModifierParameters(1f, dante.burnStrength,
                    Mathf.RoundToInt(dante.burnDuration / dante.burnRate), dante.burnRate));
            var factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());
            var modifiers = new RuntimeEquipmentModifiers();
            RuntimeEquipmentModifier intrinsic = definition.CreateRuntime(factory);
            try { modifiers.Add(intrinsic); return modifiers; }
            catch { intrinsic.Dispose(); modifiers.Clear(); throw; }
        }
    }
}
