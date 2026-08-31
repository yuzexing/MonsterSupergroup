using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.GAS.Unity;
using UnityEngine;
using GasAttackStatsMultipliers = MonsterSupergroup.GAS.AttackStatsMultipliers;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatRuntimeServiceProvider))]
    public sealed class PlayerBuildRuntime : MonoBehaviour
    {
        private readonly Dictionary<WeaponBehaviour, WeaponEntry> weapons =
            new Dictionary<WeaponBehaviour, WeaponEntry>();
        private readonly Dictionary<long, EquippedModifierGroup> equipmentByHandle =
            new Dictionary<long, EquippedModifierGroup>();
        private readonly Dictionary<long, PerkDataModifier> perksByHandle =
            new Dictionary<long, PerkDataModifier>();

        [SerializeField] private PlayerMovement owner;
        [SerializeField] private CombatRuntimeServiceProvider serviceProvider;
        [SerializeField] private uint initialWeaponId = 2u;

        private readonly GasAttackStatsMultipliers perkMultipliers =
            new GasAttackStatsMultipliers();
        private RuntimeModifierFactory factory;
        private IRandomSource randomSource;
        private long nextEquipmentHandle = 1;
        private long nextPerkHandle = 1;
        private bool initialized;

        public PlayerMovement Owner => owner;
        public int WeaponCount => weapons.Count;
        public int EquipmentCount => equipmentByHandle.Count;
        public int PerkCount => perksByHandle.Count;
        public GasAttackStatsMultipliers PerkMultipliers => perkMultipliers;
        public uint InitialWeaponId => initialWeaponId;
        public RuntimeDB BuildDatabase { get; private set; }
        public WeaponBehaviour InitialWeapon { get; private set; }
        public bool IsBuildActive => InitialWeapon != null &&
            weapons.ContainsKey(InitialWeapon);

        private void Awake()
        {
            EnsureInitialized();
        }

        public void Initialize(
            PlayerMovement player = null,
            IRandomSource buildRandomSource = null)
        {
            if (initialized)
            {
                return;
            }

            owner = player != null ? player : owner;
            if (owner == null)
            {
                owner = GetComponent<PlayerMovement>();
            }

            if (owner == null)
            {
                throw new InvalidOperationException(
                    "PlayerBuildRuntime must be attached to, or configured with, a PlayerMovement.");
            }

            if (serviceProvider == null)
            {
                serviceProvider = GetComponent<CombatRuntimeServiceProvider>();
            }

            factory = new RuntimeModifierFactory(GeneratedModifierRegistry.Create());
            randomSource = buildRandomSource ?? new UnityRandomSource();
            serviceProvider.ServicesChanged += ConfigureCombatRuntimeServices;
            initialized = true;
        }

        public WeaponBehaviour EquipWeapon(WeaponData weaponData, Transform parent = null)
        {
            EnsureInitialized();
            if (weaponData == null)
            {
                throw new ArgumentNullException(nameof(weaponData));
            }

            NativeGasWeaponDefinition definition = weaponData.NativeGasDefinition;
            if (definition == null)
            {
                throw new InvalidOperationException(
                    $"WeaponData '{weaponData.name}' has no NativeGasWeaponDefinition.");
            }

            if (weaponData.WeaponPrefab == null)
            {
                throw new InvalidOperationException(
                    $"WeaponData '{weaponData.name}' has no WeaponPrefab.");
            }

            definition.Validate();
            Transform weaponParent = parent != null ? parent : owner.AttacksParent;
            if (weaponParent == null)
            {
                throw new InvalidOperationException(
                    "PlayerBuildRuntime requires an attacks parent to equip a weapon.");
            }

            WeaponBehaviour behaviour = null;
            var modifiers = new RuntimeEquipmentModifiers();
            try
            {
                behaviour = Instantiate(weaponData.WeaponPrefab, weaponParent);
                behaviour.gameObject.SetActive(false);
                behaviour.ConfigureOwner(owner);

                WeaponRuntimeBehaviour runtime =
                    behaviour.GetComponent<WeaponRuntimeBehaviour>();
                if (runtime == null)
                {
                    runtime = behaviour.gameObject.AddComponent<WeaponRuntimeBehaviour>();
                }

                runtime.InitializeOnAwake = false;
                ConfigureRuntime(runtime, definition, modifiers);
                behaviour.ConfigureNativeRuntime(runtime, definition);
                behaviour.Init(weaponData.ID, weaponData.BaseStats);

                weapons.Add(
                    behaviour,
                    new WeaponEntry(behaviour, runtime, definition, modifiers));
                behaviour.gameObject.SetActive(true);
                return behaviour;
            }
            catch
            {
                modifiers.Clear();
                if (behaviour != null)
                {
                    Destroy(behaviour.gameObject);
                }

                throw;
            }
        }

        public WeaponBehaviour EquipWeapon(uint weaponId, Transform parent = null)
        {
            if (BuildDatabase == null)
            {
                throw new InvalidOperationException(
                    "PlayerBuildRuntime requires an active RuntimeDB before equipping by ID.");
            }

            return EquipWeapon(BuildDatabase.GetWeaponData(weaponId), parent);
        }

        public bool UnequipWeapon(WeaponBehaviour weapon)
        {
            EnsureInitialized();
            if (weapon == null || !weapons.TryGetValue(weapon, out WeaponEntry entry))
            {
                return false;
            }

            RemoveEquipmentFor(entry);
            weapons.Remove(weapon);
            if (ReferenceEquals(InitialWeapon, weapon))
            {
                InitialWeapon = null;
            }
            entry.Behaviour.Deactivate();
            entry.Runtime.Shutdown();
            entry.Modifiers.Clear();
            Destroy(entry.Behaviour.gameObject);
            return true;
        }

        public void ConfigureInitialWeapon(uint weaponId)
        {
            if (IsBuildActive)
            {
                throw new InvalidOperationException(
                    "The initial weapon cannot change while the build is active.");
            }

            initialWeaponId = weaponId;
        }

        public WeaponBehaviour StartInitialBuild(RuntimeDB database)
        {
            EnsureInitialized();
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            ClearBuild();
            BuildDatabase = database;
            try
            {
                InitialWeapon = EquipWeapon(initialWeaponId);
                return InitialWeapon;
            }
            catch
            {
                BuildDatabase = null;
                InitialWeapon = null;
                throw;
            }
        }

        public void ClearBuild()
        {
            if (!initialized)
            {
                BuildDatabase = null;
                InitialWeapon = null;
                return;
            }

            WeaponBehaviour[] equippedWeapons = new WeaponBehaviour[weapons.Count];
            weapons.Keys.CopyTo(equippedWeapons, 0);
            for (int i = 0; i < equippedWeapons.Length; i++)
            {
                UnequipWeapon(equippedWeapons[i]);
            }

            equipmentByHandle.Clear();
            perksByHandle.Clear();
            perkMultipliers.Reset();
            BuildDatabase = null;
            InitialWeapon = null;
        }

        public PlayerBuildEquipmentHandle AddEquipment(
            WeaponBehaviour weapon,
            NativeGasEquipmentDefinition equipment,
            int levelIndex)
        {
            EnsureInitialized();
            if (weapon == null || !weapons.TryGetValue(weapon, out WeaponEntry entry))
            {
                throw new ArgumentException(
                    "Weapon is not owned by this PlayerBuildRuntime.",
                    nameof(weapon));
            }

            if (equipment == null)
            {
                throw new ArgumentNullException(nameof(equipment));
            }

            IReadOnlyList<EquipmentDataModifier> definitions =
                equipment.GetModifiers(levelIndex);
            var modifierHandles = new List<ModifierHandle>(definitions.Count);
            try
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    EquipmentDataModifier data = definitions[i] ??
                        throw new InvalidOperationException(
                            $"{equipment.name} has a null modifier at level {levelIndex}, index {i}.");
                    if (!entry.Definition.Supports(data.ModifierId))
                    {
                        throw new InvalidOperationException(
                            $"Weapon '{entry.Definition.name}' does not support modifier " +
                            $"0x{data.ModifierIdValue:X8} from '{equipment.name}'.");
                    }

                    RuntimeEquipmentModifier runtimeModifier = data.CreateRuntime(factory);
                    try
                    {
                        modifierHandles.Add(entry.Modifiers.Add(runtimeModifier));
                    }
                    catch
                    {
                        runtimeModifier.Dispose();
                        throw;
                    }
                }

                var handle = new PlayerBuildEquipmentHandle(nextEquipmentHandle++);
                equipmentByHandle.Add(
                    handle.Value,
                    new EquippedModifierGroup(entry, modifierHandles));
                entry.Runtime.RefreshStats();
                return handle;
            }
            catch
            {
                for (int i = modifierHandles.Count - 1; i >= 0; i--)
                {
                    entry.Modifiers.Remove(modifierHandles[i]);
                }

                entry.Runtime.RefreshStats();
                throw;
            }
        }

        public bool RemoveEquipment(PlayerBuildEquipmentHandle handle)
        {
            EnsureInitialized();
            if (!handle.IsValid ||
                !equipmentByHandle.TryGetValue(handle.Value, out EquippedModifierGroup group))
            {
                return false;
            }

            equipmentByHandle.Remove(handle.Value);
            for (int i = group.ModifierHandles.Count - 1; i >= 0; i--)
            {
                group.Weapon.Modifiers.Remove(group.ModifierHandles[i]);
            }

            if (group.Weapon.Runtime != null && group.Weapon.Runtime.IsInitialized)
            {
                group.Weapon.Runtime.RefreshStats();
            }

            return true;
        }

        public PlayerBuildPerkHandle AddPerk(PerkDataModifier perk)
        {
            EnsureInitialized();
            if (perk == null)
            {
                throw new ArgumentNullException(nameof(perk));
            }

            var handle = new PlayerBuildPerkHandle(nextPerkHandle++);
            perksByHandle.Add(handle.Value, perk);
            try
            {
                RebuildPerks();
                return handle;
            }
            catch
            {
                perksByHandle.Remove(handle.Value);
                RebuildPerks();
                throw;
            }
        }

        public bool RemovePerk(PlayerBuildPerkHandle handle)
        {
            EnsureInitialized();
            if (!handle.IsValid || !perksByHandle.Remove(handle.Value))
            {
                return false;
            }

            RebuildPerks();
            return true;
        }

        public void ConfigureCombatRuntimeServices(CombatRuntimeServices services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (!initialized)
            {
                EnsureInitialized();
            }

            foreach (WeaponEntry entry in weapons.Values)
            {
                ConfigureRuntime(entry.Runtime, entry.Definition, entry.Modifiers, services);
            }
        }

        private void ConfigureRuntime(
            WeaponRuntimeBehaviour runtime,
            NativeGasWeaponDefinition definition,
            RuntimeEquipmentModifiers modifiers,
            CombatRuntimeServices services = null)
        {
            CombatRuntimeServices effectiveServices = services ?? serviceProvider.Services;
            effectiveServices.Configure(runtime);
            runtime.InitializeExternal(
                definition.BaseStats,
                definition.CombatId,
                modifiers,
                perkMultipliers,
                randomSource,
                effectiveServices.EventIds,
                effectiveServices.EventSink,
                effectiveServices.TriggerGuard,
                effectiveServices.TimeSource);
        }

        private void RebuildPerks()
        {
            perkMultipliers.Reset();
            var orderedHandles = new List<long>(perksByHandle.Keys);
            orderedHandles.Sort();
            for (int i = 0; i < orderedHandles.Count; i++)
            {
                RuntimePerkModifier runtime =
                    perksByHandle[orderedHandles[i]].CreateRuntime(factory);
                if (!(runtime is WeaponStatsPerkModifier weaponStats))
                {
                    throw new NotSupportedException(
                        $"Perk modifier {runtime.GetType().FullName} does not modify weapon stats.");
                }

                weaponStats.Apply(perkMultipliers);
            }

            foreach (WeaponEntry entry in weapons.Values)
            {
                entry.Runtime.RefreshStats();
            }
        }

        private void RemoveEquipmentFor(WeaponEntry entry)
        {
            var handles = new List<long>();
            foreach (KeyValuePair<long, EquippedModifierGroup> pair in equipmentByHandle)
            {
                if (ReferenceEquals(pair.Value.Weapon, entry))
                {
                    handles.Add(pair.Key);
                }
            }

            for (int i = 0; i < handles.Count; i++)
            {
                RemoveEquipment(new PlayerBuildEquipmentHandle(handles[i]));
            }
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                Initialize();
            }
        }

        public void Shutdown()
        {
            if (!initialized)
            {
                return;
            }

            serviceProvider.ServicesChanged -= ConfigureCombatRuntimeServices;
            ClearBuild();
            initialized = false;
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private sealed class WeaponEntry
        {
            public WeaponEntry(
                WeaponBehaviour behaviour,
                WeaponRuntimeBehaviour runtime,
                NativeGasWeaponDefinition definition,
                RuntimeEquipmentModifiers modifiers)
            {
                Behaviour = behaviour;
                Runtime = runtime;
                Definition = definition;
                Modifiers = modifiers;
            }

            public WeaponBehaviour Behaviour { get; }
            public WeaponRuntimeBehaviour Runtime { get; }
            public NativeGasWeaponDefinition Definition { get; }
            public RuntimeEquipmentModifiers Modifiers { get; }
        }

        private sealed class EquippedModifierGroup
        {
            public EquippedModifierGroup(
                WeaponEntry weapon,
                IReadOnlyList<ModifierHandle> modifierHandles)
            {
                Weapon = weapon;
                ModifierHandles = modifierHandles;
            }

            public WeaponEntry Weapon { get; }
            public IReadOnlyList<ModifierHandle> ModifierHandles { get; }
        }
    }

    public readonly struct PlayerBuildEquipmentHandle
    {
        internal PlayerBuildEquipmentHandle(long value)
        {
            Value = value;
        }

        internal long Value { get; }
        public bool IsValid => Value > 0;
    }

    public readonly struct PlayerBuildPerkHandle
    {
        internal PlayerBuildPerkHandle(long value)
        {
            Value = value;
        }

        internal long Value { get; }
        public bool IsValid => Value > 0;
    }
}
