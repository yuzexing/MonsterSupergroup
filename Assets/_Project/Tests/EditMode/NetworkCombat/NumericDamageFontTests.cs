using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class NumericDamageFontTests
    {
        [Test]
        public void DamageDigitsKeepOriginalGlyphsAndAtlasWithoutDynamicFallbackWork()
        {
            const string root = "Assets/_Project/Content/DamageNumbers";
            var source = AssetDatabase.LoadMainAssetAtPath("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            var font = AssetDatabase.LoadMainAssetAtPath(root + "/DamageDigits.asset");
            Assert.That(font, Is.Not.Null);
            var original = new SerializedObject(source);
            var adapted = new SerializedObject(font);
            Assert.That(adapted.FindProperty("m_AtlasPopulationMode").intValue, Is.Zero);
            Assert.That(adapted.FindProperty("m_IsMultiAtlasTexturesEnabled").boolValue, Is.False);
            Assert.That(adapted.FindProperty("m_FallbackFontAssetTable").arraySize, Is.Zero);
            Assert.That(original.FindProperty("m_FallbackFontAssetTable").arraySize, Is.GreaterThan(0), "Do not modify the shared UI font.");
            foreach (string field in new[] { "m_GlyphTable", "m_CharacterTable", "m_AtlasTextures", "m_FaceInfo" })
                Assert.That(SerializedProperty.DataEquals(original.FindProperty(field), adapted.FindProperty(field)), Is.True, field);
            int inspected = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
            foreach (var component in AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)).GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().Name != "TextMeshPro") continue;
                var data = new SerializedObject(component);
                Assert.That(data.FindProperty("m_fontAsset").objectReferenceValue, Is.SameAs(font));
                inspected++;
            }
            Assert.That(inspected, Is.EqualTo(6));
        }
    }
}
