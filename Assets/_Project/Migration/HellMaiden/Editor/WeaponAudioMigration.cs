using System;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    public static class WeaponAudioMigration
    {
        private static readonly int[] Shot = { 305521848, 1139334325, -1555974261, -1191919773 };
        private static readonly int[] Breath = { -1201277076, 1182374954, -2002646633, 429272306 };
        public static void Apply()
        {
            foreach (string path in DanteProjectilePresentationMigration.ProjectilePaths) Repair(path, false);
            Repair(DanteBeamNativeGasMigration.OutputFolder + "/GameObject/PlayerAttack_Dante_DragonsBreath_Fire.prefab", true);
            Repair(DanteBeamNativeGasMigration.OutputFolder + "/GameObject/PlayerAttack_Dante_DragonsBreath_Poison.prefab", true);
            const string folder = "Assets/_Project/Content/NetworkCombat/Limbo/Resources/LimboReference/";
            if (AssetDatabase.LoadMainAssetAtPath(folder + "AudioObservation.asset") == null)
            {
                AssetDatabase.CopyAsset(folder + "ArtEffects.asset", folder + "AudioObservation.asset");
                var rules = AssetDatabase.LoadMainAssetAtPath(folder + "AudioObservation.asset");
                rules.name = "AudioObservation";
                var serialized = new SerializedObject(rules);
                serialized.FindProperty("referenceEndTime").doubleValue = 600;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rules);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[WeaponAudio] Updated five source audio adaptations; existing reviewed audio values are preserved on repeat.");
        }
        private static void Repair(string path, bool beam)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (beam) ConfigureBeam(root);
                else ConfigureProjectile(new SerializedObject(root.GetComponent<ProjectileAttack>()));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        public static void ConfigureProjectile(SerializedObject so)
        {
            if (so.FindProperty("audioAdaptationVersion").intValue >= 1) return;
            so.FindProperty("playPiercingHitSound").boolValue = false;
            so.FindProperty("sourceSoundLifecycle").boolValue = true;
            foreach (string field in new[] { "chargeSound", "expireSound", "projectileLoopSound" })
            { Reference(so, field + ".eventRef", null, ""); so.FindProperty(field + ".automatic").boolValue = false; }
            foreach (string field in new[] { "launchSound", "projectileHitSound" })
            { Reference(so, field + ".eventRef", Shot, "event:/sx/plr/Sx_plr_slowprojectile_shot"); so.FindProperty(field + ".automatic").boolValue = true; }
            so.FindProperty("audioAdaptationVersion").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        public static void ConfigureBeam(GameObject root)
        {
            var so = new SerializedObject(root.GetComponent<AnimatedAttack>());
            if (so.FindProperty("audioAdaptationVersion").intValue >= 1) return;
            foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (c != null && (c.GetType().FullName == "FMODUnity.StudioParameterTrigger" ||
                                  c.GetType().FullName == "FMODUnity.StudioEventEmitter"))
                    UnityEngine.Object.DestroyImmediate(c);
            Reference(so, "stagedSound", Breath, "event:/sx/plr/Sx_plr_dragonbreath");
            so.FindProperty("completionSoundParameter").stringValue = "Phase";
            foreach (string field in new[] { "startSound", "loopSound", "endSound", "hitSound" }) Reference(so, field, null, "");
            so.FindProperty("audioAdaptationVersion").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Reference(SerializedObject so, string field, int[] guid, string path)
        {
            for (int i=0;i<4;i++) so.FindProperty(field + ".Guid.Data" + (i+1)).intValue = guid == null ? 0 : guid[i];
            var p=so.FindProperty(field + ".Path"); if(p != null) p.stringValue = path;
        }
    }
}
