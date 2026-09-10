using UnityEngine;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.UI
{
    /// <summary>Scene-owned presentation; the network adapter supplies all displayed progress.</summary>
    [DisallowMultipleComponent]
    public sealed class WaveProgressHUD : MonoBehaviour
    {
        private Text label;
        private GameObject panel;
        public string Content => label != null ? label.text : string.Empty;

        private void Awake()
        {
            panel = new GameObject("Wave Progress", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -12);
            rect.sizeDelta = new Vector2(560, 78);
            var background = panel.GetComponent<Image>();
            background.color = new Color(0.04f, 0.055f, 0.08f, 0.86f);
            background.raycastTarget = false;
            var textObject = new GameObject("Progress", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12, 6); textRect.offsetMax = new Vector2(-12, -6);
            label = textObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 24;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
        }
        public void Present(string content)
        {
            if (label != null) label.text = content;
            if (panel != null) panel.SetActive(isActiveAndEnabled && !string.IsNullOrEmpty(content));
        }
        private void OnDisable() { if (panel != null) panel.SetActive(false); }
        private void OnDestroy() { if (panel != null) Destroy(panel); }
    }
}
