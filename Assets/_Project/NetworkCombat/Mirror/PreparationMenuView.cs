using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Options;
using Loc = MonsterSupergroup.Gameplay.Options.MenuLocalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Presentation only. All room mutations go through authenticated requests.</summary>
    public sealed partial class PreparationMenuView : MonoBehaviour
    {
        private BootGameplayNetworkManager manager;
        private PreparationMenuCatalog catalog;
        private SteamLobbyService steam;
        private Font font;
        private Canvas canvas;
        private Camera menuCamera;
        private AudioListener listener;
        private EventSystem events;
        private RectTransform content;
        private Text notice;
        private bool selectingWeapon;
        private bool destroying;
        private CanvasGroup contentGroup;
        private string renderKey;
        private readonly List<Button> navigation = new List<Button>();
        private static readonly Color Ink = new Color32(15, 23, 29, 255);
        private static readonly Color Panel = new Color32(28, 40, 46, 255);
        private static readonly Color Cream = new Color32(243, 233, 208, 255);
        private static readonly Color Muted = new Color32(148, 165, 168, 255);
        private static readonly Color Accent = new Color32(222, 174, 92, 255);
        private static readonly Color Green = new Color32(123, 196, 154, 255);

        public static PreparationMenuView Install(Scene scene, BootGameplayNetworkManager manager)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var existing = root.GetComponent<PreparationMenuView>();
                if (existing != null) return existing;
            }
            var sceneCamera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            // The old MainMenu Canvas and its direct-Gameplay button are superseded together.
            foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
            var go = new GameObject("MainMenu · Preparation");
            SceneManager.MoveGameObjectToScene(go, scene);
            var view = go.AddComponent<PreparationMenuView>();
            view.Initialize(manager, sceneCamera);
            return view;
        }
        private void Initialize(BootGameplayNetworkManager source, Camera sceneCamera)
        {
            manager = source; catalog = PreparationMenuCatalog.Load(); steam = manager.GetComponent<SteamLobbyService>();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 24);
            // Retain the scene's configured URP camera and renderer data.
            menuCamera = sceneCamera != null ? sceneCamera : throw new InvalidOperationException("MainMenu camera is missing.");
            menuCamera.transform.SetParent(transform, true);
            menuCamera.gameObject.SetActive(true);
            menuCamera.clearFlags = CameraClearFlags.SolidColor; menuCamera.backgroundColor = Ink;
            listener = menuCamera.GetComponent<AudioListener>();
            var eventObject = new GameObject("Menu Input", typeof(EventSystem), typeof(StandaloneInputModule));
            eventObject.transform.SetParent(transform, false); events = eventObject.GetComponent<EventSystem>();
            var canvasObject = new GameObject("Menu Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false); canvas = canvasObject.GetComponent<Canvas>();
            // CombatUI's health/XP canvas uses 1500; the loading page must cover it.
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
            var scale = canvasObject.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1280, 720); scale.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var background = Box(canvas.transform, "Background", new Rect(0, 0, 1280, 720), Ink);
            // Centered reference canvas; wide/narrow aspect ratios retain the whole composition.
            background.anchorMin = background.anchorMax = new Vector2(0.5f, 0.5f);
            background.anchoredPosition = Vector2.zero; background.pivot = new Vector2(0.5f, 0.5f);
            content = Box(background, "Page", new Rect(0, 0, 1280, 720), Color.clear);
            contentGroup = content.gameObject.AddComponent<CanvasGroup>();
            Loc.Changed += OnLanguageChanged;
            manager.PreparationChanged += Invalidate;
            if (steam != null) steam.Changed += Invalidate;
            Render();
        }
        private void Invalidate() => renderKey = null;
        private void OnLanguageChanged()
        {
            // Keep the active search field, caret and scroll alive when localization finishes or changes.
            if (selectingFriends) friendRowsDirty = true;
        }
        private void OpenOptions()
        {
            contentGroup.interactable = false;
            if (localPortInput != null)
            {
                localPortInput.interactable = false;
                localHostButton.interactable = localJoinButton.interactable = localCancelButton.interactable = false;
            }
            GameOptionsPanel.Open(canvas.transform, font, () => {
                if (this == null || destroying || !isActiveAndEnabled) return;
                contentGroup.interactable = true; Render(); Invalidate();
                var option = navigation.FirstOrDefault(b => b != null && b.name == "选项");
                if (option != null && events != null) option.Select();
            });
        }
        private void Update()
        {
            if (manager == null) return;
            UpdateRunEndPresentation();
            bool playing = manager.RoomSnapshot.Phase == PreparationPhase.InGame;
            // Disable raycasters with the Canvas so invisible menu buttons cannot
            // intercept combat/upgrade input through the Gameplay EventSystem.
            canvas.gameObject.SetActive(!playing);
            menuCamera.enabled = !manager.IsGameplayLoaded;
            listener.enabled = !manager.IsGameplayLoaded;
            events.enabled = !playing;
            foreach (var system in FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (system != events && system.gameObject.scene.name == "Gameplay") system.enabled = playing;
            if (selectingFriends && (playing || manager.RoomSnapshot.Phase != PreparationPhase.Preparing ||
                manager.IsLeavingRoom || steam == null || !steam.CanBrowseInviteFriends ||
                steam.InviteFriendsSnapshot.LobbyId != friendsLobbyId)) CloseFriends();
            if (playing) return;
            if (Input.GetKeyDown(KeyCode.Escape) && GameOptionsPanel.LastClosedFrame != Time.frameCount)
            {
                if (endPageActive) { if (confirmLeaveEnded) { confirmLeaveEnded = false; Invalidate(); } }
                else if (GameOptionsPanel.IsOpen) { GameOptionsPanel.HandleBack(); return; }
                else if (manager.IsLocalRoomConnecting) manager.CancelLocalRoomConnection();
                else if (selectingFriends) CloseFriends();
                else if (selectingWeapon) { selectingWeapon = false; Invalidate(); }
                else if (manager.RoomSnapshot.Phase != PreparationPhase.None) manager.LeavePreparationRoom();
            }
            if (GameOptionsPanel.IsOpen) return;
            UpdateLocalHome();
            // Keep the search field alive while typing; only the cached friend rows refresh.
            if (selectingFriends) { UpdateFriends(); return; }
            string key = $"{manager.RoomSnapshot.RunId}:{manager.RoomSnapshot.Revision}:{manager.MenuNotice}:{manager.IsLeavingRoom}:{manager.IsGameplayTransitioning}:{NetworkClient.active}:{steam?.State}:{steam?.LastError}:{selectingWeapon}";
            if (renderKey != key) { Render(); renderKey = key; }
        }
        private void Render()
        {
            if (content == null) return;
            if (localHomePanel != null) localHomePanel.gameObject.SetActive(manager.RoomSnapshot.Phase == PreparationPhase.None && BootGameplayNetworkManager.LocalPreparationAvailable);
            int selectedIndex = navigation.FindIndex(b => b != null && events != null && b.gameObject == events.currentSelectedGameObject);
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            navigation.Clear();
            Label(content, "MONSTER / SUPERGROUP", new Rect(48, 30, 600, 32), 22, Accent);
            Label(content, "集结 · 准备 · 出发", new Rect(950, 34, 280, 26), 15, Muted, TextAnchor.MiddleRight);
            Box(content, "Header line", new Rect(48, 80, 1184, 1), new Color(1, 1, 1, 0.13f));
            var snapshot = manager.RoomSnapshot;
            if (manager.IsRunEndScreen) DrawRunEnd();
            else if (snapshot.Phase == PreparationPhase.None) DrawHome();
            else if (selectingFriends && snapshot.Phase == PreparationPhase.Preparing) DrawFriends();
            else if (selectingWeapon && snapshot.Phase == PreparationPhase.Preparing) DrawWeapons();
            else DrawRoom(snapshot);
            string message = manager.MenuNotice;
            if (string.IsNullOrEmpty(message) && steam != null && steam.IsSteamBackendSelected && steam.State == SteamLobbyState.Error && steam.IsSteamInitialized)
                message = "Steam 连接失败，请返回后重试。";
            notice = Label(content, message, new Rect(48, 640, 1184, 42), 16, Accent);
            if (selectingFriends) WireFriendNavigation();
            else WireNavigation(manager.IsRunEndScreen ? -1 : selectedIndex);
            if (localHomePanel != null && localHomePanel.gameObject.activeSelf) WireLocalHomeNavigation();
        }
        private void DrawHome()
        {
            Label(content, "MONSTER\nSUPERGROUP", new Rect(70, 128, 630, 136), 58, Cream);
            Label(content, "召集同伴，迎接下一场战斗。", new Rect(74, 282, 570, 36), 22, Muted);
            bool busy = manager.IsLocalRoomConnecting || NetworkClient.active || NetworkServer.active || manager.IsLeavingRoom || manager.IsGameplayTransitioning ||
                (steam != null && (steam.State == SteamLobbyState.Creating || steam.State == SteamLobbyState.Joining || steam.State == SteamLobbyState.Leaving));
            Button(content, busy ? "连接中…" : "游戏", new Rect(74, 352, 340, 58), manager.CreatePreparationRoom, !busy, true);
            Button(content, "反馈意见", new Rect(74, 424, 340, 52), () => { });
            Button(content, "选项", new Rect(74, 490, 340, 52), OpenOptions);
            Button(content, "退出", new Rect(74, 556, 340, 52), manager.QuitFromMenu);
            if (BootGameplayNetworkManager.LocalPreparationAvailable)
            {
                Portrait(content, new Rect(493, 365, 176, 208));
                Label(content, () => catalog.CharacterName, new Rect(477, 578, 208, 34), 24, Cream, TextAnchor.MiddleCenter);
                DrawLocalHome();
            }
            else
            {
            Box(content, "Portrait backdrop", new Rect(748, 156, 390, 440), Panel);
            Box(content, "Portrait accent", new Rect(748, 156, 4, 440), Accent);
            Portrait(content, new Rect(804, 188, 280, 312));
            Label(content, () => catalog.CharacterName.ToUpperInvariant(), new Rect(772, 516, 342, 40), 30, Cream, TextAnchor.MiddleCenter);
            Label(content, "同一场战斗 · 最多四位同伴", new Rect(772, 561, 342, 25), 16, Muted, TextAnchor.MiddleCenter);
            }
            Label(content, steam != null && steam.IsSteamInitialized ? "Steam 已连接 · 支持好友邀请" : "本地单人可用 · Steam 未连接",
                new Rect(74, 630, 620, 30), 16, Muted);
        }
        private void DrawRoom(PreparationRoomSnapshot snapshot)
        {
            bool loading = snapshot.Phase == PreparationPhase.Loading;
            var members = snapshot.Members ?? Array.Empty<PreparationMember>();
            var self = members.FirstOrDefault(m => m.ParticipantId == snapshot.SelfId);
            Label(content, loading ? "正在进入战场" : "准备房间", new Rect(48, 108, 620, 54), 36, Cream);
            Label(content, () => RoomConnectionLabel(snapshot, members.Length), new Rect(48, 166, 710, 28), 17, Muted);
            for (int i = 0; i < 4; i++)
            {
                int seat = i;
                var member = members.FirstOrDefault(m => m.Seat == seat);
                float x = 48 + i * 210;
                var card = Box(content, "Seat " + (i + 1), new Rect(x, 212, 194, 338), Panel);
                if (member.ParticipantId == 0)
                {
                    Label(card, "+", new Rect(0, 80, 194, 72), 58, Muted, TextAnchor.MiddleCenter);
                    Label(card, "等待同伴", new Rect(10, 150, 174, 64), 20, Cream, TextAnchor.MiddleCenter);
                    if (manager.IsKcpPreparationRoom)
                        Label(card, "在其他进程点击“加入本地主机”", new Rect(14, 246, 166, 72), 17, Muted, TextAnchor.MiddleCenter);
                    else Button(card, "邀请朋友", new Rect(18, 255, 158, 48), OpenFriends,
                            !loading && steam != null && steam.CanBrowseInviteFriends);
                    continue;
                }
                bool own = member.ParticipantId == snapshot.SelfId;
                Box(card, "Seat accent", new Rect(0, 0, 194, 3), own ? Accent : Muted);
                Label(card, () => $"P{member.ParticipantId}" + (member.IsHost ? Loc.Get(" · 房主") : "") + (own ? Loc.Get(" · 你") : ""),
                    new Rect(14, 15, 166, 26), 17, own ? Accent : Muted);
                Label(card, member.DisplayName, new Rect(14, 43, 166, 30), 19, Cream, localize: false);
                Portrait(card, new Rect(49, 83, 96, 118));
                Label(card, () => catalog.CharacterName, new Rect(12, 205, 170, 27), 20, Cream, TextAnchor.MiddleCenter);
                var weapon = catalog.FindWeapon(member.WeaponId);
                Func<string> name = () => weapon?.Name ?? ContentText.Name(LocalizedContentKind.Weapon, member.WeaponId);
                if (own && !loading)
                    Button(card, () => name() + "  ›", new Rect(10, 238, 174, 44), () => { selectingWeapon = true; Invalidate(); });
                else Label(card, name, new Rect(12, 238, 170, 42), 16, Cream, TextAnchor.MiddleCenter);
                Label(card, loading ? (member.GameplayReady ? "加载完成" : "正在加载…") : member.Ready ? "已准备" : "未准备",
                    new Rect(10, 293, 174, 28), 17, (loading ? member.GameplayReady : member.Ready) ? Green : Muted, TextAnchor.MiddleCenter);
            }
            var config = Box(content, "Run configuration", new Rect(906, 212, 326, 338), Panel);
            Label(config, "本次远征", new Rect(24, 19, 278, 32), 24, Cream);
            Label(config, "地图", new Rect(24, 68, 278, 25), 15, Muted);
            Label(config, () => catalog.MapName, new Rect(24, 100, 278, 36), 26, Cream);
            Label(config, () => catalog.MapDescription, new Rect(24, 146, 278, 75), 17, Muted);
            Box(config, "Divider", new Rect(24, 238, 278, 1), new Color(1, 1, 1, 0.12f));
            Label(config, "难度", new Rect(24, 263, 110, 30), 17, Muted);
            Label(config, "中等", new Rect(160, 259, 142, 38), 24, Accent, TextAnchor.MiddleRight);
            Label(content, loading ? "等待所有成员的角色和初始武器准备完成。" : "选择初始武器并准备。多人全部准备后，由房主开始。",
                new Rect(48, 565, 1150, 33), 18, Muted);
            Button(content, loading ? "取消开局" : "返回", new Rect(48, 614, 152, 48), manager.LeavePreparationRoom, !manager.IsLeavingRoom);
            if (!loading)
            {
                Button(content, self.Ready ? "取消准备" : "准备", new Rect(706, 614, 194, 48), () => manager.SetOwnReady(!self.Ready), members.Length > 1);
                if (self.IsHost)
                    Button(content, "开始游戏", new Rect(924, 614, 308, 48), manager.StartPreparedGame,
                        members.Length == 1 || members.All(m => m.Ready), true);
                else Label(content, "等待房主开始", new Rect(924, 614, 308, 48), 22, Muted, TextAnchor.MiddleCenter);
            }
        }
        private void DrawWeapons()
        {
            Label(content, "选择初始武器", new Rect(48, 108, 900, 52), 36, Cream);
            Label(content, () => Loc.Get("{0} · 当前唯一角色    /    修改选择会取消自己的准备", catalog.CharacterName), new Rect(48, 167, 1160, 30), 18, Muted);
            uint selected = manager.RoomSnapshot.Members.First(m => m.ParticipantId == manager.RoomSnapshot.SelfId).WeaponId;
            for (int i = 0; i < catalog.Weapons.Length; i++)
            {
                var weapon = catalog.Weapons[i];
                float x = 48 + (i % 3) * 400, y = 220 + (i / 3) * 170;
                var card = Box(content, "Weapon " + weapon.Id, new Rect(x, y, 384, 152), Panel);
                Box(card, "Accent", new Rect(0, 0, 3, 152), weapon.Id == selected ? Accent : Muted);
                if (weapon.Icon != null) Sprite(card, weapon.Icon, new Rect(18, 18, 56, 56));
                else
                {
                    var mark = Box(card, "Weapon emblem", new Rect(18, 18, 56, 56), new Color32(54, 66, 66, 255));
                    Label(mark, weapon.IconMark, new Rect(0, 0, 56, 56), 30, Accent, TextAnchor.MiddleCenter);
                }
                Label(card, () => weapon.Name, new Rect(88, 15, 278, 44), 21, Cream);
                Label(card, () => weapon.Description, new Rect(88, 60, 278, 42), 15, Muted);
                Button(card, weapon.Id == selected ? "已选择" : "选择", new Rect(240, 109, 126, 32),
                    () => { manager.SetOwnLoadout(weapon.Id); selectingWeapon = false; Invalidate(); }, true, weapon.Id == selected);
            }
            Button(content, "返回房间", new Rect(48, 614, 194, 48), () => { selectingWeapon = false; Invalidate(); });
        }
        private RectTransform Box(Transform parent, string name, Rect rect, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>(); Position(rt, rect); var image = go.GetComponent<Image>();
            image.color = color; image.raycastTarget = false; return rt;
        }
        private Text Label(Transform parent, string text, Rect rect, int size, Color color, TextAnchor align = TextAnchor.UpperLeft, bool localize = true)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            Position(go.GetComponent<RectTransform>(), rect); var label = go.GetComponent<Text>();
            label.font = GameLocalization.UIFont != null ? GameLocalization.UIFont : font; label.text = text; label.fontSize = size; label.color = color; label.alignment = align;
            label.supportRichText = false; label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            if (localize) Loc.Bind(label, text);
            return label;
        }
        private Text Label(Transform parent, Func<string> value, Rect rect, int size, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var label = Label(parent, value(), rect, size, color, align, false);
            label.gameObject.AddComponent<LocalizedMenuText>().SetResolver(value);
            return label;
        }
        private Button Button(Transform parent, Func<string> value, Rect rect, Action action)
        {
            var button = Button(parent, value(), rect, action);
            button.GetComponentInChildren<LocalizedMenuText>().SetResolver(() => { button.name = value(); return button.name; });
            return button;
        }
        private Button Button(Transform parent, string title, Rect rect, Action action, bool enabled = true, bool primary = false)
        {
            var rt = Box(parent, title, rect, primary ? Accent : new Color32(44, 59, 63, 255));
            rt.GetComponent<Image>().raycastTarget = true;
            var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = rt.GetComponent<Image>(); button.interactable = enabled;
            var colors = button.colors; colors.highlightedColor = new Color(1.16f, 1.16f, 1.16f, 1); colors.selectedColor = new Color(1.25f, 1.25f, 1.12f, 1);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f); button.colors = colors;
            var text = Label(rt, title, new Rect(8, 0, rect.width - 16, rect.height), rect.height < 46 ? 16 : 21, primary ? Ink : Cream, TextAnchor.MiddleCenter);
            if (!enabled) text.color = Muted;
            button.onClick.AddListener(() => action()); navigation.Add(button); return button;
        }
        private void Portrait(Transform parent, Rect rect)
        {
            if (!catalog.UseCharacterMonogram) { Sprite(parent, catalog.CharacterPortrait, rect); return; }
            float size = Mathf.Min(rect.width, rect.height) * .76f;
            var bounds = new Rect(rect.x + (rect.width - size) / 2, rect.y + (rect.height - size) / 2, size, size);
            var rt = Box(parent, "Character emblem", bounds, Accent);
            var emblem = rt.GetComponent<Image>(); emblem.sprite = catalog.CharacterPortrait; emblem.preserveAspect = true;
            Label(rt, () => catalog.CharacterName.Substring(0, 1).ToUpperInvariant(), new Rect(0, 0, size, size),
                Mathf.RoundToInt(size * .52f), Ink, TextAnchor.MiddleCenter);
        }
        private void Sprite(Transform parent, Sprite sprite, Rect rect)
        {
            if (sprite == null) return;
            var rt = Box(parent, "Illustration", rect, Color.white);
            var image = rt.GetComponent<Image>(); image.sprite = sprite; image.preserveAspect = true;
        }
        private static void Position(RectTransform rt, Rect rect)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(rect.x, -rect.y); rt.sizeDelta = rect.size;
        }
        private void WireNavigation(int selectedIndex)
        {
            if (GameOptionsPanel.IsOpen) return;
            var usable = navigation.Where(b => b.interactable).ToArray();
            for (int i = 0; i < usable.Length; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = usable[(i + usable.Length - 1) % usable.Length], selectOnDown = usable[(i + 1) % usable.Length],
                    selectOnLeft = usable[(i + usable.Length - 1) % usable.Length], selectOnRight = usable[(i + 1) % usable.Length] };
                usable[i].navigation = nav;
            }
            if (events != null && usable.Length > 0 && !HasLocalHomeFocus)
            {
                var previous = selectedIndex >= 0 && selectedIndex < navigation.Count ? navigation[selectedIndex] : null;
                events.SetSelectedGameObject(previous != null && previous.interactable ? previous.gameObject : usable[0].gameObject);
            }
        }
        private void OnDestroy()
        {
            destroying = true;
            Loc.Changed -= OnLanguageChanged;
            if (canvas != null) GameOptionsPanel.CloseFor(canvas.transform);
            if (manager != null) manager.PreparationChanged -= Invalidate;
            if (steam != null) steam.Changed -= Invalidate;
            if (font != null) Destroy(font);
        }
    }
}
