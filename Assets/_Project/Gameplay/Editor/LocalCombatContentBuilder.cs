using System;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Local;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Editor
{
    public static class LocalCombatContentBuilder
    {
        private const string MenuPath = "Tools/MonsterSupergroup/Gameplay/Rebuild Local Auto Combat";
        private const string ContentFolder = "Assets/_Project/Content/LocalCombat";
        private const string ProjectilePath = ContentFolder + "/LocalProjectile.prefab";
        private const string WeaponPrefabPath = ContentFolder + "/StarterProjectileWeapon.prefab";
        private const string WeaponDefinitionPath = ContentFolder + "/StarterProjectileWeapon.asset";
        private const string PlayerPath = ContentFolder + "/LocalPlayer.prefab";
        private const string EnemyPath = ContentFolder + "/LocalEnemy.prefab";
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";

        [MenuItem(MenuPath)]
        private static void RebuildFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                RebuildAndValidate();
            }
        }

        public static void RebuildAndValidate()
        {
            EnsureFolder("Assets/_Project/Content");
            EnsureFolder(ContentFolder);

            StraightProjectileBehaviour projectilePrefab = BuildProjectilePrefab();
            ProjectileAttackBehaviour weaponPrefab = BuildWeaponPrefab();
            WeaponDefinition weaponDefinition = BuildWeaponDefinition(weaponPrefab, projectilePrefab);
            PlayerLoader playerPrefab = BuildPlayerPrefab(weaponDefinition);
            CombatTeamBehaviour enemyPrefab = BuildEnemyPrefab();
            BuildGameplayScene(playerPrefab, enemyPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateGeneratedContent();
            Debug.Log("Local auto-combat content rebuilt and validated.");
        }

        public static void ValidateGeneratedContent()
        {
            Require(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponDefinitionPath) != null,
                "Starter weapon definition is missing.");
            Require(AssetDatabase.LoadAssetAtPath<PlayerLoader>(PlayerPath) != null,
                "Local player prefab is missing.");
            Require(AssetDatabase.LoadAssetAtPath<CombatTeamBehaviour>(EnemyPath) != null,
                "Local enemy prefab is missing.");

            Scene scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            Require(scene.IsValid(), "Gameplay scene could not be opened.");
            Require(UnityEngine.Object.FindFirstObjectByType<LocalGameplayBootstrap>() != null,
                "Gameplay scene is missing LocalGameplayBootstrap.");
            Require(GameObject.Find("NetworkManager") == null,
                "Gameplay scene still contains the legacy NetworkManager root.");
            Require(GameObject.Find("EnemySpawner") == null,
                "Gameplay scene still contains the legacy network EnemySpawner root.");
        }

        private static StraightProjectileBehaviour BuildProjectilePrefab()
        {
            var root = new GameObject("LocalProjectile");
            try
            {
                CopySprite("Assets/Prefab/Projectile.prefab", root);
                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f;
                body.useFullKinematicContacts = true;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                CircleCollider2D collider = root.AddComponent<CircleCollider2D>();
                collider.isTrigger = true;
                collider.radius = 0.5f;
                root.AddComponent<StraightProjectileBehaviour>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath);
                return prefab.GetComponent<StraightProjectileBehaviour>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static ProjectileAttackBehaviour BuildWeaponPrefab()
        {
            var root = new GameObject("StarterProjectileWeapon");
            try
            {
                WeaponRuntimeBehaviour runtime = root.AddComponent<WeaponRuntimeBehaviour>();
                runtime.InitializeOnAwake = false;
                root.AddComponent<ProjectileAttackBehaviour>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, WeaponPrefabPath);
                return prefab.GetComponent<ProjectileAttackBehaviour>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static WeaponDefinition BuildWeaponDefinition(
            ProjectileAttackBehaviour weaponPrefab,
            StraightProjectileBehaviour projectilePrefab)
        {
            WeaponDefinition definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponDefinitionPath);
            bool isNewAsset = definition == null;
            if (isNewAsset)
            {
                definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            }
            definition.Configure(
                1,
                new AttackStats
                {
                    damage = 10,
                    critMultiplier = 2f,
                    critRate = 0f,
                    speed = 1f,
                    size = 1f,
                    duration = 5f,
                    projectileCount = 1,
                    knockbackDistance = 0f,
                    damageType = DamageType.Projectile
                },
                weaponPrefab,
                projectilePrefab,
                newTargetRange: 30f,
                newProjectileSpeed: 10f,
                newProjectileHitCount: 1,
                newSpawnRadius: 0.5f);
            if (isNewAsset)
            {
                AssetDatabase.CreateAsset(definition, WeaponDefinitionPath);
            }
            else
            {
                EditorUtility.SetDirty(definition);
            }

            return definition;
        }

        private static PlayerLoader BuildPlayerPrefab(WeaponDefinition weaponDefinition)
        {
            var root = new GameObject("LocalPlayer");
            try
            {
                CopySprite("Assets/Prefab/Player.prefab", root);
                root.transform.localScale = Vector3.one * 0.2f;
                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                CircleCollider2D collider = root.AddComponent<CircleCollider2D>();
                collider.radius = 0.5f;

                CombatantBehaviour combatant = root.AddComponent<CombatantBehaviour>();
                StatusUpdateDriver statusDriver = root.AddComponent<StatusUpdateDriver>();
                statusDriver.Configure(combatant);
                CombatTeamBehaviour team = root.AddComponent<CombatTeamBehaviour>();
                team.Configure(CombatTeam.Player, combatant);
                NearestEnemyTargetProvider targetProvider = root.AddComponent<NearestEnemyTargetProvider>();
                targetProvider.Configure(team);

                Transform attacksRoot = new GameObject("Attacks").transform;
                attacksRoot.SetParent(root.transform, false);
                PlayerHandBehaviour hand = root.AddComponent<PlayerHandBehaviour>();
                hand.Configure(attacksRoot, targetProvider, team);
                root.AddComponent<LocalPlayerMovement>();
                PlayerLoader loader = root.AddComponent<PlayerLoader>();
                loader.Configure(hand, combatant, weaponDefinition);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
                return prefab.GetComponent<PlayerLoader>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static CombatTeamBehaviour BuildEnemyPrefab()
        {
            var root = new GameObject("LocalEnemy");
            try
            {
                CopySprite("Assets/Prefab/Enemy.prefab", root);
                root.transform.localScale = Vector3.one * 0.4f;
                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                CircleCollider2D collider = root.AddComponent<CircleCollider2D>();
                collider.radius = 0.5f;

                CombatantBehaviour combatant = root.AddComponent<CombatantBehaviour>();
                StatusUpdateDriver statusDriver = root.AddComponent<StatusUpdateDriver>();
                statusDriver.Configure(combatant);
                CombatTeamBehaviour team = root.AddComponent<CombatTeamBehaviour>();
                team.Configure(CombatTeam.Enemy, combatant);
                LocalEnemyChase chase = root.AddComponent<LocalEnemyChase>();
                LocalEnemyDeathBehaviour death = root.AddComponent<LocalEnemyDeathBehaviour>();
                death.Configure(combatant, chase, collider);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, EnemyPath);
                return prefab.GetComponent<CombatTeamBehaviour>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildGameplayScene(
            PlayerLoader playerPrefab,
            CombatTeamBehaviour enemyPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            DestroyRootIfPresent(scene, "NetworkManager");
            DestroyRootIfPresent(scene, "EnemySpawner");
            DestroyRootIfPresent(scene, "LocalGameplay");

            SpriteRenderer ground = GameObject.Find("Ground")?.GetComponent<SpriteRenderer>();
            Require(ground != null, "Gameplay scene requires a Ground SpriteRenderer.");

            var root = new GameObject("LocalGameplay");
            var playerSpawn = new GameObject("PlayerSpawn").transform;
            playerSpawn.SetParent(root.transform, false);
            playerSpawn.position = new Vector3(0f, 0f, -1f);
            var enemyContainer = new GameObject("Enemies").transform;
            enemyContainer.SetParent(root.transform, false);
            var spawnerObject = new GameObject("LocalEnemySpawner");
            spawnerObject.transform.SetParent(root.transform, false);
            LocalEnemySpawner spawner = spawnerObject.AddComponent<LocalEnemySpawner>();
            spawner.Configure(enemyPrefab, enemyContainer, ground);
            LocalGameplayBootstrap bootstrap = root.AddComponent<LocalGameplayBootstrap>();
            bootstrap.Configure(playerPrefab, playerSpawn, spawner);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, GameplayScenePath);
        }

        private static void CopySprite(string sourcePrefabPath, GameObject destination)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            SpriteRenderer sourceRenderer = source != null ? source.GetComponent<SpriteRenderer>() : null;
            SpriteRenderer targetRenderer = destination.AddComponent<SpriteRenderer>();
            if (sourceRenderer == null)
            {
                return;
            }

            destination.transform.localScale = source.transform.localScale;
            targetRenderer.sprite = sourceRenderer.sprite;
            targetRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
            targetRenderer.color = sourceRenderer.color;
            targetRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            targetRenderer.sortingOrder = sourceRenderer.sortingOrder;
        }

        private static void DestroyRootIfPresent(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    return;
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = path.Substring(0, path.LastIndexOf('/'));
            string folderName = path.Substring(path.LastIndexOf('/') + 1);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
