using System.Collections.Generic;
using System.Reflection;
using AstralShift.HellMaiden.AI.Enemy;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class ManualPlayRegressionTests
    {
        [Test]
        public void RecoveredEnemyTransitionsHaveNoOrphanedRegularEvents()
        {
            var failures = new List<string>();
            int inspected = 0;
            foreach (string guid in AssetDatabase.FindAssets("Reference t:Prefab", new[] { "Assets/_Project/Content/NetworkCombat/Limbo" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var animator = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<EnemyAnimator>(true);
                if (animator == null) continue;
                inspected++;
                var so = new SerializedObject(animator);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.name != "_NormalizedTimes" || !it.isArray) continue;
                    string parent = it.propertyPath.Substring(0, it.propertyPath.LastIndexOf('.'));
                    var callbacks = so.FindProperty(parent + "._Callbacks");
                    var names = so.FindProperty(parent + "._Names");
                    // The final time is Animancer's end event, which may legitimately have no callback.
                    for (int i = 0; i < it.arraySize - 1; i++)
                    {
                        bool callback = callbacks != null && i < callbacks.arraySize && callbacks.GetArrayElementAtIndex(i).managedReferenceValue != null;
                        bool named = names != null && i < names.arraySize && names.GetArrayElementAtIndex(i).objectReferenceValue != null;
                        if (!callback && !named) failures.Add(path + ":" + parent + "[" + i + "]");
                    }
                }
            }
            Assert.That(inspected, Is.GreaterThanOrEqualTo(9));
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void AttackAnimationDoesNotPauseTheMecanimAnimator()
        {
            var root = new GameObject("animation regression");
            try
            {
                var mecanim = root.AddComponent<Animator>();
                var body = root.AddComponent<EnemyAnimator>();
                body.animancer = root.AddComponent<Animancer.AnimancerComponent>();
                body.animancer.Animator = mecanim;
                var field = typeof(EnemyAnimator).GetField("animator", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                field.SetValue(body, mecanim);
                typeof(EnemyAnimator).GetMethod("PauseAnimator", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(body, null);
                Assert.That(mecanim.speed, Is.EqualTo(1), "Animancer animation must not write the ignored Animator.speed property.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
