using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Animancer;
using AstralShift.HellMaiden.AI.Enemy;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class LimboImpAssets
    {
        public const string Root = LimboReferenceAssets.Root + "/Imp";
        public const string EnemyPath = Root + "/ReferenceImp.prefab", BulletPath = Root + "/ReferenceImpBullet.prefab";
        [Serializable] public class Binding { public string field, clip, normalizedStart; public float length, speed, fade; public string[] eventTimes; }
        [Serializable] public class Geometry { public string path, type; public bool bullet, trigger; public float radius; public float[] localPosition, localScale, localRotation, offset, size; }
        [Serializable] public class Data
        {
            public string sourceSha256; public Binding[] bindings; public Geometry[] geometry;
            public float warningExtra, activeExtra, recoveryExtra, cooldown, distance, bulletSpeed, bulletDuration;
            public bool bulletTimeout; public int bulletPierce;
        }

        // Deliberately updates only Imp's adapted assets and readiness links, not existing database tuning.
        public static void Create()
        {
            LimboReferenceAssets.Create();
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(Root + "/ImpAdapted.json"));
            if (data.bindings.Length != 12) throw new InvalidDataException("Expected twelve recovered Imp attack transitions.");
            AssetDatabase.Refresh();
            if (!File.Exists(BulletPath)) AssetDatabase.CopyAsset(EnemyPrefabVariantMigration.ImpBulletPath, BulletPath);
            if (!File.Exists(EnemyPath)) AssetDatabase.CopyAsset(EnemyPrefabVariantMigration.ImpPath, EnemyPath);
            var bulletRoot = PrefabUtility.LoadPrefabContents(BulletPath);
            try
            {
                var bullet = bulletRoot.GetComponent<BulletProjectile>();
                bullet.speed = data.bulletSpeed; bullet.duration = data.bulletDuration; bullet.pierce = data.bulletPierce;
                bullet.bulletHasTimeOut = data.bulletTimeout; bullet.projectileMovement = null;
                var so = new SerializedObject(bullet);
                so.FindProperty("rotationTransform").objectReferenceValue = null;
                so.FindProperty("attachedWhileCharging").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                ApplyGeometry(bulletRoot, data, true);
                PrefabUtility.SaveAsPrefabAsset(bulletRoot, BulletPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(bulletRoot); }
            var enemyRoot = PrefabUtility.LoadPrefabContents(EnemyPath);
            try
            {
                enemyRoot.name = "ReferenceImp";
                var controller = enemyRoot.GetComponent<EnemyController>();
                var so = new SerializedObject(controller);
                so.FindProperty("attackCooldown").floatValue = data.cooldown;
                so.FindProperty("attackDistance").floatValue = data.distance;
                so.ApplyModifiedPropertiesWithoutUndo();
                var attack = enemyRoot.GetComponent<EnemyProjectileAttack>();
                attack.bulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPath).GetComponent<BulletProjectile>();
                so = new SerializedObject(attack);
                so.FindProperty("warningTime").floatValue = data.warningExtra;
                so.FindProperty("attackTime").floatValue = data.activeExtra;
                so.FindProperty("recoveryTime").floatValue = data.recoveryExtra;
                so.ApplyModifiedPropertiesWithoutUndo();
                so = new SerializedObject(enemyRoot.GetComponentInChildren<EnemyAnimator>(true));
                foreach (var binding in data.bindings)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(EnemyPrefabVariantMigration.ImpResources + "/AnimationClip/" + binding.clip + ".anim");
                    if (clip == null || Mathf.Abs(clip.length - binding.length) > .0001f)
                        throw new InvalidDataException("Existing animation does not match recovered length: " + binding.clip);
                    var transition = so.FindProperty(binding.field);
                    transition.FindPropertyRelative("_Clip").objectReferenceValue = clip;
                    transition.FindPropertyRelative("_Speed").floatValue = binding.speed;
                    transition.FindPropertyRelative("_FadeDuration").floatValue = binding.fade;
                    transition.FindPropertyRelative("_NormalizedStartTime").floatValue = Parse(binding.normalizedStart);
                    var events = transition.FindPropertyRelative("_Events");
                    var times = events.FindPropertyRelative("_NormalizedTimes"); times.arraySize = binding.eventTimes.Length;
                    for (int i = 0; i < times.arraySize; i++) times.GetArrayElementAtIndex(i).floatValue = Parse(binding.eventTimes[i]);
                    events.FindPropertyRelative("_Names").arraySize = 0;
                    // Source callbacks have empty method names. Preserve timing without importing audio targets.
                    events.FindPropertyRelative("_Callbacks").arraySize = 0;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                ApplyGeometry(enemyRoot, data, false);
                PrefabUtility.SaveAsPrefabAsset(enemyRoot, EnemyPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(enemyRoot); }

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root + "/Limbo.playable");
            foreach (var clip in timeline.GetOutputTracks().SelectMany(t => t.GetClips()).Select(c => (NetworkEnemySpawnClip)c.asset))
            {
                clip.recoveredEvidence = "docs/evidence/hellmaiden-attacks/recovered-enemies.json#" + clip.sourceEnemy;
                if (clip.sourceEnemy == "Imp")
                {
                    clip.enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
                    if (clip.referenceReadiness != ReferenceEnemyReadiness.Ready || !string.IsNullOrEmpty(clip.missingEvidence))
                    { clip.referenceReadiness = ReferenceEnemyReadiness.ValidationPending; clip.missingEvidence = "数据已恢复并接入；等待有画面的 Host/Client 验证。"; }
                }
                else if (clip.referenceReadiness != ReferenceEnemyReadiness.ValidationPending && !string.IsNullOrEmpty(clip.missingEvidence))
                { clip.referenceReadiness = ReferenceEnemyReadiness.ImplementationPending; clip.missingEvidence = "原始攻击数据已恢复；对应行为尚未接入和验证。"; }
                EditorUtility.SetDirty(clip);
            }
            MakeRules("ImpOpening", timeline, 105, false);
            MakeRules("ImpValidation", timeline, 105, true);
            for (int variant = 0; variant < 2; variant++)
            {
                string path = Root + "/ImpFixture" + variant + ".playable";
                var fixture = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
                if (fixture == null)
                {
                    fixture = ScriptableObject.CreateInstance<TimelineAsset>(); AssetDatabase.CreateAsset(fixture, path);
                    var track = fixture.CreateTrack<NetworkEnemySpawnTrack>(); var clip = track.CreateClip<NetworkEnemySpawnClip>();
                    clip.start = 1; clip.duration = 1;
                    var spawn = (NetworkEnemySpawnClip)clip.asset; spawn.enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
                    spawn.sourceEnemy = "Imp"; spawn.sourceVariant = variant; spawn.count = 1;
                    spawn.referenceMode = ReferenceSpawnMode.CurveBudget; spawn.spawnCurve = AnimationCurve.Constant(0, 1, 1);
                    spawn.referenceReadiness = ReferenceEnemyReadiness.ValidationPending; spawn.expiresOffscreen = false;
                    spawn.recoveredEvidence = "Explicit isolated mechanism fixture; not a source Timeline segment.";
                    EditorUtility.SetDirty(spawn); EditorUtility.SetDirty(track); EditorUtility.SetDirty(fixture);
                }
                MakeRules("ImpFixture" + variant, fixture, 90, true);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[LimboImp] recovered configuration applied; production gate remains until rendered validation.");
        }
        private static float Parse(string value) => float.Parse(value, CultureInfo.InvariantCulture);
        private static Vector3 V3(float[] v) => new Vector3(v[0], v[1], v[2]);
        private static void ApplyGeometry(GameObject root, Data data, bool bullet)
        {
            foreach (var row in data.geometry.Where(g => g.bullet == bullet))
            {
                var target = string.IsNullOrEmpty(row.path) ? root.transform : root.transform.Find(row.path);
                if (target == null) throw new InvalidDataException("Reference geometry target absent: " + row.path);
                if (row.type == "Transform")
                {
                    if (!string.IsNullOrEmpty(row.path)) target.localPosition = V3(row.localPosition);
                    target.localScale = V3(row.localScale);
                    target.localRotation = new Quaternion(row.localRotation[0], row.localRotation[1], row.localRotation[2], row.localRotation[3]);
                }
                else if (row.type == "CircleCollider2D")
                { var circle = target.GetComponent<CircleCollider2D>(); circle.radius = row.radius; circle.offset = new Vector2(row.offset[0], row.offset[1]); circle.isTrigger = row.trigger; }
                else if (row.type == "BoxCollider2D")
                { var box = target.GetComponent<BoxCollider2D>(); box.size = new Vector2(row.size[0], row.size[1]); box.offset = new Vector2(row.offset[0], row.offset[1]); box.isTrigger = row.trigger; }
                else throw new InvalidDataException("Unadapted geometry " + row.type);
            }
        }
        private static void MakeRules(string name, TimelineAsset timeline, double end, bool validation)
        {
            string path = LimboReferenceAssets.ResourcesRoot + "/" + name + ".asset";
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(path);
            if (rules == null)
            {
                rules = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Opening.asset"));
                AssetDatabase.CreateAsset(rules, path);
                var so = new SerializedObject(rules); so.FindProperty("timeline").objectReferenceValue = timeline;
                so.FindProperty("referenceEndTime").doubleValue = end;
                so.FindProperty("referenceValidationOnly").boolValue = validation;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        public static void CreateAndBuild()
        {
            Create();
            MonsterSupergroup.EditorTools.ProjectBuildService.Build("kcp-development", "Builds/LimboReference/MonsterSupergroupLimbo.exe");
        }
    }
}
