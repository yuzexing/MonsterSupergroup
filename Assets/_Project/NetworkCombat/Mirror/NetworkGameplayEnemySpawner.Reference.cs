using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player;
using AstralShift.Managers;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkGameplayEnemySpawner
    {
        private int[] referenceAlive;
        private System.Random referenceRandom;
        private readonly Dictionary<int, Vector2> formationCenters = new Dictionary<int, Vector2>();
        private StreamWriter referenceTrace;
        private bool referenceCompletionPublished;
        private double referenceNow;
        private readonly Dictionary<uint, EnemyBirthParameters> referenceObserved = new Dictionary<uint, EnemyBirthParameters>();
        private readonly HashSet<uint> referenceAliveIds = new HashSet<uint>();
        private readonly List<uint> removedReferenceIds = new List<uint>();
        private PauseManager referenceUpgradePause;
        public event Action ReferenceStageCompleted;
        public string ReferenceTracePath { get; private set; }
        public ReferenceWaveProgram ReferenceProgram => settings?.Reference;

        private void BeginReferenceStage()
        {
            NetworkEnemySimulationWorld.Instance.BeginReferenceClock();
            world.BeginReferenceTraps(waveRules);
            referenceAlive = new int[settings.Reference.Clips.Length];
            referenceRandom = new System.Random(settings.Reference.Seed);
            formationCenters.Clear(); referenceCompletionPublished = false;
            referenceNow = NetworkTime.time;
            referenceObserved.Clear();
            offscreenSince.Clear();
            schedule.ReferenceBarrierDecision += (index, result, before, after, roll) =>
                referenceTrace?.WriteLine(FormattableString.Invariant($"{schedule.State.Elapsed:R},barrier-decision,{index},Barrier,,,,0,{result}:chance={before:R}:next={after:R}:roll={roll:R},,,,,,,,"));
            schedule.FormationSkipped += i => referenceTrace?.WriteLine(FormattableString.Invariant($"{schedule.State.Elapsed:R},formation-skipped,{i},{settings.Reference.Clips[i].SourceEnemy},{settings.Reference.Clips[i].Variant},{settings.Reference.Clips[i].Count},,0,Occupied,,,,,,,,"));
            schedule.FormationCaptured += i => {
                if (activeParticipants.Count == 0) return;
                formationCenters[i] = NetworkServer.spawned[NextTarget().AvatarId].transform.position;
                world.CaptureReferenceFormation(i, formationCenters[i], settings.Reference.Clips[i].Count,
                    settings.Reference, schedule.FormationReleaseTime(i));
            };
            schedule.ReferenceClipEnded += i => {
                if (settings.Reference.Clips[i].Mode == ReferenceSpawnMode.FormationBurst) world.FinishReferenceFormation(i);
            };
            schedule.FormationReleased += i => world.ReleaseReferenceFormation(i);
            string directory = LimboReferenceLaunch.OutputDirectory;
            Directory.CreateDirectory(directory);
            ReferenceTracePath = Path.Combine(directory, "spawns-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".csv");
            referenceTrace = new StreamWriter(ReferenceTracePath) { AutoFlush = true };
            referenceTrace.WriteLine("elapsed,event,clip,source,variant,planned,sequence,enemy,result,totalAlive,countedAlive,hp,damage,speed,xp,x,y");
            Debug.Log($"[Limbo] trace={ReferenceTracePath} seed={settings.Reference.Seed} previewEnd={settings.Reference.EndTime:R} sourceDuration={settings.Reference.SourceDuration:R}");
        }

        private void UpdateReferenceStage(double now)
        {
            if (schedule.State.Phase == WavePhase.Stopped || schedule.State.Phase == WavePhase.Completed) return;
            CollectActiveParticipants();
            CountReferenceEnemies(out int total, out int counted);
            bool selecting = NetworkServer.connections.Count == 1 && NetworkClient.localPlayer != null && activeParticipants.Count == 1 &&
                activeParticipants[0].AvatarId == NetworkClient.localPlayer.netId &&
                NetworkClient.localPlayer.GetComponent<PlayerMovement>() is { IsUpgradeSelectionLocked: true };
            if (selecting && referenceUpgradePause == null && PauseManager.Instance != null)
            { referenceUpgradePause = PauseManager.Instance; referenceUpgradePause.PauseGame(); }
            else if (!selecting) ReleaseReferenceUpgradePause();
            bool active = activeParticipants.Count > 0 && !selecting && Time.timeScale > 0;
            if (active) { UpdateReferenceReposition(); CountReferenceEnemies(out total, out counted); }
            referenceNow += Time.deltaTime;
            while (schedule.TickReference(referenceNow, Time.frameCount, active, total, counted, referenceAlive, out var opportunity))
            {
                var clip = settings.Reference.Clips[opportunity.ClipIndex];
                var target = NextTarget();
                var prefab = settings.Prefabs[opportunity.PrefabIndex];
                bool legal;
                Vector2 position;
                if (clip.Mode == ReferenceSpawnMode.FormationBurst)
                {
                    var center = formationCenters[opportunity.ClipIndex];
                    float angle = 2 * Mathf.PI * opportunity.FormationIndex / clip.Count;
                    position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) / settings.Reference.BurstAspect) * settings.Reference.BurstRadius;
                    legal = true; // Source B has no obstacle or global-alive check.
                }
                else legal = TryReferencePosition(target, prefab, settings.Reference.OffscreenDistance, out position);
                uint id = 0;
                var stats = clip.Stats;
                var birth = new EnemyBirthParameters {
                    Enabled = true, SourceEnemy = clip.SourceEnemy, Variant = clip.Variant, ClipIndex = opportunity.ClipIndex,
                    Health = stats.BaseHealth, Damage = stats.BaseDamage, Speed = stats.BaseSpeed,
                    SpeedMultiplier = legal ? Mathf.Lerp(clip.SpeedMultipliers.x, clip.SpeedMultipliers.y, (float)referenceRandom.NextDouble()) : 1,
                    Xp = stats.BaseXP * settings.Reference.XpMultiplier(schedule.State.Elapsed),
                    Knockback = stats.KnockBackMultiplier, Stun = stats.StunTime, Wind = stats.WindMultiplier,
                    ContactRadius = clip.ContactRadius, BornAt = schedule.State.Elapsed,
                    ExpiresAt = clip.ExpiresOffscreen ? clip.End : 0, Counted = clip.Mode != ReferenceSpawnMode.FormationBurst,
                    ResetOnReposition = clip.ResetOnReposition
                };
                if (legal) id = SpawnEnemy(position, target.AvatarId, prefab, birth);
                schedule.Resolve(opportunity, id != 0);
                if (id != 0)
                {
                    total++; if (birth.Counted) counted++; referenceAlive[opportunity.ClipIndex]++;
                    referenceObserved[id] = birth;
                    WaveEnemySpawned?.Invoke(opportunity, id);
                }
                string result = id != 0 ? "Spawned" : legal ? "SpawnFailed" : "NoLegalPosition";
                referenceTrace?.WriteLine(FormattableString.Invariant($"{schedule.State.Elapsed:R},spawn,{opportunity.ClipIndex},{clip.SourceEnemy},{clip.Variant},{clip.Count},{opportunity.Sequence},{id},{result},{total},{counted},{birth.Health},{birth.Damage},{birth.Speed * birth.SpeedMultiplier:R},{birth.Xp:R},{position.x:R},{position.y:R}"));
                Debug.Log(FormattableString.Invariant($"[LimboSpawn] clip={opportunity.ClipIndex} event={opportunity.Sequence} enemy={id} source={clip.SourceEnemy}/{clip.Variant} result={result} hp={birth.Health} damage={birth.Damage} speed={birth.Speed * birth.SpeedMultiplier:F4} alive={total} counted={counted}"));
            }
            var trapSchedule = schedule;
            schedule.TickReferenceBarriers(Time.frameCount, () => (float)referenceRandom.NextDouble() * 100, i => {
                if (activeParticipants.Count == 0) return false;
                var target = NextTarget();
                return world.SpawnReferenceBarrier(NetworkServer.spawned[target.AvatarId].transform.position,
                    target.AvatarId, settings.Reference.Barriers[i], () => trapSchedule.ReleaseBarrierSlot());
            });
            world.TickReferenceTrapAuthority();
            if (schedule.State.Phase == WavePhase.Completed && !referenceCompletionPublished)
            {
                referenceCompletionPublished = true;
                referenceTrace?.WriteLine(FormattableString.Invariant($"{schedule.State.Elapsed:R},completed,,,,,,,,{total},{counted},,,,,,"));
                Debug.Log($"[Limbo] completed elapsed={schedule.State.Elapsed:F3} attempted={schedule.State.TotalAttempts} spawned={schedule.State.TotalSpawned} skipped={schedule.State.TotalSkipped} abandoned={schedule.State.AbandonedBudget} alive={total}");
                progress.Publish(this, schedule.State);
                ReferenceStageCompleted?.Invoke();
                if (NetworkManager.singleton is BootGameplayNetworkManager manager)
                    manager.CompleteReferenceStage(settings.Reference.EndTime < 720 ? $"Limbo 参考预览完成（0–{settings.Reference.EndTime:0.##} 秒）" : "Limbo 转场点已到达；Minos 战未实现");
                EndReferenceTrace();
            }
        }

        private void CountReferenceEnemies(out int total, out int counted)
        {
            total = counted = 0; Array.Clear(referenceAlive, 0, referenceAlive.Length);
            referenceAliveIds.Clear();
            var ledger = NetworkCombatWorld.Instance.Gateway.Ledger;
            foreach (var pair in NetworkServer.spawned)
            {
                var identity = pair.Value;
                if (identity == null || identity.gameObject.scene != gameObject.scene ||
                    !identity.TryGetComponent<NetworkEnemySimulationAgent>(out var agent) ||
                    !ledger.TryGetState(pair.Key, out var state) || !state.Alive) continue;
                total++;
                var birth = agent.Birth;
                if (birth.Enabled) { referenceAliveIds.Add(pair.Key); referenceObserved[pair.Key] = birth; }
                if (!birth.Enabled || birth.Counted) counted++;
                if (birth.Enabled && birth.ClipIndex >= 0 && birth.ClipIndex < referenceAlive.Length) referenceAlive[birth.ClipIndex]++;
            }
            // Telemetry only; all alive decisions above are derived from the canonical ledger.
            removedReferenceIds.Clear();
            foreach (var pair in referenceObserved)
                if (!referenceAliveIds.Contains(pair.Key))
                {
                    var b = pair.Value;
                    referenceTrace?.WriteLine(FormattableString.Invariant($"{schedule.State.Elapsed:R},death,{b.ClipIndex},{b.SourceEnemy},{b.Variant},,,{pair.Key},Removed,{total},{counted},,,,,,"));
                    removedReferenceIds.Add(pair.Key);
                }
            foreach (uint id in removedReferenceIds) { referenceObserved.Remove(id); offscreenSince.Remove(id); }
        }

        private Bounds ReferenceView(RunParticipant target)
        {
            var rig = FindFirstObjectByType<GameplayCameraRig>();
            Camera camera = rig != null ? rig.GameCamera : Camera.main;
            Vector3 center = NetworkServer.spawned[target.AvatarId].transform.position;
            if (camera != null)
            {
                var view = GameplayCameraGeometry.ViewBounds(camera);
                if (NetworkClient.localPlayer != null && target.AvatarId == NetworkClient.localPlayer.netId) return view;
                if (rig != null) return rig.ReferenceFollowView(target.AvatarId, center, groundBounds);
                view.center = center; return GameplayCameraGeometry.ClampView(view, groundBounds);
            }
            // Server without a presentation camera: fixed reference viewport, not a second camera system.
            return GameplayCameraGeometry.ClampView(new Bounds(center, new Vector3(24 * (16f / 9), 24, 0)), groundBounds);
        }

        private bool TryReferencePosition(RunParticipant target, GameObject prefab, float margin, out Vector2 position, Vector2 forward = default)
        {
            var view = ReferenceView(target);
            var controller = prefab.GetComponent<EnemyController>();
            var sprite = controller.GetComponentInChildren<SpriteRenderer>(true);
            Vector2 padding = sprite != null ? (Vector2)sprite.bounds.extents : Vector2.zero;
            Vector2 extents = (Vector2)view.extents + padding + Vector2.one * margin;
            for (int attempt = 0; attempt < settings.Attempts * 2; attempt++)
            {
                float angle = (float)(referenceRandom.NextDouble() * Mathf.PI * 2);
                if (attempt < settings.Attempts && forward.sqrMagnitude > .01f) angle = Mathf.Atan2(forward.y, forward.x) + ((float)referenceRandom.NextDouble() - .5f) * Mathf.PI / 2;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var edge = attempt < settings.Attempts ? extents : (Vector2)view.extents + Vector2.one * margin;
                position = (Vector2)view.center + (Mathf.Abs(direction.x) > Mathf.Abs(direction.y)
                    ? new Vector2(Mathf.Sign(direction.x) * edge.x, direction.y * edge.y)
                    : new Vector2(direction.x * edge.x, Mathf.Sign(direction.y) * edge.y));
                if (IsLegalReferencePosition(target, prefab, position)) return true;
            }
            position = default; return false;
        }

        private bool IsLegalReferencePosition(RunParticipant target, GameObject prefab, Vector2 position)
        {
            var footprint = spawnFootprints[prefab];
            var center = position + footprint.offset;
            if (center.x - footprint.radius < groundBounds.min.x || center.x + footprint.radius > groundBounds.max.x ||
                center.y - footprint.radius < groundBounds.min.y || center.y + footprint.radius > groundBounds.max.y) return false;
            var map = GameplayMapContext.For(gameObject);
            return map == null || (map.IsFree(position, footprint.radius, footprint.offset) &&
                (prefab.GetComponent<EnemyController>().enemyFlyingType || map.IsReachable(center, NetworkServer.spawned[target.AvatarId].transform.position)));
        }

        private void ReleaseReferenceUpgradePause()
        {
            if (referenceUpgradePause == null) return;
            referenceUpgradePause.ResumeGame(); referenceUpgradePause = null;
        }
        private void EndReferenceTrace()
        {
            ReleaseReferenceUpgradePause();
            if (settings?.Reference != null && world != null) world.ClearReferenceTraps();
            schedule?.CancelReferenceTraps();
            referenceTrace?.Dispose(); referenceTrace = null;
        }
    }
}
