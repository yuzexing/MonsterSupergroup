using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Options
{
    /// <summary>Shared modal used by preparation and combat menus; the caller retains ownership of gameplay input.</summary>
    public sealed class GameOptionsPanel : MonoBehaviour
    {
        private static GameOptionsPanel active;
        public static bool IsOpen => active != null;
        public static int LastClosedFrame { get; private set; } = -1;
        public GameOptionsData Draft => draft.Copy();
        private GameOptionsService service;
        private GameOptionsData draft;
        private Action closed;
        private Font font;
        private RectTransform frame;
        private CanvasGroup pageGroup;
        private GameObject confirmation;
        private Text countdown, status;
        private Button back, apply, revert;
        private int tab;
        private int lastBackFrame = -1;
        private bool closing, wasConfirming, rebuildPending;
        private readonly List<(Dropdown dropdown, string[] values)> translatedChoices = new();
        private readonly List<Selectable> navigation = new List<Selectable>();
        private readonly Color ink = new Color32(15, 23, 29, 255), panel = new Color32(28, 40, 46, 255);
        private readonly Color cream = new Color32(243, 233, 208, 255), accent = new Color32(222, 174, 92, 255);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { active = null; LastClosedFrame = -1; }

        public static GameOptionsPanel Open(Transform parent, Font sharedFont, Action onClose)
        {
            if (active != null) return active;
            var service = GameOptionsService.EnsureInitialized();
            if (service == null) return null;
            var go = new GameObject("Game options", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>(); image.color = new Color32(15, 23, 29, 255); image.raycastTarget = true;
            active = go.AddComponent<GameOptionsPanel>();
            active.service = service; active.font = sharedFont; active.closed = onClose; active.draft = service.Current;
            MenuLocalization.Changed += active.QueueRebuild;
            GameOptionsService.Changed += active.OnSettingsChanged;
            active.Rebuild(); return active;
        }

        public static void HandleBack() { if (active != null) active.Back(); }
        public static void CloseFor(Transform parent)
        { if (active != null && active.transform.IsChildOf(parent)) active.Close(); }

        private void Back()
        {
            if (lastBackFrame == Time.frameCount) return;
            lastBackFrame = Time.frameCount;
            if (service.IsDisplayConfirmationPending) service.RevertDisplay();
            else Close();
        }

        public void Close()
        {
            if (closing) return;
            closing = true;
            MenuLocalization.Changed -= QueueRebuild; GameOptionsService.Changed -= OnSettingsChanged;
            service.RevertDisplay(); LastClosedFrame = Time.frameCount;
            if (active == this) active = null;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(null);
            gameObject.SetActive(false); closed?.Invoke(); Destroy(gameObject);
        }

        private void OnDisable() { if (!closing && service != null) Close(); }

        // Refresh existing controls after Dropdown.Hide; retain focus and any uncommitted graphics draft.
        private void QueueRebuild() => rebuildPending = true;
        private void LateUpdate()
        {
            if (!rebuildPending) return;
            rebuildPending = false;
            foreach (var choice in translatedChoices)
            {
                for (int i = 0; i < choice.values.Length; i++) choice.dropdown.options[i].text = MenuLocalization.Get(choice.values[i]);
                choice.dropdown.RefreshShownValue();
            }
            OnSettingsChanged();
        }

        private void Rebuild()
        {
            rebuildPending = false;
            if (closing) return;
            var selection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            string focus = selection != null ? selection.name : null;
            foreach (Transform child in transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            navigation.Clear(); translatedChoices.Clear();
            frame = Box(transform, "Options frame", new Rect(0, 0, 1280, 720), Color.clear);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f); frame.anchoredPosition = Vector2.zero;
            Text(frame, "选项", new Rect(64, 28, 550, 52), 36);
            var page = Box(frame, "Options page", new Rect(0, 0, 1280, 720), Color.clear);
            pageGroup = page.gameObject.AddComponent<CanvasGroup>();
            string[] titles = { "常规", "音频", "画面" };
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                Button(page, "Options.Tab." + i, titles[i], new Rect(64 + i * 240, 96, 224, 44),
                    () => { tab = index; Rebuild(); }, tab == i);
            }
            if (tab == 0) BuildGeneral(page);
            else if (tab == 1) BuildAudio(page);
            else BuildGraphics(page);
            status = Text(page, "", new Rect(64, 566, 1152, 48), 16);
            back = Button(page, "Options.Back", "返回", new Rect(64, 636, 220, 48), Back);
            Button(page, "Options.Reset", "恢复此页默认值", new Rect(520, 636, 300, 48), () => {
                if (tab == 0) service.ResetGeneral();
                else if (tab == 1) service.ResetAudio();
                else draft.CopyGraphicsFrom(service.Defaults);
                Rebuild();
            });
            apply = null;
            if (tab == 2) apply = Button(page, "Options.Apply", "应用", new Rect(916, 636, 300, 48), () => {
                service.ApplyGraphics(draft); draft = service.Current; OnSettingsChanged();
            }, true);
            BuildConfirmation();
            OnSettingsChanged(); WireNavigation();
            var selected = navigation.FirstOrDefault(s => s != null && s.name == focus && s.interactable);
            (selected != null ? selected : navigation.First()).Select();
            if (service.IsDisplayConfirmationPending) revert.Select();
        }

        private void BuildGeneral(Transform parent)
        {
            var locales = GameLocalization.Locales.ToArray();
            Choice(parent, "Options.Language", "语言", 180, locales.Select(l => l.LocaleName).ToArray(),
                Math.Max(0, Array.FindIndex(locales, l => l.Identifier.Code == service.Current.Language)),
                value => service.SetLanguage(locales[value].Identifier.Code), false);
            Choice(parent, "Options.Shake", "镜头震动", 246, new[] { "关闭", "开启" },
                service.Current.ScreenShake ? 1 : 0, value => service.SetScreenShake(value == 1));
            Text(parent, "切换语言后，菜单立即更新。镜头震动仅影响本机画面。", new Rect(64, 344, 1120, 80), 19);
        }

        private void BuildAudio(Transform parent)
        {
            var values = service.Current;
            Slider(parent, "Options.Master", "总音量", 180, values.MasterVolume, 0, 1, v => {
                var s = service.Current; service.SetAudio(v, s.MusicVolume, s.EffectsVolume);
            });
            Slider(parent, "Options.Music", "音乐", 246, values.MusicVolume, 0, 1, v => {
                var s = service.Current; service.SetAudio(s.MasterVolume, v, s.EffectsVolume);
            });
            Slider(parent, "Options.Effects", "音效", 312, values.EffectsVolume, 0, 1, v => {
                var s = service.Current; service.SetAudio(s.MasterVolume, s.MusicVolume, v);
            });
            Text(parent, "音效控制武器、受伤、环境和菜单声音。修改后立即生效。", new Rect(64, 410, 1120, 80), 19);
        }

        private void BuildGraphics(Transform parent)
        {
            var modes = new[] { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen };
            var display = Choice(parent, "Options.DisplayMode", "显示模式", 160, new[] { "窗口", "无边框全屏", "独占全屏" },
                Math.Max(0, Array.IndexOf(modes, draft.DisplayMode)), value => draft.DisplayMode = modes[value]);
            display.interactable = GameOptionsService.DisplayChangesSupported;
            var resolutions = service.AvailableResolutions();
            var resolution = Choice(parent, "Options.Resolution", "分辨率", 214,
                resolutions.Select(r => $"{r.width} × {r.height}  ·  {r.refreshRateRatio.value:0.##} Hz").ToArray(),
                Math.Max(0, resolutions.FindIndex(r => GameOptionsService.MatchesResolution(draft, r))), value => {
                    var r = resolutions[value]; draft.Width = r.width; draft.Height = r.height;
                    draft.RefreshNumerator = r.refreshRateRatio.numerator; draft.RefreshDenominator = r.refreshRateRatio.denominator;
                }, false);
            resolution.interactable = GameOptionsService.DisplayChangesSupported;
            Choice(parent, "Options.VSync", "垂直同步", 268, new[] { "关闭", "开启" }, draft.VSync ? 1 : 0,
                value => { draft.VSync = value == 1; QueueRebuild(); });
            var frames = Choice(parent, "Options.FrameLimit", "帧率上限", 322,
                GameOptionsService.FrameLimits.Select(v => v < 0 ? "不限制" : v.ToString()).ToArray(),
                Math.Max(0, Array.IndexOf(GameOptionsService.FrameLimits, draft.FrameLimit)), v => draft.FrameLimit = GameOptionsService.FrameLimits[v]);
            frames.interactable = !draft.VSync;
            var scale = Slider(parent, "Options.RenderScale", "渲染比例", 376, draft.RenderScale, .5f, 1.5f,
                value => draft.RenderScale = value);
            scale.interactable = service.RuntimePipeline != null;
            var msaa = service.SupportedMsaa();
            var aa = Choice(parent, "Options.Msaa", "抗锯齿", 430, msaa.Select(v => v == 1 ? "关闭" : $"MSAA {v}×").ToArray(),
                Math.Max(0, Array.IndexOf(msaa, draft.Msaa)), v => draft.Msaa = msaa[v]);
            aa.interactable = msaa.Length > 1;
            Choice(parent, "Options.Texture", "纹理质量", 484, new[] { "原始", "二分之一", "四分之一" }, draft.TextureLimit, v => draft.TextureLimit = v);
        }

        private void OnSettingsChanged()
        {
            if (closing || status == null) return;
            bool pending = service.IsDisplayConfirmationPending;
            if (wasConfirming && !pending) draft = service.Current;
            confirmation.SetActive(pending); pageGroup.interactable = !pending;
            if (pending && !wasConfirming) revert.Select();
            else if (!pending && wasConfirming) RebuildAfterConfirmation();
            wasConfirming = pending;
            if (tab == 1) status.text = service.AudioAvailable ? "" : MenuLocalization.Get("音频正在初始化，已保存的音量会在就绪后应用。");
            else if (tab == 2) status.text = MenuLocalization.Get(draft.VSync ? "垂直同步已开启，帧率由显示器刷新率控制。" : "降低渲染比例可提升性能；纹理质量仅影响支持 mipmap 的纹理。") +
                (GameOptionsService.DisplayChangesSupported ? "" : "\n" + MenuLocalization.Get("编辑器中无法切换窗口模式与分辨率，请在独立运行版本中调整。"));
            else status.text = "";
            var frameChoice = navigation.FirstOrDefault(s => s != null && s.name == "Options.FrameLimit");
            if (frameChoice != null) frameChoice.interactable = !draft.VSync && !pending;
            if (apply != null) apply.interactable = !pending && !draft.SameGraphics(service.Current);
        }

        private void RebuildAfterConfirmation() { wasConfirming = false; Rebuild(); }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && LastClosedFrame != Time.frameCount) Back();
            if (closing) return;
            if (service.IsDisplayConfirmationPending)
                countdown.text = MenuLocalization.Get("保留当前显示设置？\n{0} 秒后恢复原设置。", Mathf.CeilToInt(service.DisplaySecondsRemaining));
            else if (apply != null) apply.interactable = !draft.SameGraphics(service.Current);
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                var usable = service.IsDisplayConfirmationPending
                    ? confirmation.GetComponentsInChildren<Selectable>().Where(s => s.interactable).ToList()
                    : navigation.Where(s => s != null && s.interactable).ToList();
                int index = usable.FindIndex(s => s.gameObject == EventSystem.current?.currentSelectedGameObject);
                int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
                if (usable.Count > 0) usable[(index + direction + usable.Count) % usable.Count].Select();
            }
        }

        private void BuildConfirmation()
        {
            var overlay = Box(frame, "Options.DisplayConfirmation", new Rect(0, 0, 1280, 720), new Color(0, 0, 0, .88f));
            overlay.GetComponent<Image>().raycastTarget = true; confirmation = overlay.gameObject;
            var box = Box(overlay, "Confirm frame", new Rect(330, 225, 620, 270), panel);
            countdown = Text(box, "", new Rect(32, 26, 556, 120), 25);
            revert = Button(box, "Options.RevertDisplay", "恢复原设置", new Rect(32, 178, 260, 52), service.RevertDisplay);
            Button(box, "Options.KeepDisplay", "保留设置", new Rect(328, 178, 260, 52), service.ConfirmDisplay, true);
            navigation.RemoveRange(navigation.Count - 2, 2);
            confirmation.SetActive(false);
        }

        private RectTransform Box(Transform parent, string name, Rect bounds, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(bounds.x, -bounds.y); rect.sizeDelta = bounds.size;
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return rect;
        }

        private Text Text(Transform parent, string key, Rect bounds, int size)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(bounds.x, -bounds.y); rect.sizeDelta = bounds.size;
            var label = go.GetComponent<Text>();
            label.font = GameLocalization.UIFont != null ? GameLocalization.UIFont : font; label.fontSize = size; label.color = cream; label.raycastTarget = false;
            label.supportRichText = false; label.alignment = TextAnchor.MiddleLeft;
            MenuLocalization.Bind(label, key); return label;
        }

        private Button Button(Transform parent, string name, string key, Rect bounds, Action action, bool primary = false)
        {
            var rect = Box(parent, name, bounds, primary ? accent : panel); rect.GetComponent<Image>().raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var label = Text(rect, key, new Rect(12, 0, bounds.width - 24, bounds.height), 21);
            label.alignment = TextAnchor.MiddleCenter; if (primary) label.color = ink;
            button.onClick.AddListener(() => action()); navigation.Add(button); return button;
        }

        private Dropdown Choice(Transform parent, string name, string key, float y, string[] values, int selected, Action<int> change, bool localize = true)
        {
            Text(parent, key, new Rect(64, y, 330, 42), 21);
            var root = Box(parent, name, new Rect(430, y, 786, 42), panel); root.GetComponent<Image>().raycastTarget = true;
            var dropdown = root.gameObject.AddComponent<Dropdown>(); dropdown.targetGraphic = root.GetComponent<Image>();
            dropdown.captionText = Text(root, "", new Rect(16, 0, 720, 42), 20);
            Text(root, "▾", new Rect(748, 0, 28, 42), 21);
            var template = Box(root, "Template", new Rect(0, 42, 786, 220), panel);
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 32;
            var viewport = Box(template, "Viewport", new Rect(0, 0, 786, 220), panel); viewport.gameObject.AddComponent<RectMask2D>();
            var items = Box(viewport, "Content", new Rect(0, 0, 786, 42), Color.clear);
            var item = Box(items, "Item", new Rect(0, 0, 786, 42), panel); item.GetComponent<Image>().raycastTarget = true;
            var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item.GetComponent<Image>();
            var check = Text(item, "✓", new Rect(8, 0, 32, 42), 20); toggle.graphic = check;
            dropdown.itemText = Text(item, "", new Rect(48, 0, 722, 42), 20);
            dropdown.template = template; scroll.viewport = viewport; scroll.content = items;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            template.gameObject.SetActive(false);
            dropdown.AddOptions(values.Select(v => localize ? MenuLocalization.Get(v) : v).ToList());
            if (localize) translatedChoices.Add((dropdown, values));
            dropdown.SetValueWithoutNotify(selected);
            dropdown.onValueChanged.AddListener(v => { change(v); OnSettingsChanged(); });
            navigation.Add(dropdown); return dropdown;
        }

        private Slider Slider(Transform parent, string name, string key, float y, float value, float min, float max, Action<float> change)
        {
            Text(parent, key, new Rect(64, y, 330, 42), 21);
            var root = Box(parent, name, new Rect(430, y, 660, 42), Color.clear);
            var track = Box(root, "Track", new Rect(0, 15, 660, 12), panel); track.GetComponent<Image>().raycastTarget = true;
            var fillArea = Box(root, "Fill area", new Rect(0, 15, 660, 12), Color.clear);
            var fill = Box(fillArea, "Fill", new Rect(0, 0, 660, 12), accent);
            // Slider drives stretched anchors; fixed size deltas would double the fill and handle dimensions.
            fill.sizeDelta = Vector2.zero; fill.anchoredPosition = Vector2.zero;
            var area = Box(root, "Handle area", new Rect(9, 4, 642, 34), Color.clear);
            var handle = Box(area, "Handle", new Rect(0, 0, 18, 0), cream);
            handle.pivot = new Vector2(.5f, .5f); handle.anchoredPosition = Vector2.zero;
            handle.GetComponent<Image>().raycastTarget = true;
            var slider = root.gameObject.AddComponent<Slider>(); slider.fillRect = fill; slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>(); slider.minValue = min; slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            var number = Text(parent, "", new Rect(1110, y, 106, 42), 21);
            number.text = Mathf.RoundToInt(value * 100) + "%";
            slider.onValueChanged.AddListener(v => { number.text = Mathf.RoundToInt(v * 100) + "%"; change(v); });
            navigation.Add(slider); return slider;
        }

        private void WireNavigation()
        {
            var usable = navigation.Where(s => s.interactable).ToArray();
            for (int i = 0; i < usable.Length; i++)
            {
                var nav = usable[i].navigation; nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = usable[(i + usable.Length - 1) % usable.Length]; nav.selectOnDown = usable[(i + 1) % usable.Length];
                usable[i].navigation = nav;
            }
        }
    }
}
