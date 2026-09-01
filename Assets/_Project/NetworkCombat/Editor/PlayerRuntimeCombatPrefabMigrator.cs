using System;
using AstralShift.HellMaiden.Player;
using AstralShift.QTI.Interactors;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Local;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class PlayerRuntimeCombatPrefabMigrator
    {
        public const string ProductPlayerPath =
            "Assets/_Project/Content/LocalCombat/Player.prefab";
        public const string NetworkPlayerPath =
            "Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab";

        [MenuItem("Tools/Monster Supergroup/Migrate Player Runtime Combat Prefabs")]
        public static void Run()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ProductPlayerPath);
            try
            {
                ConfigureProductPlayer(root);
                PrefabUtility.SaveAsPrefabAsset(root, ProductPlayerPath);

                ConfigureNetworkPlayer(root);
                PrefabUtility.SaveAsPrefabAsset(root, NetworkPlayerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Player runtime combat prefabs migrated successfully.");
        }

        public static void RunBatch()
        {
            Run();
        }

        private static void ConfigureProductPlayer(GameObject root)
        {
            PlayerMovement movement = RequireComponent<PlayerMovement>(root);
            CombatantBehaviour combatant = RequireComponent<CombatantBehaviour>(root);
            PlayerCombatantBinding binding = GetOrAdd<PlayerCombatantBinding>(root);
            binding.Configure(movement, combatant);

            StatusUpdateDriver statusDriver = GetOrAdd<StatusUpdateDriver>(root);
            statusDriver.Configure(combatant);

            CircleCollider2D obstacleCollider = RequireComponent<CircleCollider2D>(root);
            Transform hitboxTransform = FindChild(root.transform, "HitBox") ??
                CreateChild(root.transform, "HitBox");
            int playerHitboxLayer = LayerMask.NameToLayer("PlayerHitbox");
            if (playerHitboxLayer < 0)
            {
                throw new InvalidOperationException(
                    "The PlayerHitbox layer is required for player damage collision.");
            }
            hitboxTransform.gameObject.layer = playerHitboxLayer;
            CircleCollider2D hitboxCollider =
                GetOrAdd<CircleCollider2D>(hitboxTransform.gameObject);
            hitboxCollider.isTrigger = true;
            if (hitboxCollider.radius <= 0f)
            {
                hitboxCollider.radius = obstacleCollider.radius;
            }

            PlayerHitbox hitbox = GetOrAdd<PlayerHitbox>(hitboxTransform.gameObject);
            hitbox.Configure(binding);

            var serializedMovement = new SerializedObject(movement);
            SetObjectReference(serializedMovement, "_hitboxCollider", hitboxCollider);
            SetObjectReference(serializedMovement, "_obstacleCollider", obstacleCollider);
            SetObjectReference(serializedMovement, "combatantBinding", binding);

            Interactor interactor = root.GetComponent<Interactor>();
            if (interactor != null)
            {
                SetObjectReference(serializedMovement, "interactor", interactor);
            }

            PlayerAnimator playerAnimator = root.GetComponentInChildren<PlayerAnimator>(true);
            if (playerAnimator != null)
            {
                SetObjectReference(serializedMovement, "animator", playerAnimator);
            }

            SpriteRenderer spriteRenderer = root.GetComponentInChildren<SpriteRenderer>(true);
            if (spriteRenderer != null)
            {
                SetObjectReference(serializedMovement, "spriteRenderer", spriteRenderer);
            }

            serializedMovement.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(root);
        }

        private static void ConfigureNetworkPlayer(GameObject root)
        {
            CombatantBehaviour combatant = RequireComponent<CombatantBehaviour>(root);
            RemoveIfPresent<PlayerLoader>(root);
            RemoveIfPresent<PlayerHandBehaviour>(root);
            GetOrAdd<NetworkIdentity>(root);
            NetworkTransformReliable networkTransform = GetOrAdd<NetworkTransformReliable>(root);
            networkTransform.syncDirection = SyncDirection.ClientToServer;

            GetOrAdd<MirrorNetworkCombatBridge>(root);
            GetOrAdd<NetworkEnemySimulationEndpoint>(root);
            GetOrAdd<NetworkPlayerAutoTargeting>(root);
            PlayerBuildRuntime build = GetOrAdd<PlayerBuildRuntime>(root);
            build.ConfigureInitialWeapon(2u);
            GetOrAdd<NetworkWeaponCombatAdapter>(root);
            NetworkCombatantAdapter combatantAdapter = GetOrAdd<NetworkCombatantAdapter>(root);
            combatantAdapter.Configure(
                combatant,
                CombatEntityKind.Player,
                CombatEntityAuthority.OwnerFinal);
            GetOrAdd<NetworkPlayerBootstrap>(root);

            EditorUtility.SetDirty(root);
        }

        private static T RequireComponent<T>(GameObject root) where T : Component
        {
            T component = root.GetComponent<T>();
            if (component == null)
            {
                throw new InvalidOperationException(
                    $"{ProductPlayerPath} requires {typeof(T).Name} on its root.");
            }

            return component;
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static void RemoveIfPresent<T>(GameObject gameObject)
            where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component != null)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        private static Transform FindChild(Transform root, string name)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == name)
                {
                    return children[i];
                }
            }

            return null;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void SetObjectReference(
            SerializedObject serializedObject,
            string propertyName,
            UnityEngine.Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Missing serialized property {propertyName} on " +
                    serializedObject.targetObject.GetType().Name + ".");
            }

            property.objectReferenceValue = value;
        }
    }
}
