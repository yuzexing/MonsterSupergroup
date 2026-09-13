using System.Linq;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.HellMaidenMigration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Tests
{
    public sealed class DanteProjectilePresentationMigrationTests
    {
        [Test]
        public void Wisp_SourceLayersAndDependenciesAreComplete() => DanteProjectilePresentationMigration.ValidateImportedAssets();

        [Test]
        public void Wisp_AnimationTargetsExistAndMaterialCurvesUseSupportedProperties()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DanteProjectilePresentationMigration.ClipPath);
            foreach (string path in DanteProjectilePresentationMigration.ProjectilePaths)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var target = root.transform.Find(binding.path);
                    Assert.That(target, Is.Not.Null, binding.path);
                    if (!binding.propertyName.StartsWith("material.")) continue;
                    var material = target.GetComponent<Renderer>().sharedMaterial;
                    Assert.That(material.HasProperty(binding.propertyName.Substring(9)), Is.True, binding.propertyName);
                }
            }
        }

        [Test]
        public void Wisp_PresentationRepairPreservesIdentityAndDamage()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<AstralShift.HellMaiden.Data.Cards.WeaponData>(DanteNativeGasMigration.WeaponPath);
            Assert.That(weapon.ID, Is.EqualTo(2));
            Assert.That(weapon.BaseStats.damage, Is.EqualTo(15));
            Assert.That(weapon.AttackTags, Is.EqualTo(MonsterSupergroup.GAS.CombatTags.Attack | MonsterSupergroup.GAS.CombatTags.Projectile));
            Assert.That(weapon.WeaponPrefab, Is.TypeOf<ProjectileAttackBehaviour>());
        }

        [Test]
        public void Wisp_ThreeVariantsKeepDistinctAuthoredColors()
        {
            var colors = DanteProjectilePresentationMigration.ProjectilePaths.Select(path =>
                AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.Find("Root/Scale/HeadLight (1)")
                    .GetComponent<Renderer>().sharedMaterial.GetColor("_Color")).ToArray();
            Assert.That(colors.Distinct().Count(), Is.EqualTo(3));
        }
    }
}
