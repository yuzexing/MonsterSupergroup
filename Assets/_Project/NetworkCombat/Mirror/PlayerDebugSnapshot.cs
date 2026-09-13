using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public enum PlayerDebugSource { OwnerRuntime, ServerRecord, OfflineCheckpoint }

    /// <summary>A detached display sample. Contains no scene objects or executing runtimes.</summary>
    public sealed class PlayerDebugSnapshot
    {
        public ulong ParticipantId { get; internal set; }
        public uint AvatarId { get; internal set; }
        public bool IsSelf { get; internal set; }
        public PlayerDebugSource Source { get; internal set; }
        public CanonicalEntityState? Canonical { get; internal set; }
        public int? LocalHealth { get; internal set; }
        public int? Level { get; internal set; }
        public float? Experience { get; internal set; }
        public PlayerProgressionDebugState? Progression { get; internal set; }
        public PlayerUltimateSnapshot? Ultimate { get; internal set; }
        public double SampleTime { get; internal set; }
        public string Summary { get; internal set; }
        public string Details { get; internal set; }
    }

    /// <summary>Reads existing facts only. In particular, never calls Dash.Capture or cooldown refresh.</summary>
    public static class PlayerDebugSnapshotReader
    {
        private const string Waiting = "unavailable (waiting for synchronization)";
        private const string NotReady = "unavailable (runtime not ready)";
        private static readonly EnemyStatusID[] StatusIds = (EnemyStatusID[])Enum.GetValues(typeof(EnemyStatusID));

        public static PlayerDebugSnapshot ReadLive(NetworkIdentity identity, RunParticipant participant,
            NetworkCombatWorld world, bool serverView, uint selfId, double now)
        {
            var row = new PlayerDebugSnapshot
            {
                ParticipantId = participant?.Id ?? identity?.GetComponent<NetworkRunParticipant>()?.ParticipantId ?? 0,
                AvatarId = participant?.AvatarId ?? (identity != null ? identity.netId : 0),
                IsSelf = identity != null && identity.netId == selfId,
                Source = serverView ? PlayerDebugSource.ServerRecord : PlayerDebugSource.OwnerRuntime,
                SampleTime = now
            };
            var text = new StringBuilder();
            string header = $"P{row.ParticipantId} | ParticipantId: {(row.ParticipantId != 0 ? row.ParticipantId.ToString() : Waiting)} | Avatar netId: {row.AvatarId}\n{(identity != null ? identity.name : "Avatar pending")}" +
                (row.IsSelf ? " [self]" : "") + " | Connected";
            text.AppendLine(serverView ? "Source: server received records (player HP: OwnerFinal)" : "Source: owned runtime + confirmed replica");
            bool hasCanonical = false;
            CanonicalEntityState canonical = default;
            if (world != null)
                hasCanonical = serverView ? world.Gateway.Ledger.TryGetState(row.AvatarId, out canonical)
                    : world.Replica.TryGetEntity(row.AvatarId, out canonical);
            if (hasCanonical) row.Canonical = canonical;

            CombatantBehaviour combatant = identity != null ? identity.GetComponent<CombatantBehaviour>() : null;
            PlayerMovement player = identity != null ? identity.GetComponent<PlayerMovement>() : null;
            PlayerBuildRuntime build = identity != null ? identity.GetComponent<PlayerBuildRuntime>() : null;
            NetworkModifierSelection selection = identity != null ? identity.GetComponent<NetworkModifierSelection>() : null;
            bool combatReady = combatant != null && combatant.isActiveAndEnabled && combatant.IsInitialized;
            bool runtimeReady = player != null && player.IsRuntimeInitialized;
            bool buildReady = build != null && build.isActiveAndEnabled && build.IsBuildActive &&
                (serverView || selection != null && selection.HasOwnerBaseline);
            string hp = hasCanonical ? Health(canonical.Health, canonical.MaxHealth) : Waiting;
            string life = hasCanonical ? Life(canonical.Alive) : Waiting;
            text.AppendLine($"Confirmed HP: {hp} | {life} | v{(hasCanonical ? canonical.StateVersion.ToString() : "?")}");
            text.AppendLine($"Confirmed invulnerable: {(hasCanonical ? canonical.AbsoluteInvulnerable.ToString() : Waiting)}");
            if (!serverView)
            {
                if (combatReady)
                {
                    row.LocalHealth = combatant.CurrentHealth;
                    hp = Health(combatant.CurrentHealth, combatant.MaxHealth);
                    life = Life(combatant.IsAlive);
                    text.AppendLine($"Local HP: {hp} | {life} | Invulnerable: {combatant.IsInvulnerable}");
                    if (hasCanonical && (combatant.CurrentHealth != canonical.Health || combatant.MaxHealth != canonical.MaxHealth || combatant.IsAlive != canonical.Alive))
                        text.AppendLine("Local and confirmed health differ (owner report / correction may be in flight)");
                }
                else text.AppendLine("Local HP: " + NotReady);
            }

            string xp = Waiting;
            if (selection != null && selection.isActiveAndEnabled && (serverView || selection.HasOwnerBaseline))
            {
                row.Level = selection.Level; row.Experience = selection.Experience;
                xp = $"Lv {selection.Level} | XP {N(selection.Experience)}/{selection.ExperiencePerLevel}";
                if (selection.TryReadDebugState(serverView, out var progression)) row.Progression = progression;
            }
            text.AppendLine(xp);
            text.AppendLine(ProgressionText(row.Progression));
            text.AppendLine($"Selection lock: {(serverView ? (hasCanonical ? world.Gateway.Ledger.IsPlayerSelectingUpgrade(row.AvatarId).ToString() : Waiting) : runtimeReady ? player.IsUpgradeSelectionLocked.ToString() : NotReady)}");

            IReadOnlyList<StatusInstance> confirmedStatuses = null;
            if (hasCanonical)
                confirmedStatuses = serverView ? world.Gateway.Statuses.GetForTarget(row.AvatarId) : world.Replica.ReadStatuses(row.AvatarId);
            StatusController effective = !serverView && combatReady ? combatant.StatusController : null;
            string statusSummary = StatusText(confirmedStatuses, effective, now, text);

            string ultimateSummary = Waiting;
            var ultimate = identity != null ? identity.GetComponent<NetworkPlayerUltimate>() : null;
            if (ultimate != null && ultimate.TryReadDebugState(serverView, out var ultimateState))
            {
                row.Ultimate = ultimateState.State;
                ultimateSummary = UltimatePhase(ultimateState.State, now);
                text.AppendLine(UltimateText(ultimateState.State, now));
                text.AppendLine($"Ultimate revision: {ultimateState.Revision} | Execution enabled: {ultimateState.ExecutionEnabled}" +
                    (!serverView ? $" | Pending request: {ultimateState.PendingUse}" : ""));
            }
            else text.AppendLine("Ultimate: " + (ultimate == null || !ultimate.isActiveAndEnabled ? NotReady : Waiting));
            var dash = identity != null ? identity.GetComponent<NetworkPlayerDash>() : null;
            if (dash != null && dash.TryReadDebugState(serverView, out var dashState))
                text.AppendLine(DashText(dashState, now) + (!serverView ? $" | Pending uses: {dash.PendingOwnerUseCount}" : ""));
            else text.AppendLine("Dash: " + (dash == null || !dash.isActiveAndEnabled ? NotReady : Waiting));

            if (runtimeReady)
            {
                var stats = player.PlayerStats.currentStats;
                text.AppendLine($"Character runtime stats: Move {N(stats.moveSpeed)} | Dash distance {N(stats.dashDistance)}, speed {N(stats.dashSpeed)}, cooldown {N(stats.dashCooldown)}s");
                text.AppendLine($"Pickup radius {N(stats.pullArea)} | XP multiplier {N(stats.xpModifier)} | Flat damage reduction {N(stats.dmgReduction)}");
            }
            else text.AppendLine("Character runtime stats: " + NotReady);
            var attacks = identity != null ? identity.GetComponent<NetworkWeaponCombatAdapter>() : null;
            AppendBuild(text, buildReady ? build.CaptureState() : null, buildReady ? build.BuildDatabase : null,
                buildReady ? build : null, attacks, serverView, null, now);
            ushort? epoch = participant?.ConnectionEpoch;
            if (!epoch.HasValue && identity != null && identity.TryGetComponent(out MirrorNetworkCombatBridge bridge)) epoch = bridge.ConnectionEpoch;
            text.AppendLine($"ParticipantId: {(row.ParticipantId != 0 ? row.ParticipantId.ToString() : Waiting)} | Avatar netId: {row.AvatarId}");
            text.AppendLine($"Connection epoch: {(epoch.HasValue ? epoch.Value.ToString() : Waiting)}");
            text.AppendLine($"Health baseline: {hasCanonical} | Build ready: {buildReady}" +
                (!serverView ? $" | Owner build revision: {(selection != null && selection.HasOwnerBaseline ? selection.OwnerBuildRevision.ToString() : Waiting)}" : ""));
            if (serverView)
                text.AppendLine(attacks != null ? $"Attack rejections: {attacks.RejectedAttackCount} | Last: {attacks.LastAttackRejection}" : "Attack diagnostics: " + NotReady);
            row.Summary = $"{header}\nHP {hp} | {life} | {xp}\nUltimate {ultimateSummary} | Status: {statusSummary}";
            row.Details = text.ToString().TrimEnd();
            return row;
        }

        public static PlayerDebugSnapshot ReadOffline(RunParticipant participant, RuntimeDB database, int experienceRequired)
        {
            PlayerRuntimeCheckpoint saved = participant.Checkpoint;
            var row = new PlayerDebugSnapshot
            {
                ParticipantId = participant.Id, AvatarId = saved?.PreviousAvatarId ?? 0,
                Source = PlayerDebugSource.OfflineCheckpoint, SampleTime = saved?.CapturedAt ?? 0
            };
            if (saved == null)
            {
                row.Summary = $"P{participant.Id} | ParticipantId: {participant.Id} | Disconnected\nOffline checkpoint: unavailable";
                row.Details = "No checkpoint was captured. Previous runtime values are unavailable.";
                return row;
            }
            double now = saved.CapturedAt;
            row.Canonical = saved.Health.State;
            var text = new StringBuilder($"Source: offline checkpoint | Captured at {N(now)}s\nAll values and countdowns are frozen at capture time.\n");
            var health = saved.Health.State;
            text.AppendLine($"HP {Health(health.Health, health.MaxHealth)} | {Life(health.Alive)} | v{health.StateVersion} | Invulnerable: {health.AbsoluteInvulnerable}");
            string xp = "Progression: unavailable (not saved)";
            if (saved.Progression != null)
            {
                var progress = saved.Progression;
                row.Level = progress.Level; row.Experience = progress.Experience;
                row.Progression = new PlayerProgressionDebugState { PendingUpgradeCount = progress.PendingUpgradeCount,
                    Stage = progress.Stage, BuildRevision = progress.BuildRevision, BuildReady = saved.Build != null,
                    OfferedLevel = progress.Rewards.Length > 0 ? progress.Rewards[0].EarnedLevel : 0 };
                xp = $"Lv {progress.Level} | XP {N(progress.Experience)}/{(experienceRequired > 0 ? experienceRequired.ToString() : "?")}";
                text.AppendLine(xp);
                text.AppendLine(ProgressionText(row.Progression));
            }
            var statuses = new List<StatusInstance>();
            foreach (var status in saved.Statuses) if (!status.Removed) statuses.Add(status.ToStatusInstance());
            string statusSummary = StatusText(statuses, null, now, text);
            row.Ultimate = saved.Ultimate;
            string phase = saved.Ultimate.HasValue ? UltimatePhase(saved.Ultimate.Value, now) : "unavailable (not saved)";
            text.AppendLine(saved.Ultimate.HasValue ? UltimateText(saved.Ultimate.Value, now) : "Ultimate: " + phase);
            text.AppendLine(saved.Dash.HasValue ? DashText(saved.Dash.Value, now) : "Dash: unavailable (not saved)");
            AppendBuild(text, saved.Build, database, null, null, true, saved.WeaponCooldowns, now);
            text.AppendLine("Character/weapon runtime stats, live locks, input and attack diagnostics: unavailable (not saved)");
            text.AppendLine($"ParticipantId: {participant.Id} | Previous Avatar netId: {row.AvatarId} | Connection epoch: {participant.ConnectionEpoch}");
            row.Summary = $"P{participant.Id} | ParticipantId: {participant.Id} | Previous Avatar netId: {row.AvatarId}\nDisconnected [offline checkpoint at {N(now)}s]\nHP {Health(health.Health, health.MaxHealth)} | {Life(health.Alive)} | {xp}\nUltimate {phase} | Status: {statusSummary}";
            row.Details = text.ToString().TrimEnd();
            return row;
        }

        private static string ProgressionText(PlayerProgressionDebugState? value) => value.HasValue
            ? $"Pending rewards: {value.Value.PendingUpgradeCount} | Stage: {value.Value.Stage} | Reward level: {value.Value.OfferedLevel}\nBuild revision: {value.Value.BuildRevision}"
            : "Pending rewards / selection stage / build revision: " + Waiting;

        private static string StatusText(IReadOnlyList<StatusInstance> confirmed, StatusController effective, double now, StringBuilder details)
        {
            details.AppendLine("Status effects (confirmed records):");
            if (confirmed == null) details.AppendLine(Waiting);
            else if (confirmed.Count == 0) details.AppendLine("none");
            else
            {
                var sorted = new List<StatusInstance>(confirmed);
                sorted.Sort((a, b) => a.InstanceId.Value.CompareTo(b.InstanceId.Value));
                foreach (var instance in sorted) AppendStatusInstance(details, instance, now);
            }
            if (effective != null)
            {
                details.AppendLine("Status effects (local effective runtime):");
                if (effective.Count == 0) details.AppendLine("none");
                foreach (var id in StatusIds)
                    if (id != EnemyStatusID.None)
                        foreach (var instance in effective.GetInstances(id)) AppendStatusInstance(details, instance, effective.CurrentTime);
            }
            var summary = new StringBuilder();
            foreach (var id in StatusIds)
            {
                if (id == EnemyStatusID.None) continue;
                int canonicalCount = 0;
                double nextEnd = double.PositiveInfinity;
                if (confirmed != null)
                    foreach (var instance in confirmed)
                        if (instance.DefinitionId == id) { canonicalCount += instance.Stack; nextEnd = Math.Min(nextEnd, instance.EndTime); }
                int count = effective != null ? effective.GetStackCount(id) : canonicalCount;
                if (count == 0 && canonicalCount == 0) continue;
                if (summary.Length > 0) summary.Append(", ");
                summary.Append(id).Append(" x").Append(count);
                if (effective != null) summary.Append(" (confirmed ").Append(confirmed != null ? canonicalCount.ToString() : "?").Append(')');
                else if (!double.IsInfinity(nextEnd)) summary.Append(" (next ").Append(N(Math.Max(0, nextEnd - now))).Append("s)");
            }
            return summary.Length > 0 ? summary.ToString() : confirmed != null || effective != null ? "none" : Waiting;
        }

        private static void AppendStatusInstance(StringBuilder text, StatusInstance status, double now) =>
            text.AppendLine($"  {status.DefinitionId} x{status.Stack} | {N(Math.Max(0, status.EndTime - now))}s | instance {status.InstanceId.Value} | source P{status.SourcePlayerId}/E{status.SourceEntityId} | DOT {status.CompletedTicks}/{status.TotalTicks} | v{status.Version}");

        public static string UltimatePhase(PlayerUltimateSnapshot state, double now) =>
            now < state.ActiveUntil ? "Active" : state.HasCharge ? "Ready" : "Empty";

        private static string UltimateText(PlayerUltimateSnapshot state, double now) =>
            $"Ultimate: {UltimatePhase(state, now)} | Charge: {state.HasCharge} | Active {N(Math.Max(0, state.ActiveUntil - now))}s | Invulnerable {N(Math.Max(0, state.InvulnerableUntil - now))}s";

        public static string DashText(PlayerDashSnapshot state, double now)
        {
            int spent = 0;
            double next = double.PositiveInfinity;
            foreach (double deadline in state.RechargeReadyAt ?? Array.Empty<double>())
                if (deadline > now) { spent++; next = Math.Min(next, deadline); }
            return $"Dash: {Math.Max(0, state.MaxCharges - spent)}/{state.MaxCharges} | Next recharge {(double.IsInfinity(next) ? "none" : N(next - now) + "s")} | Next use {N(Math.Max(0, state.NextUseAt - now))}s";
        }

        private static void AppendBuild(StringBuilder text, PlayerBuildSnapshot snapshot, RuntimeDB database,
            PlayerBuildRuntime runtime, NetworkWeaponCombatAdapter attacks, bool serverView,
            PlayerWeaponCooldownSnapshot[] savedCooldowns, double now)
        {
            if (snapshot == null) { text.AppendLine("Weapons / Equipment / Perks: " + NotReady); return; }
            text.AppendLine("Weapons / Equipment (slots 1-4):");
            for (int slot = 0; slot < PlayerBuildRuntime.HandSlotCount; slot++)
            {
                uint weaponId = 0;
                foreach (var weapon in snapshot.Weapons) if (weapon.SlotIndex == slot) { weaponId = weapon.WeaponId; break; }
                text.Append($"  Slot {slot + 1}: ");
                if (weaponId == 0) text.AppendLine("empty");
                else
                {
                    text.Append(WeaponName(database, weaponId));
                    if (snapshot.InitialWeaponSlot == slot) text.Append(" [initial]");
                    WeaponBehaviour weapon = runtime != null ? runtime.GetWeaponAtSlot(slot) : null;
                    string cooldown = "unavailable (no server observation)";
                    if (weapon is DashAttackBehaviour) cooldown = "uses Dash charges";
                    else if (weapon != null && weapon.NativeRuntime != null && weapon.NativeRuntime.IsInitialized)
                    {
                        if (!serverView) cooldown = N(Math.Max(0, weapon.GetCooldown() - weapon.LastAttackElapsedTime)) + "s [owner]";
                        else if (attacks != null && attacks.TryReadDebugCooldown(slot, out var recorded)) cooldown = N(recorded.RemainingAt(now)) + "s [server observed]";
                    }
                    else if (savedCooldowns != null)
                        foreach (var saved in savedCooldowns)
                            if (saved.SlotIndex == slot && saved.WeaponId == weaponId && saved.IsValid) cooldown = N(saved.RemainingAt(now)) + "s [saved]";
                    text.AppendLine(" | Cooldown " + cooldown);
                    if (weapon != null && weapon.NativeRuntime != null && weapon.NativeRuntime.IsInitialized)
                    {
                        var stats = weapon.NativeRuntime.Stats;
                        text.AppendLine($"    GAS runtime: Damage {stats.DamageValue} | Speed x{N(stats.SpeedMultipliersProduct)} | Crit {N(stats.CritRate * 100)}% | Crit damage x{N(stats.CritDamageMultiplier)}");
                    }
                }
                int count = 0;
                foreach (var equipment in snapshot.Equipment)
                {
                    if (equipment.SlotIndex != slot) continue;
                    text.AppendLine($"    Equipment: {EquipmentName(database, equipment.EquipmentId)} Lv {equipment.LevelIndex + 1}");
                    count++;
                }
                if (count == 0) text.AppendLine("    Equipment: none");
            }
            text.AppendLine("Perks:");
            if (snapshot.Perks.Length == 0) text.AppendLine("  none");
            foreach (var perk in snapshot.Perks) text.AppendLine($"  {PerkName(database, perk.PerkId)} | {perk.Rarity}");
        }

        // Scan authored arrays instead of invoking database getters that lazily initialize gameplay caches.
        private static string WeaponName(RuntimeDB database, uint id)
        {
            if (database != null && database.WeaponDB != null && database.WeaponDB.Weapons != null)
                foreach (var definition in database.WeaponDB.Weapons)
                    if (definition != null && definition.ID == id) return ContentName(definition.GetTitle(), definition.name, id);
            return $"unknown weapon #{id}";
        }
        private static string EquipmentName(RuntimeDB database, uint id)
        {
            if (database != null && database.EquipmentDB != null && database.EquipmentDB.Equipments != null)
                foreach (var definition in database.EquipmentDB.Equipments)
                    if (definition != null && definition.ID == id) return ContentName(definition.GetTitle(), definition.name, id);
            return $"unknown equipment #{id}";
        }
        private static string PerkName(RuntimeDB database, uint id)
        {
            if (database != null && database.PerkDB != null && database.PerkDB.Perks != null)
                foreach (var definition in database.PerkDB.Perks)
                    if (definition != null && definition.ID == id) return ContentName(definition.GetTitle(), definition.name, id);
            return $"unknown perk #{id}";
        }
        private static string ContentName(string title, string assetName, uint id) => $"{(string.IsNullOrWhiteSpace(title) ? assetName : title)} #{id}";
        private static string Life(bool alive) => alive ? "Alive" : "Downed";
        private static string Health(int health, int maximum) => $"{health}/{maximum}";
        private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
