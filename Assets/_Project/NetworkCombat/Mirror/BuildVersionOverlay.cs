using MonsterSupergroup.Builds;
using MonsterSupergroup.Gameplay.Options;
using UnityEngine;
using UnityEngine.UI;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>One non-interactive footer, independent of debug panels and scene HUD visibility.</summary>
    public sealed class BuildVersionOverlay : MonoBehaviour
    {
        private static BuildVersionOverlay instance;
        private Text label;
        private RectTransform safeArea;
        private Rect lastSafe;
        private int width, height;
        public string VisibleText => label != null ? label.text : "";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => instance = null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null || MonsterSupergroup.Gameplay.Combat.GameplayRuntimeEnvironment.IsDedicatedServer) return;
            var root = new GameObject("Build version", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(root); instance = root.AddComponent<BuildVersionOverlay>();
        }
        private void Awake()
        {
            var canvas = GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 3100;
            var scaler = GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            safeArea = new GameObject("Safe area", typeof(RectTransform)).GetComponent<RectTransform>(); safeArea.SetParent(transform, false);
            var text = new GameObject("Version", typeof(RectTransform), typeof(Text)); text.transform.SetParent(safeArea, false);
            label = text.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 14; label.alignment = TextAnchor.LowerRight; label.color = new Color(.85f, .88f, .9f, .9f);
            label.raycastTarget = false; label.supportRichText = false; label.text = RuntimeBuildInfo.Display;
            var rect = (RectTransform)text.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.right;
            rect.anchoredPosition = new Vector2(-16, 10); rect.sizeDelta = new Vector2(760, 24);
            GameLocalization.Changed += ApplyFont; ApplyFont(); UpdateSafeArea();
            Debug.Log("[BuildInfo] version-ui=" + label.text);
        }
        private void ApplyFont() { if (GameLocalization.UIFont != null) label.font = GameLocalization.UIFont; }
        private void Update() { if (lastSafe != Screen.safeArea || width != Screen.width || height != Screen.height) UpdateSafeArea(); }
        private void UpdateSafeArea()
        {
            width = Mathf.Max(1, Screen.width); height = Mathf.Max(1, Screen.height); lastSafe = Screen.safeArea;
            safeArea.anchorMin = new Vector2(lastSafe.xMin / width, lastSafe.yMin / height);
            safeArea.anchorMax = new Vector2(lastSafe.xMax / width, lastSafe.yMax / height);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }
        private void OnDestroy() { GameLocalization.Changed -= ApplyFont; if (instance == this) instance = null; }
    }
}
