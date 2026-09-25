using System.Linq;
using MonsterSupergroup.NetworkCombat.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using AstralShift.HellMaiden.Data.Cards;
using UnityEditor;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatInvestigationCatalogTests
    {
        [Test] public void CatalogBindsBuildAndContentWithStableNamesForAllThreeDomains()
        {
            string first = CombatEvidenceBuild.CaptureNameCatalog("test-build", "content-version", "source-hash");
            Assert.That(CombatEvidenceBuild.CaptureNameCatalog("test-build", "content-version", "source-hash"), Is.EqualTo(first));
            var catalog = JObject.Parse(first);
            Assert.That((string)catalog["buildId"], Is.EqualTo("test-build"));
            Assert.That((string)catalog["contentVersion"], Is.EqualTo("content-version"));
            Assert.That((string)catalog["sourceConfigurationHash"], Is.EqualTo("source-hash"));
            var entries = (JArray)catalog["entries"];
            foreach (string kind in new[] { "weapon", "equipment", "blessing" })
                Assert.That(entries.Any(e => (string)e["kind"] == kind && (bool)e["identityValid"] && ((JObject)e["names"]).Count > 0), Is.True, kind);
            Assert.That(entries.All(e => !string.IsNullOrEmpty((string)e["assetGuid"])), Is.True);
        }
        [Test] public void CatalogIncludesActualRuntimeEquipmentOutsideProjectFolder()
        {
            var database = AssetDatabase.LoadAssetAtPath<EquipmentDB>("Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasEquipmentDB.asset");
            Assert.That(database, Is.Not.Null);
            Assert.That(database.Equipments, Is.Not.Empty);
            var entries = (JArray)JObject.Parse(CombatEvidenceBuild.CaptureNameCatalog("build", "content", "source"))["entries"];
            foreach (var equipment in database.Equipments)
            {
                string path = AssetDatabase.GetAssetPath(equipment);
                var matching = entries.Where(e => (string)e["kind"] == "equipment" && (string)e["assetPath"] == path).ToArray();
                Assert.That(matching, Has.Length.EqualTo(1), path);
                Assert.That((uint)matching[0]["contentId"], Is.EqualTo(equipment.ID));
                Assert.That(((JObject)matching[0]["names"]).Count, Is.GreaterThan(0), path);
            }
        }
    }
}
