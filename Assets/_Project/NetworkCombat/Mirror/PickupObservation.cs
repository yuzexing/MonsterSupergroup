using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat
{
    // Explicit development fixture only. Production Full does not install this component.
    public sealed class PickupObservation : MonoBehaviour
    {
        private bool collect = true, paused;
        private float nextSample;
        private bool Drops => LimboReferenceLaunch.Profile == "pickup-drops";
        private PlayerMovement Owner => NetworkClient.localPlayer?.GetComponent<PlayerMovement>();
        private NetworkExperienceWorld World => NetworkExperienceWorld.Current;
        private IEnumerator Start()
        {
            Log("fixture", "Manual controls: injected drops/damage/full heal/pause/enemy spawn; not gameplay pressure evidence.");
            if (LimboReferenceLaunch.Argument("--limbo-pickup-case=") != "smoke") yield break;
            Log("test-assistance", "Explicit short smoke: damage 300, health drop, XP drop, shutdown. Not visual acceptance.");
            float deadline = Time.realtimeSinceStartup + 90;
            while ((Owner == null || World == null || !World.CanGrant(out _) ||
                    !Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline ||
                    Owner.GetComponent<NetworkModifierSelection>().IsSelecting) && Time.realtimeSinceStartup < deadline) yield return null;
            if (Owner == null || World == null || !World.CanGrant(out _)) { SmokeFailed("initialization"); yield break; }
            yield return new WaitForSecondsRealtime(.5f);
            if (Owner.CombatantBinding.ApplyDamage(300) != 300) { SmokeFailed("damage input"); yield break; }
            Owner.GetComponent<MirrorNetworkCombatBridge>().Flush();
            Spawn(PickupEffect.RestoreHealth);
            yield return new WaitForSecondsRealtime(.2f);
            if (Owner.CombatantBinding.CurrentHealth != 200) { SmokeFailed("healed before arrival"); yield break; }
            deadline = Time.realtimeSinceStartup + 15;
            while (World.HealthCount != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            if (World.HealthCount != 0 || Owner.CombatantBinding.CurrentHealth != 400) { SmokeFailed("health receipt"); yield break; }
            var selection = Owner.GetComponent<NetworkModifierSelection>();
            float before = selection.Experience;
            int beforeLevel = selection.Level;
            Spawn(PickupEffect.Experience);
            deadline = Time.realtimeSinceStartup + 15;
            while (World.UnclaimedCount != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            if (World.UnclaimedCount != 0 || selection.Experience <= before && selection.Level <= beforeLevel) { SmokeFailed("XP collection"); yield break; }
            Log("smoke-gameplay", "health 500->200->400; no heal at .2 seconds; XP collected through normal collector.");
            var manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            manager.StopHost();
            deadline = Time.realtimeSinceStartup + 15;
            while ((manager.IsGameplayLoaded || manager.IsGameplayTransitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            bool clean = !NetworkClient.active && !NetworkServer.active && FindObjectsByType<NetworkExperienceGem>(FindObjectsSortMode.None).Length == 0;
            Log(clean ? "smoke-passed" : "smoke-failed", "shutdown and pickup cleanup");
            Application.Quit(clean ? 0 : 1);
        }
        private void SmokeFailed(string reason) { Log("smoke-failed", reason); Debug.LogError("[PickupSmoke] " + reason); Application.Quit(1); }
        private void Log(string kind, string detail) => PickupAudit.Emit(kind, World?.RunId, 0, detail);
        private void Update()
        {
            if (Owner != null)
            {
                Owner.GetComponent<NetworkExperienceCollector>().enabled = collect;
                Owner.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(Drops);
            }
            if (World == null || Time.realtimeSinceStartup < nextSample) return;
            nextSample = Time.realtimeSinceStartup + 1;
            Log("pool-sample", $"entities={World.EntitiesCreated};entityHits={World.EntityPoolHits};visuals={World.VisualsCreated};visualHits={World.VisualPoolHits};health={World.HealthCount};claims={World.PendingHealthCount};heap={GC.GetTotalMemory(false)};frameMs={Time.unscaledDeltaTime * 1000}");
        }
        private void Spawn(PickupEffect effect)
        {
            if (!NetworkServer.active || Owner == null || World == null) return;
            Log("fixture-spawn", effect.ToString());
            typeof(NetworkExperienceWorld).GetMethod("SpawnPickup", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(World, new object[] { effect, effect == PickupEffect.Experience ? 5f : 200f,
                    (Vector2)Owner.transform.position + Vector2.right, Owner.gameObject.scene });
        }
        private void SpawnTargets()
        {
            if (!NetworkServer.active || Owner == null) return;
            var spawner = FindFirstObjectByType<NetworkGameplayEnemySpawner>();
            Log("fixture-enemies", "8 ordinary targets; real weapon kills and production drop RNG; no forced drop success.");
            for (int i = 0; i < 8; i++)
            {
                var instance = Instantiate(spawner.EnemyPrefab, Owner.transform.position + new Vector3(3 + i % 4, i / 4 - .5f), Quaternion.identity);
                SceneManager.MoveGameObjectToScene(instance, Owner.gameObject.scene);
                instance.GetComponent<NetworkEnemySimulationAgent>().ConfigureInitialServerTarget(NetworkClient.localPlayer.netId);
                NetworkServer.Spawn(instance);
            }
        }
        private void OnGUI()
        {
            if (Owner == null || World == null) return;
            GUILayout.BeginArea(new Rect(12, 12, 420, 320), GUI.skin.box);
            GUILayout.Label(Drops ? "DROP RULE OBSERVATION (assisted)" : "PICKUP OBSERVATION (assisted)");
            int bottles = NetworkServer.active ? World.HealthCount : NetworkExperienceGem.ClientGems.Count(x => x != null && x.Effect == PickupEffect.RestoreHealth);
            GUILayout.Label($"HP {Owner.CombatantBinding.CurrentHealth}/{Owner.CombatantBinding.MaximumHealth} | bottles {bottles}/4");
            if (GUILayout.Button("Damage self by 300 (test input)")) { Log("fixture-damage", "300"); Owner.CombatantBinding.ApplyDamage(300); }
            if (GUILayout.Button("Fill own health (test input)")) { Log("fixture-heal", "maximum"); Owner.CombatantBinding.RestoreHealth(Owner.CombatantBinding.MaximumHealth); }
            bool nextCollect = GUILayout.Toggle(collect, "Collect nearby items");
            if (nextCollect != collect) { collect = nextCollect; Log("fixture-collector", collect.ToString()); }
            if (NetworkServer.active)
            {
                if (GUILayout.Button("Create XP (5 base XP)")) Spawn(PickupEffect.Experience);
                if (GUILayout.Button("Create health bottle (200)")) Spawn(PickupEffect.RestoreHealth);
                if (Drops && GUILayout.Button("Spawn 8 enemies for ordinary kills")) SpawnTargets();
                if (GUILayout.Button(paused ? "Resume test pause" : "Pause combat (test input)"))
                { paused = !paused; Time.timeScale = paused ? 0 : 1; Log("fixture-pause", paused.ToString()); }
            }
            else GUILayout.Label("Host creates shared drops. This client uses the normal collection Command.");
            GUILayout.Label("XP grants at claim. Bottle heals after arrival. No auto screenshots.");
            GUILayout.EndArea();
        }
        private void OnDestroy() { if (paused) Time.timeScale = 1; }
    }
}
