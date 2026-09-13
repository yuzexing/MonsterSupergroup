using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using MonsterSupergroup.GAS;
using UnityEngine.Localization;

namespace MonsterSupergroup.Gameplay.Options
{
    public enum LocalizedContentKind { Weapon, Equipment, Perk, Ultimate, Character, Map }
    public static class ContentText
    {
        public static string Name(LocalizedContentKind kind, uint id) => GameLocalization.Content(Key(kind, id, "name"), id);
        public static string Key(LocalizedContentKind kind, uint id, string field) => kind.ToString().ToLowerInvariant() + "." + id + "." + field;
        public static LocalizedString Reference(LocalizedContentKind kind, uint id, string field) => new(GameLocalization.ContentTable, Key(kind, id, field));
        public static string Description(WeaponData data) => data.GetDescription();
        public static string Description(EquipmentData data, uint level) => data.GetDescription(level);
        public static string Description(PerkData data, PerkRarity rarity) => data.GetDescription(rarity);
        public static string Description(RuntimeDB database, LocalizedContentKind kind, uint id, uint levelIndex = 0, PerkRarity rarity = PerkRarity.Bronze)
        {
            if (kind == LocalizedContentKind.Weapon)
                return database?.WeaponDB?.Weapons?.FirstOrDefault(w => w.ID == id)?.GetDescription() ?? GameLocalization.Content(Key(kind, id, "description"), id);
            if (kind == LocalizedContentKind.Equipment)
                return database?.EquipmentDB?.Equipments?.FirstOrDefault(e => e.ID == id)?.GetDescription(levelIndex) ?? GameLocalization.Menu("ui.content.unknown", id);
            if (kind == LocalizedContentKind.Perk)
            {
                var data = database?.PerkDB?.Perks?.FirstOrDefault(p => p.ID == id);
                return data != null && data.HasRarity(rarity) ? data.GetDescription(rarity) : GameLocalization.Menu("ui.content.unknown", id);
            }
            return GameLocalization.Content(Key(kind, id, "description"), id);
        }
        public static string Rarity(PerkRarity rarity) => GameLocalization.Menu("ui.rarity." + rarity.ToString().ToLowerInvariant());
        public static Dictionary<string, object> Arguments(EquipmentModifierApplication[] modifiers)
        { var args = new Dictionary<string, object>(); foreach (var modifier in modifiers) Add(args, modifier.Parameters); return args; }
        public static Dictionary<string, object> Arguments(PerkModifierApplication[] modifiers)
        { var args = new Dictionary<string, object>(); foreach (var modifier in modifiers) Add(args, modifier.Parameters); return args; }
        // Units belong to typed effects, never to a translation's punctuation or old term spelling.
        private static void Add(Dictionary<string, object> args, object parameters)
        {
            switch (parameters)
            {
                case DamageStatModifierParameters p: args["damagePercent"] = p.multiplierIncrement * 100; break;
                case SpeedStatModifierParameters p: args["attackSpeedPercent"] = p.multiplierIncrement * 100; break;
                case SizeStatModifierParameters p: args["sizePercent"] = p.multiplierIncrement * 100; break;
                case DurationStatModifierParameters p: args["durationPercent"] = p.multiplierIncrement * 100; break;
                case CritRateStatModifierParameters p: args["critChancePercent"] = p.multiplierIncrement * 100; break;
                case CritMultiplierStatModifierParameters p: args["critDamagePercent"] = p.multiplierIncrement * 100; break;
                case KnockbackStatModifierParameters p: args["knockbackPercent"] = p.multiplierIncrement * 100; break;
                case ProjectileCountStatModifierParameters p: args["projectileCount"] = p.countIncrement; break;
                case WeaponDamagePerkModifierParameters p: args["damagePercent"] = p.multiplierIncrement * 100; break;
                case WeaponSpeedPerkModifierParameters p: args["attackSpeedPercent"] = p.multiplierIncrement * 100; break;
                case WeaponSizePerkModifierParameters p: args["sizePercent"] = p.multiplierIncrement * 100; break;
                case WeaponDurationPerkModifierParameters p: args["durationPercent"] = p.multiplierIncrement * 100; break;
                case WeaponCritRatePerkModifierParameters p: args["critChancePercent"] = p.multiplierIncrement * 100; break;
                case WeaponCritMultiplierPerkModifierParameters p: args["critDamagePercent"] = p.multiplierIncrement * 100; break;
                case WeaponProjectileCountPerkModifierParameters p: args["projectileCount"] = p.countIncrement; break;
                case OnHitBurnModifierParameters p:
                    args["chancePercent"] = p.chance * 100; args["damagePercent"] = p.damageMultiplier * 100;
                    args["hitCount"] = p.numberOfHits; args["interval"] = p.hitIntervalDuration; break;
                // An effect may have no display parameters. Required missing selectors fail the editor validation.
                default: break;
            }
        }
    }
}
