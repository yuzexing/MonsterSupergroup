using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.GAS.Unity;
using UnityEngine;
using GasAttackStatsMultipliers = MonsterSupergroup.GAS.AttackStatsMultipliers;
using HellMaidenProjectileAttackBehaviour =
    AstralShift.HellMaiden.Player.Attacks.ProjectileAttackBehaviour;
using EquipmentModifierSlots =
    AstralShift.HellMaiden.Data.EquipmentModifierSlots;
using EquipmentMultiSlotConfig =
    AstralShift.HellMaiden.Data.EquipmentMultiSlotConfig;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatRuntimeServiceProvider))]
    public sealed class PlayerBuildRuntime : MonoBehaviour
    {
        public const int HandSlotCount = 4;
        public const int MaxEquipmentPerSlot = 3;

        private readonly Dictionary<WeaponBehaviour, WeaponEntry> weapons =
            new Dictionary<WeaponBehaviour, WeaponEntry>();
        private readonly WeaponEntry[] weaponSlots = new WeaponEntry[HandSlotCount];
        private readonly int[] equipmentCountsBySlot = new int[HandSlotCount];
        private readonly Dictionary<long, EquippedEquipment> equipmentByHandle =
            new Dictionary<long, EquippedEquipment>();
        private readonly Dictionary<long, EquippedPerk> perksByHandle =
            new Dictionary<long, EquippedPerk>();

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

		public event Action<ProjectilePresentationSpawn>
			ProjectilePresentationSpawned;

		public event Action<ProjectilePresentationTermination>
			ProjectilePresentationTerminated;

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
            return EquipWeaponAtSlot(FindFirstAvailableWeaponSlot(), weaponData, parent);
        }

        public WeaponBehaviour EquipWeaponAtSlot(
            int slotIndex,
            WeaponData weaponData,
            Transform parent = null)
        {
            EnsureInitialized();
            ValidateSlotIndex(slotIndex);
            if (weaponSlots[slotIndex] != null)
            {
                throw new InvalidOperationException(
                    $"Player build slot {slotIndex} already contains a weapon.");
            }

            if (weaponData == null)
            {
                throw new ArgumentNullException(nameof(weaponData));
            }

            if (weaponData.WeaponPrefab == null)
            {
                throw new InvalidOperationException(
                    $"WeaponData '{weaponData.name}' has no WeaponPrefab.");
            }

            weaponData.ValidateNativeGas();
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
                ConfigureRuntime(runtime, weaponData, modifiers);
                behaviour.ConfigureNativeRuntime(runtime, weaponData);
                behaviour.InitNative(weaponData.ID);

				var entry = new WeaponEntry(
					behaviour,
					runtime,
					weaponData,
					modifiers,
					slotIndex);
                weapons.Add(behaviour, entry);
				weaponSlots[slotIndex] = entry;
				AttachExistingEquipmentTo(entry);
				SubscribeToPresentation(behaviour);
                behaviour.gameObject.SetActive(true);
                return behaviour;
            }
            catch
            {
                if (behaviour != null &&
                    weapons.TryGetValue(behaviour, out WeaponEntry failedEntry))
                {
                    DetachEquipmentFrom(failedEntry);
                    weapons.Remove(behaviour);
                    if (weaponSlots[slotIndex] == failedEntry)
                    {
                        weaponSlots[slotIndex] = null;
                    }
                }
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

        public WeaponBehaviour EquipWeaponAtSlot(
            int slotIndex,
            uint weaponId,
            Transform parent = null)
        {
            if (BuildDatabase == null)
            {
                throw new InvalidOperationException(
                    "PlayerBuildRuntime requires an active RuntimeDB before equipping by ID.");
            }

            return EquipWeaponAtSlot(
                slotIndex,
                BuildDatabase.GetWeaponData(weaponId),
                parent);
        }

        public bool UnequipWeapon(WeaponBehaviour weapon)
        {
            EnsureInitialized();
            if (weapon == null || !weapons.TryGetValue(weapon, out WeaponEntry entry))
            {
                return false;
            }

            DetachEquipmentFrom(entry);
            weapons.Remove(weapon);
            if (weaponSlots[entry.SlotIndex] == entry)
            {
                weaponSlots[entry.SlotIndex] = null;
            }
            if (ReferenceEquals(InitialWeapon, weapon))
            {
                InitialWeapon = null;
            }
            entry.Behaviour.Deactivate();
			UnsubscribeFromPresentation(entry.Behaviour);
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
            Array.Clear(
                equipmentCountsBySlot,
                0,
                equipmentCountsBySlot.Length);
            perksByHandle.Clear();
            perkMultipliers.Reset();
            BuildDatabase = null;
            InitialWeapon = null;
        }

        public PlayerBuildEquipmentHandle AddEquipment(
            WeaponBehaviour weapon,
            EquipmentData equipment,
            int levelIndex)
        {
            EnsureInitialized();
            if (weapon == null || !weapons.TryGetValue(weapon, out WeaponEntry entry))
            {
                throw new ArgumentException(
                    "Weapon is not owned by this PlayerBuildRuntime.",
                    nameof(weapon));
            }

            return AddEquipment(entry.SlotIndex, equipment, levelIndex);
        }

        public PlayerBuildEquipmentHandle AddEquipment(
            int sourceSlotIndex,
            EquipmentData equipment,
            int levelIndex)
        {
            EnsureInitialized();
            ValidateSlotIndex(sourceSlotIndex);
            if (equipment == null)
            {
                throw new ArgumentNullException(nameof(equipment));
            }

            if (equipmentCountsBySlot[sourceSlotIndex] >= MaxEquipmentPerSlot)
            {
                throw new InvalidOperationException(
                    $"Player build slot {sourceSlotIndex} already contains the maximum " +
                    $"of {MaxEquipmentPerSlot} equipment cards.");
            }

            if (equipment.Levels == null ||
                (uint)levelIndex >= equipment.Levels.Length ||
                equipment.Levels[levelIndex] == null)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(levelIndex),
                    $"Equipment '{equipment.name}' has no level {levelIndex}.");
            }

            var group = new EquippedEquipment(
                equipment,
                levelIndex,
                sourceSlotIndex);
            try
            {
                ApplyEquipment(group);
                var handle = new PlayerBuildEquipmentHandle(nextEquipmentHandle++);
                equipmentByHandle.Add(handle.Value, group);
                equipmentCountsBySlot[sourceSlotIndex]++;
                return handle;
            }
            catch
            {
                DetachEquipment(group);
                throw;
            }
        }

        public bool RemoveEquipment(PlayerBuildEquipmentHandle handle)
        {
            EnsureInitialized();
            if (!handle.IsValid ||
                !equipmentByHandle.TryGetValue(handle.Value, out EquippedEquipment group))
            {
                return false;
            }

            equipmentByHandle.Remove(handle.Value);
            equipmentCountsBySlot[group.SourceSlotIndex]--;
            DetachEquipment(group);

            return true;
        }

        public PlayerBuildPerkHandle AddPerk(
            PerkData perk,
            PerkRarity rarity)
        {
            EnsureInitialized();
            if (perk == null)
            {
                throw new ArgumentNullException(nameof(perk));
            }

            if (!perk.HasRarity(rarity))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rarity),
                    $"Perk '{perk.name}' has no {rarity} definition.");
            }

            var handle = new PlayerBuildPerkHandle(nextPerkHandle++);
            perksByHandle.Add(handle.Value, new EquippedPerk(perk, rarity));
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
                ConfigureRuntime(entry.Runtime, entry.Data, entry.Modifiers, services);
            }
        }

        private void ConfigureRuntime(
            WeaponRuntimeBehaviour runtime,
            WeaponData weaponData,
            RuntimeEquipmentModifiers modifiers,
            CombatRuntimeServices services = null)
        {
            CombatRuntimeServices effectiveServices = services ?? serviceProvider.Services;
            effectiveServices.Configure(runtime);
            runtime.InitializeExternal(
                weaponData.BaseStats,
                weaponData.ID,
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
                EquippedPerk perk = perksByHandle[orderedHandles[i]];
                PerkModifierApplication[] applications =
                    perk.Data.GetRarity(perk.Rarity).Modifiers;
                for (int applicationIndex = 0;
                    applicationIndex < applications.Length;
                    applicationIndex++)
                {
                    PerkModifierApplication application =
                        applications[applicationIndex]
                        ?? throw new InvalidOperationException(
                            $"Perk '{perk.Data.name}' has a null modifier in " +
                            $"{perk.Rarity} at index {applicationIndex}.");
                    if (application.Domain != PerkApplicationDomain.WeaponStats)
                    {
                        throw new NotSupportedException(
                            $"Perk '{perk.Data.name}' uses the {application.Domain} " +
                            "domain, which has not been connected to PlayerBuildRuntime yet.");
                    }

                    PerkDataModifier data = application.Modifier
                        ?? throw new InvalidOperationException(
                            $"Perk '{perk.Data.name}' has no native modifier definition " +
                            $"in {perk.Rarity} at index {applicationIndex}.");
                    RuntimePerkModifier runtime = data.CreateRuntime(factory);
                    if (!(runtime is WeaponStatsPerkModifier weaponStats))
                    {
                        throw new InvalidOperationException(
                            $"Perk modifier {runtime.GetType().FullName} is authored as " +
                            "WeaponStats but does not implement WeaponStatsPerkModifier.");
                    }

                    weaponStats.Apply(perkMultipliers);
                }
            }

            foreach (WeaponEntry entry in weapons.Values)
            {
                entry.Runtime.RefreshStats();
            }
        }

        private void ApplyEquipment(EquippedEquipment equipment)
        {
            for (int slotIndex = 0; slotIndex < weaponSlots.Length; slotIndex++)
            {
                WeaponEntry entry = weaponSlots[slotIndex];
                if (entry != null)
                {
                    ApplyEquipmentToWeapon(equipment, entry);
                }
            }
        }

        private void AttachExistingEquipmentTo(WeaponEntry entry)
        {
            var orderedHandles = new List<long>(equipmentByHandle.Keys);
            orderedHandles.Sort();
            for (int i = 0; i < orderedHandles.Count; i++)
            {
                ApplyEquipmentToWeapon(
                    equipmentByHandle[orderedHandles[i]],
                    entry);
            }
        }

        private void ApplyEquipmentToWeapon(
            EquippedEquipment equipment,
            WeaponEntry entry)
        {
            if (equipment.AppliedModifiers.ContainsKey(entry))
            {
                return;
            }

            EquipmentModifierApplication[] definitions =
                equipment.Data.Levels[equipment.LevelIndex].Modifiers;
            var modifierHandles = new List<ModifierHandle>(definitions.Length);
            try
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    EquipmentModifierApplication application = definitions[i]
                        ?? throw new InvalidOperationException(
                            $"{equipment.Data.name} has a null modifier at level " +
                            $"{equipment.LevelIndex}, index {i}.");
                    if (!TargetsSlot(
                        application,
                        equipment.SourceSlotIndex,
                        entry.SlotIndex))
                    {
                        continue;
                    }

                    EquipmentDataModifier data = application.Modifier
                        ?? throw new InvalidOperationException(
                            $"{equipment.Data.name} has no native modifier definition " +
                            $"at level {equipment.LevelIndex}, index {i}.");
                    if (!entry.Data.Supports(data.ModifierId))
                    {
                        throw new InvalidOperationException(
                            $"Weapon '{entry.Data.name}' does not support modifier " +
                            $"0x{data.ModifierIdValue:X8} from " +
                            $"'{equipment.Data.name}'.");
                    }

                    RuntimeEquipmentModifier runtimeModifier =
                        data.CreateRuntime(factory);
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

                equipment.AppliedModifiers.Add(entry, modifierHandles);
                if (entry.Runtime.IsInitialized)
                {
                    entry.Runtime.RefreshStats();
                }
            }
            catch
            {
                RemoveModifierHandles(entry, modifierHandles);
                throw;
            }
        }

        private void DetachEquipmentFrom(WeaponEntry entry)
        {
            foreach (EquippedEquipment equipment in equipmentByHandle.Values)
            {
                if (equipment.AppliedModifiers.TryGetValue(
                    entry,
                    out List<ModifierHandle> handles))
                {
                    equipment.AppliedModifiers.Remove(entry);
                    RemoveModifierHandles(entry, handles);
                }
            }
        }

        private static void DetachEquipment(EquippedEquipment equipment)
        {
            var applied = new List<KeyValuePair<WeaponEntry, List<ModifierHandle>>>(
                equipment.AppliedModifiers);
            equipment.AppliedModifiers.Clear();
            for (int i = 0; i < applied.Count; i++)
            {
                RemoveModifierHandles(applied[i].Key, applied[i].Value);
            }
        }

        private static void RemoveModifierHandles(
            WeaponEntry entry,
            IReadOnlyList<ModifierHandle> handles)
        {
            for (int i = handles.Count - 1; i >= 0; i--)
            {
                entry.Modifiers.Remove(handles[i]);
            }

            if (entry.Runtime != null && entry.Runtime.IsInitialized)
            {
                entry.Runtime.RefreshStats();
            }
        }

        private static bool TargetsSlot(
            EquipmentModifierApplication application,
            int sourceSlotIndex,
            int targetSlotIndex)
        {
            if (!application.HasMultiSlotConfig)
            {
                return sourceSlotIndex == targetSlotIndex;
            }

            EquipmentMultiSlotConfig multiSlot = application.MultiSlot;
            if (sourceSlotIndex == targetSlotIndex)
            {
                return multiSlot != null && multiSlot.IsSelfApplied;
            }

            int distance = Math.Abs(targetSlotIndex - sourceSlotIndex);
            if (distance < 1 || distance > 3 || multiSlot == null)
            {
                return false;
            }

            EquipmentModifierSlots flag =
                (EquipmentModifierSlots)(1 << (distance - 1));
            return targetSlotIndex < sourceSlotIndex
                ? (multiSlot.LeftSlots & flag) != EquipmentModifierSlots.None
                : (multiSlot.RightSlots & flag) != EquipmentModifierSlots.None;
        }

        private int FindFirstAvailableWeaponSlot()
        {
            for (int i = 0; i < weaponSlots.Length; i++)
            {
                if (weaponSlots[i] == null)
                {
                    return i;
                }
            }

            throw new InvalidOperationException(
                $"All {HandSlotCount} PlayerBuildRuntime weapon slots are occupied.");
        }

        private static void ValidateSlotIndex(int slotIndex)
        {
            if ((uint)slotIndex >= HandSlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }
        }

		private void SubscribeToPresentation(WeaponBehaviour weapon)
		{
			if (!(weapon is HellMaidenProjectileAttackBehaviour projectile))
			{
				return;
			}

			projectile.PresentationSpawned += HandlePresentationSpawned;
			projectile.PresentationTerminated += HandlePresentationTerminated;
		}

		private void UnsubscribeFromPresentation(WeaponBehaviour weapon)
		{
			if (!(weapon is HellMaidenProjectileAttackBehaviour projectile))
			{
				return;
			}

			projectile.PresentationSpawned -= HandlePresentationSpawned;
			projectile.PresentationTerminated -= HandlePresentationTerminated;
		}

		private void HandlePresentationSpawned(
			ProjectilePresentationSpawn spawn)
		{
			ProjectilePresentationSpawned?.Invoke(spawn);
		}

		private void HandlePresentationTerminated(
			ProjectilePresentationTermination termination)
		{
			ProjectilePresentationTerminated?.Invoke(termination);
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
                WeaponData data,
                RuntimeEquipmentModifiers modifiers,
                int slotIndex)
            {
                Behaviour = behaviour;
                Runtime = runtime;
                Data = data;
                Modifiers = modifiers;
                SlotIndex = slotIndex;
            }

            public WeaponBehaviour Behaviour { get; }
            public WeaponRuntimeBehaviour Runtime { get; }
            public WeaponData Data { get; }
            public RuntimeEquipmentModifiers Modifiers { get; }
            public int SlotIndex { get; }
        }

        private sealed class EquippedEquipment
        {
            public EquippedEquipment(
                EquipmentData data,
                int levelIndex,
                int sourceSlotIndex)
            {
                Data = data;
                LevelIndex = levelIndex;
                SourceSlotIndex = sourceSlotIndex;
            }

            public EquipmentData Data { get; }
            public int LevelIndex { get; }
            public int SourceSlotIndex { get; }
            public Dictionary<WeaponEntry, List<ModifierHandle>> AppliedModifiers
                { get; } = new Dictionary<WeaponEntry, List<ModifierHandle>>();
        }

        private sealed class EquippedPerk
        {
            public EquippedPerk(PerkData data, PerkRarity rarity)
            {
                Data = data;
                Rarity = rarity;
            }

            public PerkData Data { get; }
            public PerkRarity Rarity { get; }
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
