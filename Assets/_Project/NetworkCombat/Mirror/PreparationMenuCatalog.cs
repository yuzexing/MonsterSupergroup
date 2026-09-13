using MonsterSupergroup.Gameplay.Options;
using UnityEngine.Localization;
using System;
using Loc = MonsterSupergroup.Gameplay.Options.MenuLocalization;
using System.Linq;
using AstralShift.HellMaiden.Data.Cards;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Preparation Menu Catalog")]
    public sealed class PreparationMenuCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class WeaponOption
        {
            public WeaponData Weapon;
            public Sprite Icon;
            public string IconMark;
            public string Description => Weapon != null ? Weapon.GetDescription() : GameLocalization.Menu("ui.content.unavailable");
            public uint Id => Weapon != null ? Weapon.ID : 0;
            public string Name => Weapon != null ? Weapon.GetTitle() : ContentText.Name(LocalizedContentKind.Weapon, Id);
        }
        [SerializeField] private LocalizedString localizedCharacterName = new();
        public LocalizedString LocalizedCharacterName => localizedCharacterName;
        public string CharacterName => GameLocalization.Resolve(localizedCharacterName, 1);
        public Sprite CharacterPortrait;
        public bool UseCharacterMonogram;
        [SerializeField] private LocalizedString localizedMapName = new();
        [SerializeField] private LocalizedString localizedMapDescription = new();
        public LocalizedString LocalizedMapName => localizedMapName;
        public LocalizedString LocalizedMapDescription => localizedMapDescription;
        public string MapName => GameLocalization.Resolve(localizedMapName, 1);
        public string MapDescription => GameLocalization.Resolve(localizedMapDescription, 1);
        public WeaponOption[] Weapons = Array.Empty<WeaponOption>();
        public uint[] AllowedWeaponIds => Weapons.Select(w => w.Id).ToArray();
        public WeaponOption FindWeapon(uint id) => Weapons.FirstOrDefault(w => w.Id == id);
        public static PreparationMenuCatalog Load()
        {
            var catalog = Resources.Load<PreparationMenuCatalog>("PreparationMenuCatalog");
            if (catalog == null) throw new InvalidOperationException("Resources/PreparationMenuCatalog is missing.");
            return catalog;
        }
    }
}
