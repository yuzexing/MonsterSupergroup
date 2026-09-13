using System;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.Rendering;
using Mirror;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static partial class EnemyPrefabVariantMigration
    {
        public const string ImpPath = "Assets/_Project/Content/NetworkCombat/NetworkEnemyImp.prefab";
        public const string ImpResources = "Assets/_Project/Content/HellMaiden/Enemies/Imp";
        public const string ImpBulletPath = ImpResources + "/GameObject/EnemyBulletAttackImp.prefab";
        private const string ImpReports = "Logs/Imp";

        [MenuItem("Monster Supergroup/Network Combat/Migrate Imp Variant")]
        public static void MigrateImp()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before migrating enemy Prefabs.");
            Directory.CreateDirectory(ImpReports);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(ImpPath) == null)
                {
                    ImportEnemyResources(ImpResources, "Enemy_Imp.prefab", "EnemyBulletAttackImp.prefab", "Enemy_Imp_*.anim", ImpReports);
                    RepairImpBullet(true);
                    var source = PrefabUtility.LoadPrefabContents(ImpResources + "/GameObject/Enemy_Imp.prefab");
                    var scene = EditorSceneManager.NewPreviewScene();
                    GameObject target = null;
                    try
                    {
                        ConfigureImpAnimator(source, false);
                        target = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath), scene);
                        CopyHierarchyDifferences(source, target);
                        target.name = "NetworkEnemyImp";
                        target.GetComponent<NetworkEnemySimulationAgent>().ConfigureProductSimulation(false);
                        var contact = target.GetComponent<EnemyContactDamage>(); contact.Configure(contact.DamageInteraction, false);
                        var so = new SerializedObject(target.GetComponent<EnemyController>());
                        so.FindProperty("combatantBinding").objectReferenceValue = target.GetComponent<EnemyCombatantBinding>();
                        so.FindProperty("obstaclesLayerMask").intValue = LayerMask.GetMask("Obstacles"); so.ApplyModifiedPropertiesWithoutUndo();
                        target.transform.Find("Collider").gameObject.layer = LayerMask.NameToLayer("EnemyCollision");
                        target.GetComponentInChildren<EnemyHurtbox>(true).gameObject.layer = LayerMask.NameToLayer("EnemyHitbox");
                        target.AddComponent<NetworkEnemyProjectileAdapter>();
                        var renderer = target.GetComponent<EnemyController>().spriteRenderer;
                        string materialPath = ImpResources + "/Material/ImpBody.mat";
                        var material = new Material(renderer.sharedMaterial); material.EnableKeyword("HITEFFECT_ON"); material.SetFloat("_HitEffectBlend", 0);
                        AssetDatabase.CreateAsset(material, materialPath); renderer.sharedMaterial = material;
                        Save(target, ImpPath);
                    }
                    finally { if (target != null) Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene); PrefabUtility.UnloadPrefabContents(source); }
                }
                RepairImpBullet(false);
                var root = PrefabUtility.LoadPrefabContents(ImpPath);
                try
                {
                    bool changed = ConfigureImpAnimator(root, true);
                    if (root.GetComponent<NetworkEnemyProjectileAdapter>() == null) { root.AddComponent<NetworkEnemyProjectileAdapter>(); changed = true; }
                    if (changed) Save(root, ImpPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                RegisterInScenes(); Validate(); ValidateImp();
                Debug.Log("[Imp] Variant migration PASS");
            }
            finally
            {
                if (setup.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static bool ConfigureImpAnimator(GameObject root, bool missingOnly)
        {
            var animator = root.GetComponentInChildren<EnemyAnimator>(true); var so = new SerializedObject(animator);
            void Bind(string field, Object value) { var p = so.FindProperty(field); if (!missingOnly || p.objectReferenceValue == null) p.objectReferenceValue = value; }
            Bind("animancer", animator.GetComponents<MonoBehaviour>().First(c => c.GetType().FullName == "Animancer.AnimancerComponent"));
            Bind("animator", animator.GetComponent<Animator>()); Bind("paletteSwapper", animator.GetComponent<SpriteRendererPaletteSwapper>());
            var array = so.FindProperty("renderers");
            if (!missingOnly || array.arraySize == 0)
            {
                var renderers = root.GetComponentsInChildren<SpriteRenderer>(true); array.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            }
            string[] fields = { "move", "attackWarning", "attack", "recovery", "hurt", "dead" };
            string[] names = { "Walk_", "Warning", "AttackMoment_", "Recovery_", "Hurt", "Death" };
            for (int i = 0; i < fields.Length; i++)
                foreach (string side in new[] { "Left", "Right" })
                    foreach (string elevation in new[] { "Up", "Down" })
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ImpResources + "/AnimationClip/Enemy_Imp_" + names[i] + side + "Down.anim");
                        if (clip == null) throw new InvalidOperationException("Missing Imp animation " + fields[i] + side);
                        Bind(fields[i] + side + elevation + "._Clip", clip);
                    }
            return so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RepairImpBullet(bool initial)
        {
            var root = PrefabUtility.LoadPrefabContents(ImpBulletPath);
            try
            {
                var bullet = root.GetComponent<BulletProjectile>(); var so = new SerializedObject(bullet);
                void Bind(string field, Object value) { var p = so.FindProperty(field); if (p.objectReferenceValue == null) p.objectReferenceValue = value; }
                Bind("damageInteraction", root.GetComponentInChildren<PlayerDamageInteraction>(true));
                Bind("bulletVisual", root.transform.Find("BulletVisual").gameObject);
                Bind("rotationTransform", root.transform.Find("BulletVisual"));
                Bind("bulletParticles", root.transform.Find("BulletVisual").GetComponentInChildren<ParticleSystem>(true));
                bool changed = so.ApplyModifiedPropertiesWithoutUndo();
                if (initial) { bullet.speed = 6; bullet.duration = 5; bullet.pierce = 1; bullet.bulletHasTimeOut = true; root.SetActive(false); changed = true; }
                foreach (var trigger in root.GetComponentsInChildren<AstralShift.QTI.Triggers.Physics2D.StepOn2DTrigger>(true))
                {
                    if (trigger.interaction == null) { trigger.interaction = bullet.damageInteraction; changed = true; }
                    int mask = LayerMask.GetMask("PlayerHitbox");
                    if (trigger.layerMask != mask) { trigger.layerMask = mask; changed = true; }
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, ImpBulletPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void ValidateImp()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ImpPath); var controller = root.GetComponent<EnemyController>();
            if (PrefabUtility.GetPrefabAssetType(root) != PrefabAssetType.Variant || AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root)) != BasePath)
                throw new InvalidOperationException("Imp must directly inherit NetworkEnemyBase.");
            var attack = root.GetComponent<EnemyProjectileAttack>(); attack.enemyAnimator = controller.enemyAnimator;
            if (controller.attackScript != attack || attack.bulletPrefab == null || attack.bulletPosition == null || root.GetComponent<NetworkEnemyProjectileAdapter>() == null)
                throw new InvalidOperationException("Imp attack binding missing.");
            foreach (var asset in new[] { root, attack.bulletPrefab.gameObject })
                if (asset.GetComponentsInChildren<Component>(true).Any(c => c == null)) throw new InvalidOperationException("Missing Imp script.");
            if (root.GetComponentsInChildren<NetworkIdentity>(true).Length != 1 || attack.bulletPrefab.GetComponentsInChildren<NetworkIdentity>(true).Length != 0)
                throw new InvalidOperationException("Enemy/projectile network identities invalid.");
            var report = new LustReport {
                guid = AssetDatabase.AssetPathToGUID(ImpPath), parent = BasePath, assetId = root.GetComponent<NetworkIdentity>().assetId,
                health = controller.stats.BaseHealth, damage = controller.stats.BaseDamage, speed = controller.stats.BaseSpeed, xp = controller.stats.BaseXP,
                distance = controller.attackDistance, cooldown = controller.attackCooldown, warning = attack.WarningTime, active = attack.AttackTime, recovery = attack.RecoveryTime,
                overrides = PrefabUtility.GetPropertyModifications(root).Select(p => p.propertyPath + " = " + (p.objectReference != null ? p.objectReference.name : p.value)).ToArray()
            };
            Directory.CreateDirectory(ImpReports); File.WriteAllText(ImpReports + "/prefab-report.json", JsonUtility.ToJson(report, true));
        }

        public static void BuildImpValidation()
        {
            VerifyImpMigrationRepeat();
            const string output = "Builds/Imp/Imp.exe"; Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { NetworkCombatSetupUtility.BootScenePath, NetworkCombatSetupUtility.GameplayScenePath },
                locationPathName = output, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies,
                extraScriptingDefines = new[] { "MONSTER_KCP_DEVELOPMENT_BUILD", "MONSTER_MENU_VALIDATION" }
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Imp build failed.");
        }

        public static void VerifyImpMigrationRepeat()
        {
            MigrateImp();
            string[] paths = { ImpPath, ImpBulletPath, BasePath, SkeletonPath, ExamplePath, LustSinnerPath, NetworkCombatSetupUtility.BootScenePath, NetworkCombatSetupUtility.SandboxScenePath };
            var before = paths.ToDictionary(p => p, File.ReadAllBytes);
            MigrateImp();
            foreach (string path in paths) if (!before[path].SequenceEqual(File.ReadAllBytes(path))) throw new InvalidOperationException("Migration changed valid authored content on repeat: " + path);
            File.WriteAllText(ImpReports + "/migration-repeat.txt", "PASS: two consecutive migrations preserve all existing variants, Imp, bullet and scene registrations.");
        }
    }
}
