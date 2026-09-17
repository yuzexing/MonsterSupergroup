using System.Linq;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.HellMaidenMigration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Tests
{
    public sealed class WeaponAudioMigrationTests
    {
        [Test]
        public void WispSourceSoundsAndManualAdaptationSurviveRepeatedRepair()
        {
            foreach (string path in DanteProjectilePresentationMigration.ProjectilePaths)
            {
                var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                try
                {
                    var so=new SerializedObject(root.GetComponent<ProjectileAttack>());
                    Assert.That(so.FindProperty("projectileLoopSound.automatic").boolValue,Is.False);
                    Assert.That(so.FindProperty("playPiercingHitSound").boolValue,Is.False);
                    Assert.That(so.FindProperty("launchSound.eventRef.Guid.Data1").intValue,Is.EqualTo(305521848));
                    Assert.That(so.FindProperty("projectileHitSound.eventRef.Guid.Data1").intValue,Is.EqualTo(305521848));
                    so.FindProperty("launchSound.automatic").boolValue=false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    WeaponAudioMigration.ConfigureProjectile(so);
                    Assert.That(so.FindProperty("launchSound.automatic").boolValue,Is.False,"Reviewed adaptations are not overwritten.");
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
        [Test]
        public void BreathHasOneStagedEntryAndNoStaleEmitterOrEndParameter()
        {
            foreach(string element in new[]{"Fire","Poison"})
            {
                var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DanteBeamNativeGasMigration.OutputFolder+"/GameObject/PlayerAttack_Dante_DragonsBreath_"+element+".prefab"));
                try
                {
                    var so=new SerializedObject(root.GetComponent<AnimatedAttack>());
                    Assert.That(so.FindProperty("stagedSound.Guid.Data1").intValue,Is.EqualTo(-1201277076));
                    Assert.That(so.FindProperty("completionSoundParameter").stringValue,Is.EqualTo("Phase"));
                    Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c!=null && (c.GetType().Name=="StudioEventEmitter" || c.GetType().Name=="StudioParameterTrigger")),Is.False);
                    so.FindProperty("completionSoundParameter").stringValue="";
                    so.ApplyModifiedPropertiesWithoutUndo();
                    WeaponAudioMigration.ConfigureBeam(root);
                    Assert.That(so.FindProperty("completionSoundParameter").stringValue,Is.Empty);
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
    }
}
