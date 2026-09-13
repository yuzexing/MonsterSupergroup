using System;
using Loc = MonsterSupergroup.Gameplay.Options.MenuLocalization;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class PreparationMenuView
    {
        private bool selectingFriends;
        private ulong friendsLobbyId;
        private double nextFriendsRefresh;
        private string friendSearch = "";
        private InputField friendSearchField;
        private ScrollRect friendScroll;
        private RectTransform friendRows;
        private Text friendsStatus;
        private Button friendsRefresh, friendsBack;
        private GameObject lastFriendSelection;
        private bool friendRowsDirty;
        private bool friendRowsPopulated;
        private readonly Dictionary<ulong, Button> friendButtons = new Dictionary<ulong, Button>();
        private readonly Dictionary<ulong, double> friendRetryAt = new Dictionary<ulong, double>();

        private void OpenFriends()
        {
            if (steam == null || !steam.CanBrowseInviteFriends) return;
            var snapshot = steam.RefreshInviteFriends();
            friendsLobbyId = snapshot.LobbyId;
            friendSearch = "";
            selectingFriends = true; selectingWeapon = false;
            Render();
            events.SetSelectedGameObject(friendSearchField.gameObject);
        }
        private void CloseFriends()
        {
            selectingFriends = false; friendsLobbyId = 0; friendSearch = "";
            friendButtons.Clear(); friendRetryAt.Clear();
            friendSearchField = null; friendScroll = null; friendRows = null;
            friendsStatus = null; friendsRefresh = null; friendsBack = null; lastFriendSelection = null;
            friendRowsDirty = false; friendRowsPopulated = false;
            Invalidate();
        }
        private void DrawFriends()
        {
            friendRowsPopulated = false;
            Label(content, "邀请 Steam 好友", new Rect(48, 108, 900, 52), 36, Cream);
            var lobbyHint = Label(content, "", new Rect(48, 167, 1184, 30), 18, Muted);
            Loc.Bind(lobbyHint, "大厅 {0}    ·    选择一位好友发送邀请，接受后加入准备房间。", friendsLobbyId);
            var search = Box(content, "Friend search", new Rect(48, 215, 956, 46), Panel);
            search.GetComponent<Image>().raycastTarget = true;
            friendSearchField = search.gameObject.AddComponent<InputField>();
            friendSearchField.targetGraphic = search.GetComponent<Image>();
            friendSearchField.textComponent = Label(search, "", new Rect(16, 0, 924, 46), 20, Cream, TextAnchor.MiddleLeft);
            friendSearchField.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
            friendSearchField.placeholder = Label(search, "搜索好友名称…", new Rect(16, 0, 924, 46), 20, Muted, TextAnchor.MiddleLeft);
            friendSearchField.lineType = InputField.LineType.SingleLine;
            friendSearchField.SetTextWithoutNotify(friendSearch);
            friendSearchField.onValueChanged.AddListener(value => { friendSearch = value; RefreshFriendRows(false); });
            friendsRefresh = Button(content, "刷新", new Rect(1028, 215, 204, 46), () => RefreshFriendRows(true));
            var scrollRoot = Box(content, "Friend list", new Rect(48, 278, 1184, 300), Panel);
            scrollRoot.GetComponent<Image>().raycastTarget = true;
            friendScroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            friendScroll.horizontal = false; friendScroll.movementType = ScrollRect.MovementType.Clamped;
            friendScroll.scrollSensitivity = 32;
            var viewport = Box(scrollRoot, "Viewport", new Rect(0, 0, 1164, 300), Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            friendRows = Box(viewport, "Friend rows", new Rect(0, 0, 1164, 300), Color.clear);
            friendScroll.viewport = viewport; friendScroll.content = friendRows;
            var bar = Box(scrollRoot, "Scrollbar", new Rect(1168, 0, 16, 300), Ink);
            bar.GetComponent<Image>().raycastTarget = true;
            var handle = Box(bar, "Handle", new Rect(0, 0, 16, 300), Muted);
            handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one;
            handle.pivot = new Vector2(.5f, .5f); handle.anchoredPosition = Vector2.zero; handle.sizeDelta = Vector2.zero;
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            friendScroll.verticalScrollbar = scrollbar;
            friendsStatus = Label(content, "", new Rect(48, 585, 1184, 26), 16, Muted);
            friendsBack = Button(content, "返回房间", new Rect(48, 620, 194, 48), CloseFriends);
            Label(content, "Tab / ↑↓ 选择 · Enter 邀请 · Esc 返回", new Rect(560, 622, 672, 42), 17, Muted, TextAnchor.MiddleRight);
            RefreshFriendRows(false);
        }
        private void UpdateFriends()
        {
            if (friendRowsDirty) { friendRowsDirty = false; RefreshFriendRows(false); }
            else if (Time.realtimeSinceStartupAsDouble >= nextFriendsRefresh) RefreshFriendRows(true);
            if (notice != null) notice.text = Loc.Get(manager.MenuNotice);
            if (events.currentSelectedGameObject != lastFriendSelection)
            { RevealSelectedFriend(); lastFriendSelection = events.currentSelectedGameObject; }
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                var current = events.currentSelectedGameObject != null ? events.currentSelectedGameObject.GetComponent<Selectable>() : null;
                var next = current == null ? friendSearchField :
                    Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? current.navigation.selectOnUp : current.navigation.selectOnDown;
                if (next != null) events.SetSelectedGameObject(next.gameObject);
            }
            // Cooldowns expire without waiting for the next two-second friend query.
            foreach (var pair in friendRetryAt)
            {
                if (!friendButtons.TryGetValue(pair.Key, out var button) || button == null) continue;
                double remaining = pair.Value - Time.realtimeSinceStartupAsDouble;
                if (remaining > 0) button.GetComponentInChildren<Text>().text = Loc.Get("已提交 · {0} 秒", Math.Ceiling(remaining));
                else if (!button.interactable && steam.InviteFriendsSnapshot.CanSend)
                {
                    button.interactable = true; button.GetComponentInChildren<Text>().text = Loc.Get("再次邀请");
                    WireFriendNavigation();
                }
            }
        }
        private void RefreshFriendRows(bool querySteam)
        {
            if (friendRows == null || steam == null) return;
            var snapshot = querySteam ? steam.RefreshInviteFriends() : steam.InviteFriendsSnapshot;
            if (querySteam || !friendRowsPopulated) nextFriendsRefresh = Time.realtimeSinceStartupAsDouble + 2;
            if (snapshot.LobbyId != friendsLobbyId) { CloseFriends(); return; }
            var selected = events.currentSelectedGameObject;
            ulong selectedId = friendButtons.FirstOrDefault(pair => pair.Value != null && pair.Value.gameObject == selected).Key;
            float scroll = friendRowsPopulated ? friendScroll.verticalNormalizedPosition : 1;
            friendRowsPopulated = true;
            foreach (var button in friendButtons.Values) navigation.Remove(button);
            foreach (Transform child in friendRows) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            friendButtons.Clear(); friendRetryAt.Clear();
            var visible = snapshot.Friends.Where(f => string.IsNullOrWhiteSpace(friendSearch) ||
                f.Name.IndexOf(friendSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            friendRows.sizeDelta = new Vector2(1164, Mathf.Max(300, visible.Length * 72));
            for (int i = 0; i < visible.Length; i++)
            {
                var friend = visible[i];
                var row = Box(friendRows, "Friend " + friend.SteamId, new Rect(0, i * 72, 1164, 68), new Color32(33, 47, 53, 255));
                Label(row, friend.Name, new Rect(20, 7, 430, 30), 21, Cream, localize: false);
                Label(row, friend.PresenceLabel, new Rect(20, 38, 430, 24), 16,
                    friend.Presence == SteamFriendPresence.Offline ? Muted : Green);
                Label(row, friend.InLobby ? "已在大厅" : friend.LastResult.Message, new Rect(472, 10, 484, 50), 16,
                    friend.LastResult.Submitted ? Green : Muted, TextAnchor.MiddleLeft);
                bool cooling = friend.RetryAt > Time.realtimeSinceStartupAsDouble;
                ulong expectedLobby = friendsLobbyId;
                var button = Button(row, friend.InLobby ? "已在大厅" : cooling ? "已提交" : "邀请",
                    new Rect(980, 12, 164, 44), () => {
                        steam.SendLobbyInvite(expectedLobby, friend.SteamId);
                        // Rebuild after UGUI's submit callback has finished with this button.
                        friendRowsDirty = true;
                    }, snapshot.CanSend && !friend.InLobby && !cooling);
                button.name = "Invite friend " + friend.SteamId;
                friendButtons[friend.SteamId] = button;
                if (!friend.InLobby && cooling) friendRetryAt[friend.SteamId] = friend.RetryAt;
            }
            if (visible.Length == 0) Label(friendRows, snapshot.Friends.Count == 0 ? "暂无可显示的 Steam 好友，请刷新重试。" : "没有找到匹配的好友。",
                new Rect(24, 110, 1116, 52), 22, Muted, TextAnchor.MiddleCenter);
            friendsStatus.text = string.IsNullOrEmpty(snapshot.Error) ? Loc.Get("显示 {0} / {1} 位好友 · 每 2 秒自动刷新", visible.Length, snapshot.Friends.Count) : Loc.Get(snapshot.Error);
            Canvas.ForceUpdateCanvases();
            friendScroll.verticalNormalizedPosition = scroll;
            WireFriendNavigation();
            if (selectedId != 0)
                events.SetSelectedGameObject(friendButtons.TryGetValue(selectedId, out var next) ? next.gameObject : friendSearchField.gameObject);
            lastFriendSelection = events.currentSelectedGameObject;
        }
        private void WireFriendNavigation()
        {
            if (friendSearchField == null) return;
            var usable = new List<Selectable> { friendSearchField, friendsRefresh };
            usable.AddRange(friendButtons.Values.Where(b => b.interactable)); usable.Add(friendsBack);
            for (int i = 0; i < usable.Count; i++)
                usable[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = usable[(i + usable.Count - 1) % usable.Count], selectOnDown = usable[(i + 1) % usable.Count],
                    selectOnLeft = usable[(i + usable.Count - 1) % usable.Count], selectOnRight = usable[(i + 1) % usable.Count] };
            // Preserve focus on a submitted row while still allowing the user to leave it.
            foreach (var button in friendButtons.Values.Where(b => !b.interactable))
                button.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = friendsRefresh,
                    selectOnDown = friendsBack, selectOnLeft = friendsRefresh, selectOnRight = friendsBack };
        }
        private void RevealSelectedFriend()
        {
            var button = friendButtons.Values.FirstOrDefault(b => b.gameObject == events.currentSelectedGameObject);
            if (button == null || friendRows == null) return;
            var row = (RectTransform)button.transform.parent;
            float top = -row.anchoredPosition.y, offset = friendRows.anchoredPosition.y, height = friendScroll.viewport.rect.height;
            if (top < offset) friendRows.anchoredPosition = new Vector2(0, top);
            else if (top + row.rect.height > offset + height) friendRows.anchoredPosition = new Vector2(0, top + row.rect.height - height);
        }
    }
}
