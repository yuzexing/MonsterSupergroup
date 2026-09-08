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
        private bool weaponExecutionEnabled = true;

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

        public WeaponBehaviour GetWeaponAtSlot(int slotIndex)
        {
            ValidateSlotIndex(slotIndex);
            return weaponSlots[slotIndex]?.Behaviour;
        }

        public IReadOnlyList<PlayerBuildEquipmentState> GetEquipmentStates()
        {
            var handles = new List<long>(equipmentByHandle.Keys);
            handles.Sort();
            var states = new PlayerBuildEquipmentState[handles.Count];
            for (int i = 0; i < handles.Count; i++)
            {
                EquippedEquipment equipment = equipmentByHandle[handles[i]];
                states[i] = new PlayerBuildEquipmentState(
                    new PlayerBuildEquipmentHandle(handles[i]), equipment.Data,
                    equipment.LevelIndex, equipment.SourceSlotIndex);
            }
            return Array.AsReadOnly(states);
        }

        public void SetWeaponExecutionEnabled(bool value)
        {
            weaponExecutionEnabled = value;
            foreach (WeaponEntry entry in weapons.Values)
                entry.Behaviour.enabled = value;
        }

        public PlayerBuildSnapshot CaptureState()
        {
            var snapshot = new PlayerBuildSnapshot { InitialWeaponId = initialWeaponId };
            var weaponStates = new List<PlayerBuildWeaponSnapshot>();
            for (int slot = 0; slot < HandSlotCount; slot++)
            {
                WeaponEntry entry = weaponSlots[slot];
                if (entry == null) continue;
                weaponStates.Add(new PlayerBuildWeaponSnapshot { SlotIndex = slot, WeaponId = entry.Data.ID });
                if (entry.Behaviour == InitialWeapon) snapshot.InitialWeaponSlot = slot;
            }
            snapshot.Weapons = weaponStates.ToArray();
            IReadOnlyList<PlayerBuildEquipmentState> equipment = GetEquipmentStates();
            snapshot.Equipment = new PlayerBuildEquipmentSnapshot[equipment.Count];
            for (int i = 0; i < equipment.Count; i++)
                snapshot.Equipment[i] = new PlayerBuildEquipmentSnapshot
                {
                    SlotIndex = equipment[i].SourceSlotIndex,
                    EquipmentId = equipment[i].EquipmentId,
                    LevelIndex = equipment[i].LevelIndex
                };
            var perkHandles = new List<long>(perksByHandle.Keys);
            perkHandles.Sort();
            snapshot.Perks = new PlayerBuildPerkSnapshot[perkHandles.Count];
            for (int i = 0; i < perkHandles.Count; i++)
            {
                EquippedPerk perk = perksByHandle[perkHandles[i]];
                snapshot.Perks[i] = new PlayerBuildPerkSnapshot { PerkId = perk.Data.ID, Rarity = perk.Rarity };
            }
            return snapshot;
        }

        /// <summary>Restore a detached participant through the same equip/add APIs as a live build.</summary>
        public void RestoreState(RuntimeDB database, PlayerBuildSnapshot snapshot)
        {
            ValidateSnapshot(database, snapshot);
            EnsureInitialized();
            ClearBuild();
            try { ReconcileState(database, snapshot); }
            catch
            {
                ClearBuild();
                throw;
            }
        }

        /// <summary>Apply an owner baseline without restarting unchanged weapons or their active attacks.</summary>
        public void ReconcileState(RuntimeDB database, PlayerBuildSnapshot snapshot)
        {
            ValidateSnapshot(database, snapshot);
            EnsureInitialized();
            BuildDatabase = database;
            initialWeaponId = snapshot.InitialWeaponId;
            for (int slot = 0; slot < HandSlotCount; slot++)
            {
                uint desiredId = 0;
                foreach (PlayerBuildWeaponSnapshot weapon in snapshot.Weapons)
                    if (weapon.SlotIndex == slot) desiredId = weapon.WeaponId;
                WeaponBehaviour current = GetWeaponAtSlot(slot);
                if (current != null && current.WeaponData.ID == desiredId) continue;
                if (current != null) UnequipWeapon(current);
                if (desiredId != 0) EquipWeaponAtSlot(slot, database.GetWeaponData(desiredId));
            }
            InitialWeapon = snapshot.InitialWeaponSlot < 0 ? null : GetWeaponAtSlot(snapshot.InitialWeaponSlot);

            // Match each occurrence once: two identical cards remain two cards, never one or three.
            var retainedEquipment = new bool[snapshot.Equipment.Length];
            foreach (PlayerBuildEquipmentState current in GetEquipmentStates())
            {
                int match = -1;
                for (int i = 0; i < snapshot.Equipment.Length; i++)
                {
                    PlayerBuildEquipmentSnapshot desired = snapshot.Equipment[i];
                    if (!retainedEquipment[i] && current.SourceSlotIndex == desired.SlotIndex &&
                        current.EquipmentId == desired.EquipmentId && current.LevelIndex == desired.LevelIndex)
                    { match = i; break; }
                }
                if (match < 0) RemoveEquipment(current.Handle);
                else retainedEquipment[match] = true;
            }
            for (int i = 0; i < snapshot.Equipment.Length; i++)
                if (!retainedEquipment[i])
                {
                    PlayerBuildEquipmentSnapshot desired = snapshot.Equipment[i];
                    AddEquipment(desired.SlotIndex, ResolveEquipment(database, desired.EquipmentId), desired.LevelIndex);
                }

            var retainedPerks = new bool[snapshot.Perks.Length];
            foreach (long handle in new List<long>(perksByHandle.Keys))
            {
                EquippedPerk current = perksByHandle[handle];
                int match = -1;
                for (int i = 0; i < snapshot.Perks.Length; i++)
                    if (!retainedPerks[i] && current.Data.ID == snapshot.Perks[i].PerkId &&
                        current.Rarity == snapshot.Perks[i].Rarity) { match = i; break; }
                if (match < 0) RemovePerk(new PlayerBuildPerkHandle(handle));
                else retainedPerks[match] = true;
            }
            for (int i = 0; i < snapshot.Perks.Length; i++)
                if (!retainedPerks[i]) AddPerk(ResolvePerk(database, snapshot.Perks[i].PerkId), snapshot.Perks[i].Rarity);
        }

        private static void ValidateSnapshot(RuntimeDB database, PlayerBuildSnapshot snapshot)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Weapons == null || snapshot.Equipment == null || snapshot.Perks == null)
                throw new ArgumentException("Build snapshot collections cannot be null.", nameof(snapshot));
            var slots = new HashSet<int>();
            foreach (PlayerBuildWeaponSnapshot weapon in snapshot.Weapons)
            {
                ValidateSlotIndex(weapon.SlotIndex);
                if (!slots.Add(weapon.SlotIndex)) throw new ArgumentException("Build snapshot repeats a weapon slot.");
                database.GetWeaponData(weapon.WeaponId).ValidateNativeGas();
            }
            if (snapshot.InitialWeaponSlot != -1 && !slots.Contains(snapshot.InitialWeaponSlot))
                throw new ArgumentException("The initial weapon slot is missing from the build snapshot.");
            var counts = new int[HandSlotCount];
            foreach (PlayerBuildEquipmentSnapshot equipment in snapshot.Equipment)
            {
                ValidateSlotIndex(equipment.SlotIndex);
                if (++counts[equipment.SlotIndex] > MaxEquipmentPerSlot)
                    throw new ArgumentException("Build snapshot exceeds equipment slot capacity.");
                EquipmentData data = ResolveEquipment(database, equipment.EquipmentId);
                if (data.Levels == null || (uint)equipment.LevelIndex >= data.Levels.Length ||
                    data.Levels[equipment.LevelIndex] == null)
                    throw new ArgumentException($"Equipment {equipment.EquipmentId} has no level {equipment.LevelIndex}.");
            }
            foreach (PlayerBuildPerkSnapshot perk in snapshot.Perks)
            {
                PerkData data = ResolvePerk(database, perk.PerkId);
                data.ValidateNativeGas();
                if (!data.HasRarity(perk.Rarity))
                    throw new ArgumentException($"Perk {perk.PerkId} has no {perk.Rarity} definition.");
                foreach (PerkModifierApplication application in data.GetRarity(perk.Rarity).Modifiers)
                    if (application.Domain != PerkApplicationDomain.WeaponStats)
                        throw new NotSupportedException($"Perk {perk.PerkId} uses an unsupported runtime domain.");
            }
        }

        private static EquipmentData ResolveEquipment(RuntimeDB database, uint id)
        {
            if (database.EquipmentDB?.Equipments != null)
                foreach (EquipmentData equipment in database.EquipmentDB.Equipments)
                    if (equipment != null && equipment.ID == id) return equipment;
            throw new InvalidOperationException($"EquipmentDB cannot resolve card {id}.");
        }

        private static PerkData ResolvePerk(RuntimeDB database, uint id)
        {
            if (database.PerkDB?.Perks != null)
                foreach (PerkData perk in database.PerkDB.Perks)
                    if (perk != null && perk.ID == id) return perk;
            throw new InvalidOperationException($"PerkDB cannot resolve perk {id}.");
        }

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
                behaviour.enabled = weaponExecutionEnabled;
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

        public PlayerBuildEquipmentHandle UpgradeEquipment(
            PlayerBuildEquipmentHandle handle, int nextLevel)
        {
            EnsureInitialized();
            if (!handle.IsValid ||
                !equipmentByHandle.TryGetValue(handle.Value, out EquippedEquipment previous))
                throw new ArgumentException("Equipment is not owned by this build.", nameof(handle));
            if (nextLevel != previous.LevelIndex + 1 || previous.Data.Levels == null ||
                (uint)nextLevel >= previous.Data.Levels.Length || previous.Data.Levels[nextLevel] == null)
                throw new ArgumentOutOfRangeException(nameof(nextLevel),
                    "An equipment upgrade must select the next authored level.");

            var replacement = new EquippedEquipment(
                previous.Data, nextLevel, previous.SourceSlotIndex);
            try
            {
                // Stage the replacement before consuming the original. A factory or
                // targeting failure leaves its handle, level and effects intact.
                ApplyEquipment(replacement);
            }
            catch
            {
                DetachEquipment(replacement);
                throw;
            }
            DetachEquipment(previous);
            equipmentByHandle.Remove(handle.Value);
            var newHandle = new PlayerBuildEquipmentHandle(nextEquipmentHandle++);
            equipmentByHandle.Add(newHandle.Value, replacement);
            return newHandle;
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

    public readonly struct PlayerBuildEquipmentState
    {
        internal PlayerBuildEquipmentState(PlayerBuildEquipmentHandle handle,
            EquipmentData equipment, int levelIndex, int sourceSlotIndex)
        {
            Handle = handle;
            Equipment = equipment;
            LevelIndex = levelIndex;
            SourceSlotIndex = sourceSlotIndex;
        }

        public PlayerBuildEquipmentHandle Handle { get; }
        public EquipmentData Equipment { get; }
        public uint EquipmentId => Equipment.ID;
        public int LevelIndex { get; }
        public int SourceSlotIndex { get; }
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
