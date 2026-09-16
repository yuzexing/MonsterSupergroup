using UnityEngine;
using UnityEngine.Rendering;

namespace MonsterSupergroup.Gameplay.Combat
{
    public static class GameplayMapPresentation
    {
        public static void Body(Transform visual, Transform feet)
        {
            if (visual == null || GameplayMapContext.For(feet.gameObject) == null) return;
            Transform anchor;
            if (visual.parent != null && visual.parent.name == "World Y Sort") anchor = visual.parent;
            else
            {
                anchor = new GameObject("World Y Sort").transform;
                anchor.SetParent(feet, false);
                visual.SetParent(anchor, true);
            }
            var group = anchor.GetComponent<SortingGroup>();
            if (group == null) group = anchor.gameObject.AddComponent<SortingGroup>();
            group.sortingLayerName = "Props"; group.sortingOrder = 0;
        }
        public static void Effect(GameObject root)
        {
            if (root == null || GameplayMapContext.Active == null) return;
            GameplayPlanarEffect.Attach(root);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer.sortingLayerName != "UI" && renderer.sortingLayerName != "EnemyAttack") renderer.sortingLayerName = "Foreground";
            foreach (var group in root.GetComponentsInChildren<SortingGroup>(true))
                if (group.sortingLayerName != "UI" && group.sortingLayerName != "EnemyAttack") group.sortingLayerName = "Foreground";
        }
        public static void Warning(GameObject root)
        {
            if (root == null || GameplayMapContext.Active == null) return;
            GameplayPlanarEffect.Attach(root);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.sortingLayerName = "EnemyAttack";
            foreach (var group in root.GetComponentsInChildren<SortingGroup>(true)) group.sortingLayerName = "EnemyAttack";
        }
    }
}
