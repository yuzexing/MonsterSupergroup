using UnityEngine;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.UI
{
    /// <summary>Owner progression supplied by LocalPlayerUIBinder; no legacy Leveler/global events.</summary>
    public sealed class PlayerExperienceHUD : MonoBehaviour
    {
        [SerializeField] private Texture2D fillTexture;
        [SerializeField] private Texture2D backgroundTexture;
        private Sprite fillSprite, backgroundSprite;
        private GameObject panel;
        private Image fill;
        private Text label;
        public string Content => label != null ? label.text : string.Empty;
        private void Awake()
        {
            // Existing XP textures were exported with empty multiple-sprite tables. Reuse their pixels
            // without changing the shared importer; these two display sprites belong to this HUD only.
            if (fillTexture == null || backgroundTexture == null)
                throw new System.InvalidOperationException("CombatHUD XP textures are missing.");
            fillSprite = Sprite.Create(fillTexture, new Rect(0, 0, fillTexture.width, fillTexture.height), Vector2.one * .5f);
            backgroundSprite = Sprite.Create(backgroundTexture, new Rect(0, 0, backgroundTexture.width, backgroundTexture.height), Vector2.one * .5f);
            panel = new GameObject("Experience", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, 24);
            rect.sizeDelta = new Vector2(540, 68);
            var background = panel.GetComponent<Image>();
            background.color = new Color(.025f, .045f, .06f, .9f); background.raycastTarget = false;
            Image MakeBar(string name, Sprite sprite)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var r = (RectTransform)go.transform; r.SetParent(rect, false);
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(12, 8); r.offsetMax = new Vector2(-12, -42);
                var image = go.GetComponent<Image>(); image.sprite = sprite; image.raycastTarget = false;
                return image;
            }
            MakeBar("Track", backgroundSprite);
            fill = MakeBar("XP", fillSprite);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = 0;
            var text = new GameObject("Level and XP", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var textRect = (RectTransform)text.transform; textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12, 24); textRect.offsetMax = new Vector2(-12, -4);
            label = text.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 24; label.alignment = TextAnchor.MiddleCenter; label.color = Color.white;
            label.raycastTarget = false;
            Clear();
        }
        public void Present(int level, float experience, int threshold)
        {
            if (label == null) return;
            if (level < 1 || threshold < 1) { Clear(); return; }
            label.text = $"Lv. {level}     XP {experience:0.##} / {threshold}";
            fill.fillAmount = Mathf.Clamp01(experience / threshold);
            panel.SetActive(isActiveAndEnabled);
        }
        public void Clear() { if (label != null) label.text = string.Empty; if (panel != null) panel.SetActive(false); }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (panel != null) Destroy(panel);
            if (fillSprite != null) Destroy(fillSprite);
            if (backgroundSprite != null) Destroy(backgroundSprite);
        }
    }
}
