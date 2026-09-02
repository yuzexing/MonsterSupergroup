using System;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Interactions;
using AstralShift.QTI.Triggers;
using AstralShift.Rendering;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class EnemySimulationPrefabMigrator
    {
        private const string SkeletonWarningPrefabPath =
            "Assets/_Project/Content/NetworkCombat/Validation/HellMaiden/" +
            "GameObject/Skeleton_Warning.prefab";
        public const string SkeletonNetworkPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkEnemySkeleton.prefab";
        private const string SkeletonAnimationFolder =
            "Assets/_Project/Content/NetworkCombat/Validation/HellMaiden/" +
            "AnimationClip/";

        [MenuItem("Monster Supergroup/Network Combat/Migrate Enemy Simulation Prefabs")]
        public static void Migrate()
        {
            MigratePlayerPrefab();
            MigrateLightweightEnemyPrefab();
            GameObject productEnemy = MigrateProductEnemyPrefab();
            GameObject skeletonEnemy = MigrateSkeletonEnemyPrefab();
            MigrateWorldPrefab();
            MigrateSandboxScene(productEnemy, skeletonEnemy);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void MigrateBatch()
        {
            try
            {
                Migrate();
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }

                throw;
            }
        }

        private static void MigratePlayerPrefab()
        {
            GameObject root = LoadPrefab(NetworkCombatSetupUtility.PlayerPrefabPath);
            try
            {
                GetOrAdd<NetworkEnemySimulationEndpoint>(root);
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    NetworkCombatSetupUtility.PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void MigrateLightweightEnemyPrefab()
        {
            GameObject root = LoadPrefab(NetworkCombatSetupUtility.EnemyPrefabPath);
            try
            {
                ConfigureNetworkEnemy(root);
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    NetworkCombatSetupUtility.EnemyPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject MigrateProductEnemyPrefab()
        {
            GameObject root = LoadPrefab(
                NetworkCombatSetupUtility.ProductEnemyPrefabPath);
            try
            {
                ConfigureNetworkEnemy(root);
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    NetworkCombatSetupUtility.ProductEnemyPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                NetworkCombatSetupUtility.ProductEnemyPrefabPath);
        }

        private static GameObject MigrateSkeletonEnemyPrefab()
        {
            RepairSkeletonWarningPrefab();
            GameObject root = LoadPrefab(SkeletonNetworkPrefabPath);
            try
            {
                RestoreSkeletonAnimator(root);
                EnemyController controller = root.GetComponent<EnemyController>();
                EnemyAttackMelee melee = root.GetComponent<EnemyAttackMelee>();
                if (controller == null || melee == null)
                {
                    throw new InvalidOperationException(
                        "NetworkEnemySkeleton must contain EnemyController and " +
                        "EnemyAttackMelee.");
                }

                melee.attackPrefab = AssetDatabase.LoadAssetAtPath<EnemyAttackPrefab>(
                    SkeletonWarningPrefabPath);
                if (melee.attackPrefab == null)
                {
                    throw new InvalidOperationException(
                        "Skeleton_Warning could not be resolved as EnemyAttackPrefab.");
                }

                // The validation sandbox has no scanned A* graph, so this first
                // attack-authority vertical slice uses EnemyDefaultMovement.
                controller.usesPathfinding = false;

                CombatantBehaviour combatant = GetOrAdd<CombatantBehaviour>(root);
                GetOrAdd<EnemyCombatantBinding>(root);
                GetOrAdd<StatusUpdateDriver>(root).Configure(combatant);
                GetOrAdd<CombatTeamBehaviour>(root).Configure(
                    CombatTeam.Enemy,
                    combatant);
                ConfigureNetworkEnemy(root, movementOnly: false);
                GetOrAdd<NetworkEnemyMeleeReplica>(root);

                if (root.GetComponentsInChildren<Component>(true)
                    .Any(component => component == null))
                {
                    throw new InvalidOperationException(
                        "NetworkEnemySkeleton still contains a Missing Script.");
                }

                PrefabUtility.SaveAsPrefabAsset(root, SkeletonNetworkPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                SkeletonNetworkPrefabPath);
        }

        private static void ConfigureNetworkEnemy(
            GameObject root,
            bool movementOnly = true)
        {
            GetOrAdd<NetworkIdentity>(root);
            RemoveIfPresent<NetworkTransformReliable>(root);
            EnemySimulationAuthority authority =
                GetOrAdd<EnemySimulationAuthority>(root);
            authority.ConfigureNetworkManaged(
                EnemySimulationMode.NormalClient);
            GetOrAdd<EnemySnapshotInterpolator>(root);
            GetOrAdd<NetworkEnemySimulationAgent>(root)
                .ConfigureProductSimulation(movementOnly);
            EnemyController controller = root.GetComponent<EnemyController>();
            if (controller != null && controller.attackScript != null)
            {
                // Network attack scripts start dormant so Observer instances do
                // not execute legacy Start/Update/OnEnable gameplay before their
                // replicated SimulationRole is known.
                controller.attackScript.enabled = false;
                EditorUtility.SetDirty(controller.attackScript);
            }
            RepairDamageInteractionTriggers(root);
            CombatantBehaviour combatant = root.GetComponent<CombatantBehaviour>();
            if (combatant == null)
            {
                throw new InvalidOperationException(
                    $"{root.name} requires {nameof(CombatantBehaviour)}.");
            }
            GetOrAdd<NetworkCombatantAdapter>(root).Configure(
                combatant,
                CombatEntityKind.Enemy,
                CombatEntityAuthority.ServerCanonical);
            GetOrAdd<NetworkEnemyServerDriver>(root);
        }

        private static void RepairSkeletonWarningPrefab()
        {
            GameObject root = LoadPrefab(SkeletonWarningPrefabPath);
            try
            {
                EnemyAttackPrefab attack = root.GetComponent<EnemyAttackPrefab>();
                if (attack == null)
                {
                    throw new InvalidOperationException(
                        "Skeleton_Warning requires EnemyAttackPrefab.");
                }

                attack.damageInteraction =
                    root.GetComponentInChildren<PlayerDamageInteraction>(true);
                attack.hitBox = root.GetComponentInChildren<
                    AstralShift.HellMaiden.Player.Attacks.BaseAttackHitBox>(true);
                attack.attackWarning =
                    root.GetComponentInChildren<EnemyAttackWarning>(true);
                if (attack.damageInteraction == null || attack.attackWarning == null)
                {
                    throw new InvalidOperationException(
                        "Skeleton_Warning requires its warning and " +
                        "PlayerDamageInteraction references to be restored.");
                }

                RepairDamageInteractionTriggers(root);
                GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    NetworkCombatSetupUtility.PlayerPrefabPath);
                PlayerHitbox playerHitbox = playerPrefab != null
                    ? playerPrefab.GetComponentInChildren<PlayerHitbox>(true)
                    : null;
                if (playerHitbox == null)
                {
                    throw new InvalidOperationException(
                        "NetworkPlayer requires PlayerHitbox before restoring " +
                        "Skeleton_Warning collision filtering.");
                }
                AstralShift.QTI.Triggers.Physics2D.StepOn2DTrigger[] triggers =
                    root.GetComponentsInChildren<
                        AstralShift.QTI.Triggers.Physics2D.StepOn2DTrigger>(true);
                for (int i = 0; i < triggers.Length; i++)
                {
                    triggers[i].layerMask = 1 << playerHitbox.gameObject.layer;
                    EditorUtility.SetDirty(triggers[i]);
                }
                PrefabUtility.SaveAsPrefabAsset(root, SkeletonWarningPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RestoreSkeletonAnimator(GameObject root)
        {
            EnemyAnimator enemyAnimator =
                root.GetComponentInChildren<EnemyAnimator>(true);
            if (enemyAnimator == null)
            {
                throw new InvalidOperationException(
                    "Enemy_Skeleton requires EnemyAnimator.");
            }

            MonoBehaviour animancer = root.GetComponentsInChildren<MonoBehaviour>(true)
                .FirstOrDefault(component =>
                    component != null &&
                    component.GetType().FullName == "Animancer.AnimancerComponent");
            Animator unityAnimator = root.GetComponentInChildren<Animator>(true);
            if (animancer == null || unityAnimator == null)
            {
                throw new InvalidOperationException(
                    "Enemy_Skeleton requires restored Animancer and Animator components.");
            }

            var serialized = new SerializedObject(enemyAnimator);
            SetObjectReference(serialized, "animancer", animancer);
            SetObjectReference(serialized, "animator", unityAnimator);
            SetObjectReference(
                serialized,
                "paletteSwapper",
                root.GetComponentInChildren<SpriteRendererPaletteSwapper>(true));
            SetObjectArray(
                serialized,
                "renderers",
                root.GetComponentsInChildren<SpriteRenderer>(true));

            AnimationClip moveLeft = LoadSkeletonClip(
                "Enemy_Skeleton_WalkLeft_03.anim");
            AnimationClip moveRight = LoadSkeletonClip(
                "Enemy_Skeleton_WalkRight_03.anim");
            AnimationClip warningLeft = LoadSkeletonClip(
                "Enemy_Skeleton_WarningLeftDown.anim");
            AnimationClip warningRight = LoadSkeletonClip(
                "Enemy_Skeleton_WarningRightDown.anim");
            AnimationClip attackLeft = LoadSkeletonClip(
                "Enemy_Skeleton_AttackMomentLeft.anim");
            AnimationClip attackRight = LoadSkeletonClip(
                "Enemy_Skeleton_AttackMomentRight.anim");
            AnimationClip recoveryLeft = LoadSkeletonClip(
                "Enemy_Skeleton_AttackRecoveryLeft.anim");
            AnimationClip recoveryRight = LoadSkeletonClip(
                "Enemy_Skeleton_AttackRecoveryRight.anim");
            AnimationClip hurtLeft = LoadSkeletonClip(
                "Enemy_Skeleton_Hurt_LeftDown.anim");
            AnimationClip hurtRight = LoadSkeletonClip(
                "Enemy_Skeleton_Hurt_RightDown.anim");
            AnimationClip deadLeft = LoadSkeletonClip(
                "Enemy_Skeleton_Dead_LeftDown.anim");
            AnimationClip deadRight = LoadSkeletonClip(
                "Enemy_Skeleton_Dead_RightDown.anim");

            SetDirectionalClips(serialized, "move", moveLeft, moveRight);
            SetDirectionalClips(
                serialized,
                "attackWarning",
                warningLeft,
                warningRight);
            SetDirectionalClips(serialized, "attack", attackLeft, attackRight);
            SetDirectionalClips(
                serialized,
                "recovery",
                recoveryLeft,
                recoveryRight);
            SetDirectionalClips(serialized, "hurt", hurtLeft, hurtRight);
            SetDirectionalClips(serialized, "dead", deadLeft, deadRight);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(enemyAnimator);
        }

        private static AnimationClip LoadSkeletonClip(string fileName)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                SkeletonAnimationFolder + fileName);
            if (clip == null)
            {
                throw new InvalidOperationException(
                    $"Required Skeleton animation is missing: {fileName}");
            }

            return clip;
        }

        private static void SetDirectionalClips(
            SerializedObject serialized,
            string prefix,
            AnimationClip left,
            AnimationClip right)
        {
            SetClip(serialized, prefix + "LeftUp", left);
            SetClip(serialized, prefix + "LeftDown", left);
            SetClip(serialized, prefix + "RightUp", right);
            SetClip(serialized, prefix + "RightDown", right);
        }

        private static void SetClip(
            SerializedObject serialized,
            string propertyName,
            AnimationClip clip)
        {
            SerializedProperty transition = serialized.FindProperty(propertyName);
            SerializedProperty clipProperty =
                transition?.FindPropertyRelative("_Clip");
            if (clipProperty == null)
            {
                throw new InvalidOperationException(
                    $"Animancer transition property is missing: {propertyName}._Clip");
            }

            clipProperty.objectReferenceValue = clip;
        }

        private static void SetObjectReference(
            SerializedObject serialized,
            string propertyName,
            UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Serialized property is missing: {propertyName}");
            }

            property.objectReferenceValue = value;
        }

        private static void SetObjectArray(
            SerializedObject serialized,
            string propertyName,
            UnityEngine.Object[] values)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                throw new InvalidOperationException(
                    $"Serialized array is missing: {propertyName}");
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void RepairDamageInteractionTriggers(GameObject root)
        {
            InteractionTrigger[] triggers =
                root.GetComponentsInChildren<InteractionTrigger>(true);
            for (int i = 0; i < triggers.Length; i++)
            {
                if (triggers[i].interaction != null)
                {
                    continue;
                }

                PlayerDamageInteraction damageInteraction =
                    triggers[i].GetComponent<PlayerDamageInteraction>();
                if (damageInteraction != null)
                {
                    triggers[i].interaction = damageInteraction;
                    EditorUtility.SetDirty(triggers[i]);
                }
            }
        }

        private static void MigrateWorldPrefab()
        {
            GameObject root = LoadPrefab(NetworkCombatSetupUtility.WorldPrefabPath);
            try
            {
                GetOrAdd<NetworkEnemySimulationWorld>(root);
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    NetworkCombatSetupUtility.WorldPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void MigrateSandboxScene(
            GameObject productEnemy,
            GameObject skeletonEnemy)
        {
            Scene scene = EditorSceneManager.OpenScene(
                NetworkCombatSetupUtility.SandboxScenePath,
                OpenSceneMode.Single);
            NetworkManager manager = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NetworkManager>(true))
                .Single();
            if (!manager.spawnPrefabs.Contains(productEnemy))
            {
                manager.spawnPrefabs.Add(productEnemy);
                EditorUtility.SetDirty(manager);
            }
            if (!manager.spawnPrefabs.Contains(skeletonEnemy))
            {
                manager.spawnPrefabs.Add(skeletonEnemy);
                EditorUtility.SetDirty(manager);
            }

            if (scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EnemyAIManager>(true))
                .All(component => component == null))
            {
                new GameObject("Enemy AI Manager").AddComponent<EnemyAIManager>();
            }

            if (scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<NetworkCombatPoolBootstrap>(true))
                .All(component => component == null))
            {
                var poolRoot = new GameObject("Network Combat Pool Manager");
                poolRoot.AddComponent<PoolManager>();
                poolRoot.AddComponent<NetworkCombatPoolBootstrap>();
            }

            NetworkEnemySandboxSpawner spawner = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<NetworkEnemySandboxSpawner>(true))
                .Single();
            NetworkEnemyProcessValidationBootstrap processValidation =
                scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        NetworkEnemyProcessValidationBootstrap>(true))
                    .FirstOrDefault();
            if (processValidation == null)
            {
                processValidation = new GameObject(
                        "Enemy Simulation Process Validation")
                    .AddComponent<NetworkEnemyProcessValidationBootstrap>();
            }
            processValidation.Configure(manager, spawner, skeletonEnemy);
            EditorUtility.SetDirty(processValidation);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(
                scene,
                NetworkCombatSetupUtility.SandboxScenePath))
            {
                throw new InvalidOperationException("Failed to save network sandbox scene.");
            }
        }

        private static GameObject LoadPrefab(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                throw new InvalidOperationException($"Required prefab is missing: {path}");
            }
            return PrefabUtility.LoadPrefabContents(path);
        }

        private static T GetOrAdd<T>(GameObject root) where T : Component
        {
            T component = root.GetComponent<T>();
            return component != null ? component : root.AddComponent<T>();
        }

        private static void RemoveIfPresent<T>(GameObject root) where T : Component
        {
            T component = root.GetComponent<T>();
            if (component != null)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }
}
