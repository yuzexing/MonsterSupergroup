#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.Rendering;
using Mirror;
using MonsterSupergroup.Gameplay.Combat.Content;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Transient fixture only: never writes the user's Full.asset, custom Timeline or Prefabs.
    internal sealed class EnemyDefinitionRuntimeFixture : IDisposable
    {
        private readonly BootGameplayNetworkManager manager;
        private readonly bool resetOnReposition;
        private readonly List<UnityEngine.Object> assets = new();
        public readonly EnemyDefinition[] Definitions;
        public EnemyDefinitionRuntimeFixture(BootGameplayNetworkManager manager, bool resetOnReposition = true)
        {
            this.manager = manager;
            this.resetOnReposition = resetOnReposition;
            Definitions = manager.EnemyCatalog.Definitions.Where(d => d.Prefab.name == "ReferenceBrotchi")
                .OrderBy(d => d.Stats.Capture().Health).ToArray();
            Require(Definitions.Length == 2 && Definitions[0].Prefab == Definitions[1].Prefab, "Expected two definitions sharing the Brotchi Prefab.");
            SceneManager.sceneLoaded += ConfigureGameplay;
        }
        private void ConfigureGameplay(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != manager.GameplayScene) return;
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>(); assets.Add(timeline);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = 300;
            for (int i = 0; i < Definitions.Length; i++)
            {
                var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(null, Definitions[i].DisplayName); assets.Add(track);
                var clip = track.CreateClip<NetworkEnemySpawnClip>(); clip.start = 1 + i * .2; clip.duration = 1;
                var spawn = (NetworkEnemySpawnClip)clip.asset; assets.Add(spawn);
                Set(spawn, "enemy", Definitions[i]); Set(spawn, "authoringVersion", 1);
                spawn.referenceMode = ReferenceSpawnMode.CurveBudget; spawn.count = 1;
                spawn.spawnCurve = AnimationCurve.Constant(0, 1, 1); spawn.speedMultipliers = Vector2.one;
                spawn.contactRadius = 0; spawn.expiresOffscreen = false; spawn.resetOnReposition = resetOnReposition;
            }
            var rules = ScriptableObject.CreateInstance<GameplayWaveRules>(); assets.Add(rules);
            Set(rules, "timeline", timeline); Set(rules, "referenceStage", true);
            Set(rules, "referenceEndTime", 300d); Set(rules, "referenceSourceDuration", 300d);
            Set(rules, "referenceXpAmplitude", 0f); Set(rules, "positionAttempts", 100);
            foreach (var spawner in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)))
                spawner.ConfigureWaveRules(rules);
        }
        private static void Set(object target, string field, object value) =>
            (target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
             ?? throw new MissingFieldException(target.GetType().Name, field)).SetValue(target, value);
        public NetworkEnemySimulationAgent[] Agents() => NetworkClient.spawned.Values
            .Where(i => i != null).Select(i => i.GetComponent<NetworkEnemySimulationAgent>())
            .Where(a => a != null && Definitions.Any(d => d.Id == a.Birth.DefinitionId)).ToArray();
        public bool PairReady() => Agents().Length == 2 && Agents().All(a => a.ProductEnemyInitialized && a.GetComponent<EnemyController>().enemyAnimator.PaletteSwapper.Appearance != null);
        public bool PlaceInView()
        {
            if (!NetworkServer.active || NetworkClient.localPlayer == null) return false;
            var world = NetworkEnemySimulationWorld.Instance;
            var agents = Agents();
            if (agents.Length != 2 || agents.Any(a => !world.Registry.TryGetLatestSnapshot(a.netId, out _))) return false;
            for (int i = 0; i < agents.Length; i++)
                Require(world.RepositionReferenceEnemy(agents[i], (Vector2)NetworkClient.localPlayer.transform.position + new Vector2(4 + i * 2, 3)), "Fixture reposition failed.");
            return true;
        }
        public void Verify(NetworkEnemySimulationAgent agent, bool fresh)
        {
            var definition = Definitions.Single(d => d.Id == agent.Birth.DefinitionId);
            var expected = definition.Stats.Capture(); var enemy = agent.GetComponent<EnemyController>();
            var combatant = agent.GetComponent<CombatantBehaviour>();
            Require(combatant.MaxHealth == expected.Health && (!fresh || combatant.CurrentHealth == expected.Health), "Incorrect definition HP: " + definition.DisplayName);
            Require(enemy.stats.Damage == expected.Damage && Mathf.Abs(enemy.stats.Speed - expected.Speed) < .001f, "Incorrect definition damage/speed.");
            var swapper = enemy.enemyAnimator.PaletteSwapper;
            Require(swapper != null && swapper.Appearance == definition.Appearance, "Appearance resolved from wrong source.");
            var sprite = swapper.Renderer.sprite; Require(sprite != null, "Body sprite missing.");
            if (definition.Appearance.UsesPalette)
                Require(definition.Appearance.TextureMappings.Any(m => m.baked == sprite.texture), "Renderer is not using the definition's baked atlas.");
            Debug.Log($"[EnemyDefinitionFixture] id={agent.netId} definition={definition.IdText} name={definition.DisplayName} hp={combatant.CurrentHealth}/{combatant.MaxHealth} texture={sprite.texture.name}");
        }
        public static void Damage(uint target, int amount)
        {
            var owner = NetworkClient.localPlayer;
            var execution = new AstralShift.HellMaiden.Player.Attacks.LegacyCombatExecution(
                owner.GetComponent<CombatRuntimeServiceProvider>().Services);
            var context = execution.BeginAttack(6, CombatTags.Damage);
            // This definition-lifecycle fixture admits one explicit host-authored hit;
            // weapon fire-rate/geometry admission is covered by its own test suite.
            Require(NetworkServer.active, "Fixture damage is host-only.");
            var world = NetworkCombatWorld.Instance;
            var admission = world.Gateway.Attacks.Admit(owner.netId, context.SourceEntityId, 6,
                owner.GetComponent<NetworkModifierSelection>().BuildRevision, context.RootEventId.Value);
            Require(admission == CombatRejectionReason.None, "Fixture root admission: " + admission);
            var source = new AstralShift.HellMaiden.Player.Attacks.LegacyDamageSource(execution, context, 6);
            var victim = NetworkClient.spawned[target].GetComponent<EnemyController>();
            Debug.Log($"[EnemyDefinitionFixture] damage-before target={target} hp={victim.CurrentHealth} dead={victim.IsDead} immune={victim.IsImmune} amount={amount} context={context.EventId.Value}/{context.Sequence}");
            victim.Damage(amount,
                AstralShift.HellMaiden.Player.Attacks.DamageType.Normal, source);
            owner.GetComponent<MirrorNetworkCombatBridge>().Flush();
            Debug.Log($"[EnemyDefinitionFixture] damage-after target={target} hp={victim.CurrentHealth} dead={victim.IsDead} ledgerAlive={world.Gateway?.Ledger.IsAlive(target)}");
        }
        public static IEnumerator Wait(Func<bool> condition, string stage, float seconds = 45)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(condition(), "Timed out: " + stage);
        }
        public static void Require(bool valid, string message)
        { if (!valid) throw new InvalidOperationException(message); }
        public void Dispose()
        {
            SceneManager.sceneLoaded -= ConfigureGameplay;
            for (int i = assets.Count - 1; i >= 0; i--) if (assets[i] != null) UnityEngine.Object.Destroy(assets[i]);
            assets.Clear();
        }
    }
}
#endif
