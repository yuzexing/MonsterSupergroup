#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class GameplayHealthHUDLoadingTests
    {
        [UnityTest]
        public IEnumerator GameplayScene_CreatesOneHUDInItsOwnScene_AndUnloadsCleanly()
        {
            const string path = "Assets/_Project/Scenes/Gameplay.unity";
            Scene originalScene = SceneManager.GetActiveScene();
            var playerObject = new GameObject("HUD Surviving Player");
            var combatant = playerObject.AddComponent<CombatantBehaviour>();
            combatant.Initialize(135);
            GameplayUIRoot ui = null;
            Scene gameplay = default;
            try
            {
                // Load the actual scene; never instantiate the UI in the test.
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path,
                    new LoadSceneParameters(LoadSceneMode.Additive));
                yield return null;
                gameplay = SceneManager.GetSceneByPath(path);
                var loaders = gameplay.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GameplayUILoader>(true)).ToArray();
                Assert.That(loaders.Length, Is.EqualTo(1));
                ui = loaders[0].Instance;
                Assert.That(ui, Is.Not.Null);
                Assert.That(ui.gameObject.scene, Is.EqualTo(gameplay));
                var debugPanels = ui.GetComponentsInChildren<NetworkEnemyDebugPanel>(true);
                Assert.That(debugPanels.Length, Is.EqualTo(1), "Production Gameplay must load the Enemy Debug panel.");
                var debugPanel = debugPanels[0];
                Assert.That(debugPanel.isActiveAndEnabled, Is.True);
                Assert.That(debugPanel.transform, Is.EqualTo(ui.transform), "Debug lives outside the HP and selection subtrees.");
                Assert.That(debugPanel.Expanded, Is.True);
                Assert.That(debugPanel.Rows, Is.Empty);
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(originalScene),
                    "The loader must not depend on the newly loaded scene being active.");
                Assert.That(gameplay.GetRootGameObjects().SelectMany(root =>
                    root.GetComponentsInChildren<GameplayUIRoot>(true)).Count(), Is.EqualTo(1));
                loaders[0].enabled = false;
                loaders[0].enabled = true;
                yield return null;
                Assert.That(loaders[0].Instance, Is.SameAs(ui));
                ui.GetComponentInChildren<CombatHUDController>().Bind(combatant);
                Assert.That(HealthSubscribers(combatant), Is.EqualTo(1));
                yield return SceneManager.UnloadSceneAsync(gameplay);
                Assert.That(ui == null, Is.True);
                Assert.That(debugPanel == null, Is.True);
                Assert.That(HealthSubscribers(combatant), Is.Zero);
            }
            finally
            {
                if (gameplay.IsValid() && gameplay.isLoaded)
                    SceneManager.UnloadSceneAsync(gameplay);
                Object.DestroyImmediate(playerObject);
            }
        }

        private static int HealthSubscribers(CombatantBehaviour combatant)
        {
            var handlers = (System.Delegate)typeof(CombatantBehaviour)
                .GetField("HealthChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(combatant);
            return handlers?.GetInvocationList().Length ?? 0;
        }
    }
}
#endif
