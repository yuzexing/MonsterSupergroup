using System.IO;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    public static class DanteProjectilePresentationPreview
    {
        public static void ImportAndCapture()
        {
            DanteProjectilePresentationMigration.Import();
            var paths = new System.Collections.Generic.List<string>(Directory.GetFiles(
                DanteProjectilePresentationMigration.OutputFolder, "*", SearchOption.AllDirectories));
            paths.AddRange(DanteProjectilePresentationMigration.ProjectilePaths);
            paths.Add(DanteProjectilePresentationMigration.ImpactPath);
            var before = paths.ConvertAll(File.ReadAllBytes);
            DanteProjectilePresentationMigration.Import();
            for (int i = 0; i < paths.Count; i++)
                if (!System.Linq.Enumerable.SequenceEqual(before[i], File.ReadAllBytes(paths[i])))
                    throw new System.InvalidOperationException("Repeat import changed " + paths[i]);
            Debug.Log("Wisp repeat import: byte-identical assets and GUIDs.");
            Capture();
        }


        public static void Capture()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DanteProjectilePresentationMigration.ClipPath);
            string[] names = { "Default", "Fire", "Poison" };
            for (int i = 0; i < names.Length; i++)
                foreach (bool light in new[] { false, true })
                    foreach (float time in new[] { .1f, .8f })
                    {
                        string name = names[i] + (light ? "-light-" : "-dark-") + (time < .3f ? "appear" : "flight");
                        DanteAttackPresentationPreview.Capture(DanteProjectilePresentationMigration.ProjectilePaths[i],
                            new[] { clip }, new[] { 2f }, time, Path.Combine("Logs/Wisp/Previews", name + ".png"),
                            "Wisp " + name, animationRootPath: "", direction: Vector2.right,
                            backgroundColor: light ? new Color(.65f, .65f, .65f) : new Color(.025f, .03f, .05f));
                    }
        }
    }
}
