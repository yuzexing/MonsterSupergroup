using MonsterSupergroup.Gameplay.Options;
using System;
using Loc = MonsterSupergroup.Gameplay.Options.MenuLocalization;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkGameplayMenuController
    {
        private static readonly Color Ink = new Color32(15, 23, 29, 255);
        private static readonly Color Panel = new Color32(28, 40, 46, 255);
        private static readonly Color Selected = new Color32(75, 67, 48, 255);
        private static readonly Color Cream = new Color32(243, 233, 208, 255);
        private static readonly Color Muted = new Color32(148, 165, 168, 255);
        private static readonly Color Accent = new Color32(222, 174, 92, 255);
        private Font font;
        private GameObject canvasRoot, menuPanel, confirmPanel, weaponArea;
        private CanvasGroup menuGroup;
        private Button entryButton, continueButton, optionsButton, feedbackButton, exitButton;
        private Button cancelButton, confirmButton, characterTab, spellTab;
        private readonly Button[] slotButtons = new Button[4];
        private readonly Text[] slotLabels = new Text[4];
        private readonly Image[] slotIcons = new Image[4];
        private readonly Text[] slotMarks = new Text[4];
        private Text characterText, weaponText, confirmMessage, mapCaption;
        private ScrollRect characterScroll, weaponScroll;

        private void BuildView()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 24);
            canvasRoot = new GameObject("Gameplay ESC Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasRoot, gameObject.scene);
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 3000;
            var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            entryButton = MakeButton(canvasRoot.transform, "CombatMenu.Open", "菜单 [Esc]", new Rect(24, 118, 152, 44), OpenMenu);

            menuPanel = Box(canvasRoot.transform, "CombatMenu.Overlay", default, new Color(0.025f, 0.045f, 0.065f, 0.91f)).gameObject;
            Stretch((RectTransform)menuPanel.transform); menuPanel.GetComponent<Image>().raycastTarget = true;
            menuGroup = menuPanel.AddComponent<CanvasGroup>();
            var frame = Frame(menuPanel.transform, "Menu frame");
            Label(frame, "本局菜单", new Rect(64, 56, 340, 56), 38, Cream);
            mapCaption = Label(frame, "", new Rect(448, 68, 736, 40), 21, Muted);
            mapCaption.text = Loc.Get("{0}    /    难度 · 中等", Loc.Get(catalog.MapName));
            Box(frame, "Divider", new Rect(64, 128, 1152, 1), new Color(1, 1, 1, 0.16f));
            continueButton = MakeButton(frame, "CombatMenu.Continue", "继续游戏", new Rect(64, 178, 304, 62), CloseMenu);
            optionsButton = MakeButton(frame, "CombatMenu.Options", "选项", new Rect(64, 256, 304, 62), OpenOptions);
            feedbackButton = MakeButton(frame, "CombatMenu.Feedback", "反馈意见", new Rect(64, 334, 304, 62), () => { });
            exitButton = MakeButton(frame, "CombatMenu.Exit", "退出本局", new Rect(64, 448, 304, 62), RequestExit);
            Label(frame, "战斗仍在继续", new Rect(64, 550, 320, 36), 24, Accent);
            Label(frame, "角色仍可能受伤，自动攻击继续。", new Rect(64, 592, 320, 48), 16, Muted);
            var stats = Box(frame, "Current attributes", new Rect(416, 160, 800, 480), Panel);
            characterTab = MakeButton(stats, "CombatMenu.CharacterTab", "角色统计", new Rect(24, 20, 364, 44), () => SetPage(false));
            spellTab = MakeButton(stats, "CombatMenu.SpellTab", "法术统计", new Rect(404, 20, 372, 44), () => SetPage(true));
            characterScroll = MakeScroll(stats, "Character statistics", new Rect(24, 86, 752, 370), out characterText);
            weaponArea = Box(stats, "Weapon statistics", new Rect(24, 84, 752, 372), Panel).gameObject;
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slot = i;
                slotButtons[i] = MakeButton(weaponArea.transform, "CombatMenu.Slot" + (i + 1), "", new Rect(i * 190, 0, 182, 84), () => SelectWeapon(slot));
                slotLabels[i] = slotButtons[i].GetComponentInChildren<Text>();
                Position((RectTransform)slotLabels[i].transform, new Rect(50, 6, 124, 72));
                slotLabels[i].fontSize = 16; slotLabels[i].alignment = TextAnchor.MiddleLeft;
                slotLabels[i].resizeTextForBestFit = true; slotLabels[i].resizeTextMinSize = 11; slotLabels[i].resizeTextMaxSize = 16;
                slotIcons[i] = Box(slotButtons[i].transform, "Icon", new Rect(8, 24, 36, 36), Color.white).GetComponent<Image>();
                slotIcons[i].preserveAspect = true;
                slotMarks[i] = Label(slotButtons[i].transform, "", new Rect(8, 24, 36, 36), 27, Accent);
                slotMarks[i].alignment = TextAnchor.MiddleCenter;
            }
            weaponScroll = MakeScroll(weaponArea.transform, "Selected weapon", new Rect(0, 104, 752, 268), out weaponText);
            Label(frame, "Esc 继续    ·    方向键 / Tab 选择    ·    Enter 确认    ·    滚轮查看属性", new Rect(416, 658, 800, 30), 16, Muted);

            confirmPanel = Box(canvasRoot.transform, "CombatMenu.Confirmation", default, new Color(0, 0, 0, 0.76f)).gameObject;
            Stretch((RectTransform)confirmPanel.transform); confirmPanel.GetComponent<Image>().raycastTarget = true;
            var confirmFrame = Frame(confirmPanel.transform, "Confirm frame");
            var dialog = Box(confirmFrame, "Exit dialog", new Rect(334, 214, 612, 292), Ink);
            Box(dialog, "Accent", new Rect(0, 0, 612, 3), Accent);
            Label(dialog, "离开本局", new Rect(36, 30, 540, 44), 28, Cream);
            confirmMessage = Label(dialog, "", new Rect(36, 94, 540, 86), 21, Cream);
            cancelButton = MakeButton(dialog, "CombatMenu.CancelExit", "取消", new Rect(36, 206, 256, 50), CancelExit);
            confirmButton = MakeButton(dialog, "CombatMenu.ConfirmExit", "确认退出", new Rect(316, 206, 260, 50), ConfirmExit);
            confirmPanel.SetActive(false); menuPanel.SetActive(false);
            SetPage(false);
        }

        private void RefreshStatistics()
        {
            if (Snapshot == null) return;
            var s = Snapshot;
            var text = new StringBuilder(Loc.Get("{0}    ·    当前属性", Loc.Get(s.CharacterName))).AppendLine().AppendLine();
            text.AppendLine(s.HealthState == MenuDataState.Ready ? Loc.Get("生命    {0} / {1}       {2}", s.Health, s.MaxHealth, Loc.Get(s.Alive ? "存活 · Alive" : "倒地 · Downed")) : Loc.Get("生命    ") + StateText(s.HealthState));
            text.AppendLine(s.ProgressState == MenuDataState.Ready ? Loc.Get("等级    {0}       经验    {1} / {2}", s.Level, N(s.Experience), N(s.ExperienceRequired)) : Loc.Get("等级 / 经验    ") + StateText(s.ProgressState));
            if (s.AttributesState == MenuDataState.Ready)
            {
                text.AppendLine(Loc.Get("移动速度    {0}", N(s.MoveSpeed))).AppendLine(Loc.Get("固定减伤    {0}", N(s.DamageReduction)));
                text.AppendLine(Loc.Get("拾取范围    {0}", N(s.PickupRange))).AppendLine(Loc.Get("经验倍率    ×{0}", N(s.ExperienceMultiplier)));
                text.AppendLine().AppendLine(Loc.Get("冲刺属性")).AppendLine(Loc.Get("距离    {0}       速度    {1}", N(s.DashDistance), N(s.DashSpeed)));
                text.AppendLine(Loc.Get("恢复间隔    {0} 秒", N(s.DashInterval)));
            }
            else text.AppendLine(Loc.Get("角色 / 冲刺属性    ") + StateText(s.AttributesState));
            text.Append(Loc.Get("冲刺次数    ")).Append(s.DashAvailable.HasValue ? $"{s.DashAvailable} / {s.DashMaximum}" : StateText(s.DashState));
            SetScrollText(characterScroll, characterText, text.ToString());
            for (int i = 0; i < slotButtons.Length; i++)
            {
                var row = s.Weapons[i];
                slotLabels[i].text = Loc.Get("槽位 {0}{1}\n{2}", i + 1, row.Initial ? Loc.Get(" · 初始") : "",
                    row.State == MenuDataState.Ready ? ContentText.Name(LocalizedContentKind.Weapon, row.Id) : StateText(row.State));
                slotIcons[i].sprite = row.Icon; slotIcons[i].enabled = row.Icon != null;
                slotMarks[i].text = row.Icon != null ? "" : row.State == MenuDataState.Empty ? "—" :
                    !string.IsNullOrEmpty(row.IconMark) ? Loc.Get(row.IconMark) : row.Id != 0 ? row.Id.ToString() : "…";
                SetButtonFill(slotButtons[i], i == selectedSlot ? Selected : Ink);
            }
            var weapon = s.Weapons[selectedSlot]; text.Clear();
            if (weapon.State != MenuDataState.Ready) text.Append(StateText(weapon.State));
            else
            {
                text.AppendLine(ContentText.Name(LocalizedContentKind.Weapon, weapon.Id) + (weapon.Initial ? Loc.Get(" · 初始武器") : "")).AppendLine(Loc.Get("当前属性"));
                text.AppendLine(Loc.Get("伤害    {0}       攻速倍率    {1}", Value(weapon.Damage), Value(weapon.AttackSpeed, "×")));
                text.AppendLine(Loc.Get("暴击率    {0}       暴击倍率    {1}", Value(weapon.CritRate * 100, "", "%"), Value(weapon.CritMultiplier, "×")));
                text.AppendLine(weapon.UsesDash ? Loc.Get("使用冲刺次数") : Loc.Get("冷却剩余    {0} 秒", Value(weapon.CooldownRemaining)));
                text.AppendLine().AppendLine(Loc.Get("武器装备"));
                if (weapon.Equipment.Count == 0) text.AppendLine(Loc.Get("无装备"));
                foreach (var item in weapon.Equipment) text.AppendLine(Loc.Get("{0}    等级 {1}", ContentText.Name(LocalizedContentKind.Equipment, item.Id), item.Level));
                text.AppendLine().Append(Loc.Get("数值随当前构筑变化；不包含目标条件和命中触发效果。"));
            }
            SetScrollText(weaponScroll, weaponText, text.ToString());
        }
        public static string StateText(MenuDataState state) => Loc.Get(state == MenuDataState.Empty ? "空槽位" : state == MenuDataState.ComponentUnavailable ? "组件未就绪" : "同步中");
        private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Value(float? value, string prefix = "", string suffix = "") => value.HasValue ? prefix + N(value.Value) + suffix : Loc.Get("组件未就绪");
        private static void SetScrollText(ScrollRect scroll, Text label, string value)
        {
            if (label.text == value) return;
            float position = string.IsNullOrEmpty(label.text) || scroll.content.rect.height <= scroll.viewport.rect.height + .01f
                ? 1f : scroll.verticalNormalizedPosition;
            label.text = value;
            var rect = (RectTransform)label.transform;
            rect.sizeDelta = new Vector2(0, Mathf.Max(scroll.viewport.rect.height, label.preferredHeight + 12));
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = position;
        }
        private ScrollRect MakeScroll(Transform parent, string name, Rect bounds, out Text label)
        {
            var root = Box(parent, name, bounds, Panel);
            var scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
            var viewport = Box(root, "Viewport", new Rect(0, 0, bounds.width - 24, bounds.height), Panel);
            viewport.GetComponent<Image>().raycastTarget = true; viewport.gameObject.AddComponent<RectMask2D>();
            label = Label(viewport, "", new Rect(0, 0, bounds.width - 24, bounds.height), 21, Cream);
            label.lineSpacing = 1.28f;
            var content = (RectTransform)label.transform; content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.sizeDelta = new Vector2(0, bounds.height);
            scroll.viewport = viewport; scroll.content = content;
            var bar = Box(root, "Scrollbar", new Rect(bounds.width - 14, 0, 14, bounds.height), Ink);
            var handle = Box(bar, "Handle", default, Muted); Stretch(handle);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>();
            bar.GetComponent<Image>().raycastTarget = true; handle.GetComponent<Image>().raycastTarget = true;
            scroll.verticalScrollbar = scrollbar; scroll.verticalNormalizedPosition = 1;
            return scroll;
        }
        private static RectTransform Frame(Transform parent, string name)
        {
            var frame = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); frame.SetParent(parent, false);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f); frame.sizeDelta = new Vector2(1280, 720);
            return frame;
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero; rect.anchoredPosition = Vector2.zero;
        }
        private static void Position(RectTransform rect, Rect bounds)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(bounds.x, -bounds.y); rect.sizeDelta = bounds.size;
        }
        private static RectTransform Box(Transform parent, string name, Rect bounds, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); Position(rect, bounds);
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            return rect;
        }
        private Text Label(Transform parent, string text, Rect bounds, int size, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            Position(go.GetComponent<RectTransform>(), bounds); var label = go.GetComponent<Text>();
            label.font = GameLocalization.UIFont != null ? GameLocalization.UIFont : font; label.fontSize = size; label.text = text; label.color = color;
            if (!string.IsNullOrEmpty(text)) Loc.Bind(label, text);
            label.raycastTarget = false; label.supportRichText = false; label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }
        private Button MakeButton(Transform parent, string name, string title, Rect bounds, UnityEngine.Events.UnityAction action)
        {
            var rect = Box(parent, name, bounds, Color.white); rect.GetComponent<Image>().raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors; colors.normalColor = Panel; colors.highlightedColor = new Color32(90, 80, 55, 255);
            colors.selectedColor = new Color32(102, 86, 54, 255); colors.pressedColor = Selected; button.colors = colors;
            var text = Label(rect, title, new Rect(12, 0, bounds.width - 24, bounds.height), 22, Cream); text.alignment = TextAnchor.MiddleCenter;
            button.onClick.AddListener(action); return button;
        }
        private static void SetButtonFill(Button button, Color color)
        {
            var colors = button.colors;
            if (colors.normalColor == color) return;
            colors.normalColor = color; button.colors = colors;
        }
    }
}
