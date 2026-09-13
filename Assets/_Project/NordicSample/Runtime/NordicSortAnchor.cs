using UnityEngine;
using UnityEngine.Rendering;

namespace MonsterSupergroup.NordicSample
{
    [ExecuteAlways, RequireComponent(typeof(SortingGroup))]
    public sealed class NordicSortAnchor : MonoBehaviour
    {
        public bool moving;
        public float footOffsetY;
        // Keep 0.01-world-unit foot precision. Slot 0 belongs to the actor; static
        // roots sharing a foot row get distinct slots 1..7, baked by the editor.
        public const int OrderStride = 8;
        [HideInInspector] public int stableTieBreak;
        public int FootRow => -Mathf.RoundToInt((transform.position.y + footOffsetY) * 100f);
        public int Order => FootRow * OrderStride + (moving ? 0 : stableTieBreak);
        private void OnEnable() => RefreshOrder();
        private void OnValidate() => RefreshOrder();
        private void LateUpdate() { if (moving || !Application.isPlaying) RefreshOrder(); }
        public void RefreshOrder()
        {
            var group = GetComponent<SortingGroup>();
            group.sortingLayerName = "Props";
            group.sortingOrder = Order;
        }
    }
}
