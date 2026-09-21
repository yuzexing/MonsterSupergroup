using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    internal static class LimboReferenceFixture
    {
        // Full is the user's editable launch rule, not an immutable test fixture.
        // Pin only the imported Timeline on a transient rule copy; never save it.
        public static WaveParameters CaptureFull()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Full.asset");
            Assert.That(source, Is.Not.Null);
            var copy = Object.Instantiate(source);
            try
            {
                var serialized = new SerializedObject(copy);
                serialized.FindProperty("timeline").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root + "/Limbo.playable");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(copy.TryCapture(out var result, out string error), Is.True, error);
                return result;
            }
            finally { Object.DestroyImmediate(copy); }
        }
    }
}
