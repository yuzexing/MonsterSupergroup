using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public enum MenuDataState { Ready, Synchronizing, ComponentUnavailable, Empty }

    public sealed class GameplayMenuSnapshot
    {
        public uint AvatarId { get; internal set; }
        public string CharacterName { get; internal set; }
        public MenuDataState HealthState { get; internal set; }
        public MenuDataState ProgressState { get; internal set; }
        public MenuDataState AttributesState { get; internal set; }
        public MenuDataState DashState { get; internal set; }
        public int Health { get; internal set; }
        public int MaxHealth { get; internal set; }
        public bool Alive { get; internal set; }
        public int Level { get; internal set; }
        public float Experience { get; internal set; }
        public float ExperienceRequired { get; internal set; }
        public float MoveSpeed { get; internal set; }
        public float DamageReduction { get; internal set; }
        public float PickupRange { get; internal set; }
        public float ExperienceMultiplier { get; internal set; }
        public float DashDistance { get; internal set; }
        public float DashSpeed { get; internal set; }
        public float DashInterval { get; internal set; }
        public int? DashAvailable { get; internal set; }
        public int? DashMaximum { get; internal set; }
        public IReadOnlyList<GameplayMenuWeaponSnapshot> Weapons { get; internal set; }
    }

    public sealed class GameplayMenuWeaponSnapshot
    {
        public int Slot { get; internal set; }
        public uint Id { get; internal set; }
        public string Name { get; internal set; }
        public Sprite Icon { get; internal set; }
        public string IconMark { get; internal set; }
        public MenuDataState State { get; internal set; }
        public bool Initial { get; internal set; }
        public bool UsesDash { get; internal set; }
        public float? Damage { get; internal set; }
        public float? AttackSpeed { get; internal set; }
        public float? CritRate { get; internal set; }
        public float? CritMultiplier { get; internal set; }
        public float? CooldownRemaining { get; internal set; }
        public IReadOnlyList<GameplayMenuEquipmentSnapshot> Equipment { get; internal set; }
    }

    public readonly struct GameplayMenuEquipmentSnapshot
    {
        public readonly uint Id;
        public readonly string Name;
        public readonly int Level;
        public GameplayMenuEquipmentSnapshot(uint id, string name, int level) { Id = id; Name = name; Level = level; }
    }

    /// <summary>Owner-only, detached reads. Never initializes a build or advances dash/cooldown queues.</summary>
    public static class GameplayMenuSnapshotReader
    {
        public static GameplayMenuSnapshot Read(NetworkIdentity owner, PreparationMenuCatalog catalog, double now)
        {
            if (owner != null && (!owner.isOwned || !owner.isActiveAndEnabled)) owner = null;
            var result = new GameplayMenuSnapshot { AvatarId = owner != null ? owner.netId : 0,
                CharacterName = catalog != null ? catalog.CharacterName : "角色",
                HealthState = MenuDataState.Synchronizing, ProgressState = MenuDataState.Synchronizing,
                AttributesState = MenuDataState.Synchronizing, DashState = MenuDataState.Synchronizing };
            var combatant = owner != null ? owner.GetComponent<CombatantBehaviour>() : null;
            var player = owner != null ? owner.GetComponent<PlayerMovement>() : null;
            var selection = owner != null ? owner.GetComponent<NetworkModifierSelection>() : null;
            var build = owner != null ? owner.GetComponent<PlayerBuildRuntime>() : null;
            if (owner != null)
            {
                result.HealthState = combatant != null ? MenuDataState.Synchronizing : MenuDataState.ComponentUnavailable;
                result.ProgressState = selection != null ? MenuDataState.Synchronizing : MenuDataState.ComponentUnavailable;
                result.AttributesState = player != null && player.PlayerStats != null ? MenuDataState.Synchronizing : MenuDataState.ComponentUnavailable;
            }
            if (combatant != null && combatant.isActiveAndEnabled && combatant.IsInitialized)
            {
                result.HealthState = MenuDataState.Ready; result.Health = combatant.CurrentHealth;
                result.MaxHealth = combatant.MaxHealth; result.Alive = combatant.IsAlive;
            }
            if (selection != null && selection.isActiveAndEnabled && selection.HasOwnerBaseline)
            {
                result.ProgressState = MenuDataState.Ready; result.Level = selection.Level;
                result.Experience = selection.Experience; result.ExperienceRequired = selection.ExperiencePerLevel;
            }
            if (player != null && player.isActiveAndEnabled && player.IsRuntimeInitialized && player.PlayerStats != null)
            {
                var stats = player.PlayerStats.currentStats;
                result.AttributesState = MenuDataState.Ready; result.MoveSpeed = stats.moveSpeed;
                result.DamageReduction = stats.dmgReduction; result.PickupRange = stats.pullArea;
                result.ExperienceMultiplier = stats.xpModifier; result.DashDistance = stats.dashDistance;
                result.DashSpeed = stats.dashSpeed; result.DashInterval = stats.dashCooldown;
            }
            var dash = owner != null ? owner.GetComponent<NetworkPlayerDash>() : null;
            if (owner != null && (dash == null || !dash.isActiveAndEnabled)) result.DashState = MenuDataState.ComponentUnavailable;
            if (dash != null && dash.TryReadDebugState(false, out var dashState))
            {
                result.DashState = MenuDataState.Ready;
                int pending = 0;
                if (dashState.RechargeReadyAt != null) foreach (double deadline in dashState.RechargeReadyAt) if (deadline > now) pending++;
                result.DashMaximum = dashState.MaxCharges;
                result.DashAvailable = Math.Max(0, dashState.MaxCharges - pending);
            }
            bool ready = build != null && build.isActiveAndEnabled && build.IsBuildActive && result.ProgressState == MenuDataState.Ready;
            PlayerBuildSnapshot state = ready ? build.CaptureState() : null;
            var database = ready ? build.BuildDatabase : null;
            var weapons = new List<GameplayMenuWeaponSnapshot>(PlayerBuildRuntime.HandSlotCount);
            for (int slot = 0; slot < PlayerBuildRuntime.HandSlotCount; slot++)
            {
                var row = new GameplayMenuWeaponSnapshot { Slot = slot, State = ready ? MenuDataState.Empty :
                    owner != null && build == null ? MenuDataState.ComponentUnavailable : MenuDataState.Synchronizing };
                var equipment = new List<GameplayMenuEquipmentSnapshot>();
                if (state != null)
                {
                    foreach (var item in state.Weapons) if (item.SlotIndex == slot) { row.Id = item.WeaponId; break; }
                    if (row.Id != 0)
                    {
                        row.State = MenuDataState.Ready; row.Initial = state.InitialWeaponSlot == slot;
                        row.Name = $"武器 #{row.Id}";
                        if (database != null && database.WeaponDB != null && database.WeaponDB.Weapons != null)
                            foreach (var item in database.WeaponDB.Weapons)
                                if (item != null && item.ID == row.Id && !string.IsNullOrWhiteSpace(item.GetTitle())) { row.Name = item.GetTitle(); break; }
                        row.Icon = catalog != null ? catalog.FindWeapon(row.Id)?.Icon : null;
                        row.IconMark = catalog != null ? catalog.FindWeapon(row.Id)?.IconMark : null;
                        var weapon = build.GetWeaponAtSlot(slot);
                        row.UsesDash = weapon is DashAttackBehaviour;
                        if (weapon != null && weapon.NativeRuntime != null && weapon.NativeRuntime.IsInitialized)
                        {
                            var stats = weapon.NativeRuntime.Stats;
                            row.Damage = stats.DamageValue; row.AttackSpeed = stats.SpeedMultipliersProduct;
                            row.CritRate = stats.CritRate; row.CritMultiplier = stats.CritDamageMultiplier;
                            if (!row.UsesDash) row.CooldownRemaining = Math.Max(0, weapon.GetCooldown() - weapon.LastAttackElapsedTime);
                        }
                    }
                    foreach (var item in state.Equipment)
                    {
                        if (item.SlotIndex != slot) continue;
                        string name = $"装备 #{item.EquipmentId}";
                        if (database != null && database.EquipmentDB != null && database.EquipmentDB.Equipments != null)
                            foreach (var definition in database.EquipmentDB.Equipments)
                                if (definition != null && definition.ID == item.EquipmentId && !string.IsNullOrWhiteSpace(definition.GetTitle())) { name = definition.GetTitle(); break; }
                        equipment.Add(new GameplayMenuEquipmentSnapshot(item.EquipmentId, name, item.LevelIndex + 1));
                    }
                }
                row.Equipment = equipment.AsReadOnly(); weapons.Add(row);
            }
            result.Weapons = weapons.AsReadOnly();
            return result;
        }
    }
}
