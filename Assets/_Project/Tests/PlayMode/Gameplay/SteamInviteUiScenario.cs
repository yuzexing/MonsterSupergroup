#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    internal static class SteamInviteUiScenario
    {
        internal static IEnumerator Run(BootGameplayNetworkManager manager, Func<string, IEnumerator> screenshot = null)
        {
            Assert.That(manager.TryStartOfflineRoom(out string error), Is.True, error);
            yield return Until(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing);
            var steam = manager.GetComponent<SteamLobbyService>();
            var fake = new FakeFriends(); ulong lobbyId = 42; bool room = true;
            // No Steam initialization or overlay is needed. This adapter cannot contact real friends.
            steam.ConfigureInvitationsForTests(fake, () => new SteamInviteContext(lobbyId, true,
                room && manager.RoomSnapshot.Phase == PreparationPhase.Preparing), () => Time.realtimeSinceStartupAsDouble);
            manager.ShowMenuNotice("验收模拟好友 · 不会发送真实 Steam 邀请");
            yield return null;
            var view = UnityEngine.Object.FindFirstObjectByType<PreparationMenuView>();
            Assert.That(view, Is.Not.Null);
            Invoke(view, "OpenFriends"); yield return null;
            var search = view.GetComponentInChildren<InputField>();
            var scroll = view.GetComponentInChildren<ScrollRect>();
            Assert.That(search, Is.Not.Null);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.verticalNormalizedPosition, Is.GreaterThan(.99f), "First open must start at the online friends at the top.");
            Assert.That(scroll.verticalScrollbar.handleRect.rect.height, Is.LessThan(scroll.viewport.rect.height));
            Assert.That(FriendButton(view, 2).interactable, Is.False, "Already in lobby.");
            Assert.That(FriendButton(view, 3).interactable, Is.True, "Offline must still be invitable.");
            if (screenshot != null) yield return screenshot("friends-list");

            // Search stays alive across polling, including focus, search text and cursor.
            search.text = "测试好友 03"; yield return null;
            Assert.That(VisibleFriends(view).Length, Is.EqualTo(1));
            EventSystem.current.SetSelectedGameObject(search.gameObject);
            search.caretPosition = 3;
            var options = GameOptionsService.EnsureInitialized();
            bool hadPreference = PlayerPrefs.HasKey(GameOptionsService.PreferenceKey);
            string preference = PlayerPrefs.GetString(GameOptionsService.PreferenceKey), language = options.Current.Language;
            try
            {
                options.SetLanguage("en"); yield return null;
                Assert.That(view.GetComponentInChildren<InputField>(), Is.SameAs(search), "Locale changes must preserve the active friend search.");
                Assert.That(search.caretPosition, Is.EqualTo(3));
                Assert.That(FriendButton(view, 3).transform.parent.GetComponentsInChildren<Text>().Any(t => t.text == "测试好友 03 · Friend"), Is.True,
                    "Player nicknames must not be translated.");
                options.SetLanguage("zh-CN"); yield return null;
            }
            finally
            {
                options.SetLanguage(language);
                if (hadPreference) PlayerPrefs.SetString(GameOptionsService.PreferenceKey, preference);
                else PlayerPrefs.DeleteKey(GameOptionsService.PreferenceKey);
                PlayerPrefs.Save();
            }
            int reads = fake.ReadCount;
            for (int i = 0; i < 5; i++)
            { yield return new WaitForSecondsRealtime(.45f); Invoke(view, "RefreshFriendRows", false); }
            Assert.That(fake.ReadCount, Is.GreaterThan(reads), "Visible picker must refresh automatically.");
            Assert.That(view.GetComponentInChildren<InputField>(), Is.SameAs(search));
            Assert.That(search.text, Is.EqualTo("测试好友 03"));
            Assert.That(search.caretPosition, Is.EqualTo(3));
            if (screenshot != null) yield return screenshot("friends-search");

            search.text = ""; yield return null;
            var focus = FriendButton(view, 30);
            EventSystem.current.SetSelectedGameObject(focus.gameObject); yield return null;
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0), "Keyboard focus should reveal a distant row.");
            float position = scroll.verticalNormalizedPosition;
            fake.Reverse = true;
            Invoke(view, "RefreshFriendRows", true); yield return null;
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(FriendButton(view, 30).gameObject));
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(position).Within(.001f));
            var beforeMove = EventSystem.current.currentSelectedGameObject;
            ExecuteEvents.Execute(beforeMove, new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down }, ExecuteEvents.moveHandler);
            yield return null;
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.EqualTo(beforeMove));
            if (screenshot != null) yield return screenshot("friends-scroll-keyboard");

            search.text = "测试好友 03"; yield return null;
            var invite = FriendButton(view, 3);
            EventSystem.current.SetSelectedGameObject(invite.gameObject);
            ExecuteEvents.Execute(invite.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            yield return null;
            Assert.That(fake.Sends, Is.EqualTo(new[] { (42ul, 3ul) }));
            Assert.That(FriendButton(view, 3).interactable, Is.False);
            Assert.That(manager.MenuNotice, Is.EqualTo("邀请已提交，等待好友接受"));
            Assert.That(manager.RoomSnapshot.Members.Length, Is.EqualTo(1), "Sending never adds a Mirror member.");
            if (screenshot != null) yield return screenshot("friends-submitted");
            yield return new WaitForSecondsRealtime(5.1f);
            Assert.That(FriendButton(view, 3).interactable, Is.True);
            fake.Success = false;
            FriendButton(view, 3).onClick.Invoke(); yield return null;
            Assert.That(FriendButton(view, 3).interactable, Is.True, "API failure permits retry.");
            Assert.That(manager.RoomSnapshot.Phase, Is.EqualTo(PreparationPhase.Preparing));
            Assert.That(NetworkServer.active, Is.True, "Invitation failure preserves the room.");

            // An event retained from a previous page must carry the original lobby ID.
            var oldClick = FriendButton(view, 3).onClick;
            lobbyId = 43; yield return null; yield return null;
            Assert.That(view.GetComponentInChildren<InputField>(), Is.Null);
            int sent = fake.Sends.Count;
            oldClick.Invoke();
            Assert.That(fake.Sends.Count, Is.EqualTo(sent));
            Assert.That(manager.MenuNotice, Does.Contain("房间已变化"));
            Invoke(view, "OpenFriends"); yield return null;
            room = false; yield return null; yield return null;
            Assert.That(view.GetComponentInChildren<InputField>(), Is.Null, "Loading/closed context closes the picker.");
            Assert.That(steam.InviteFriendsSnapshot.Friends, Is.Empty);
            room = true; Invoke(view, "OpenFriends"); yield return null;
            manager.LeavePreparationRoom();
            yield return Until(() => !NetworkClient.active && !NetworkServer.active && !manager.IsLeavingRoom);
            yield return null;
            Assert.That(view.GetComponentsInChildren<InputField>().Any(field => field == search), Is.False,
                "The friend search must close; the development home may have its own port editor.");
            Assert.That(steam.InviteFriendsSnapshot.Friends, Is.Empty);
            Debug.Log("[SteamInviteUI] result=PASS fake_api=true overlay_dependency=false search_scroll_focus_send_lifecycle=true");
        }
        private static Button[] VisibleFriends(PreparationMenuView view) => view.GetComponentsInChildren<Button>()
            .Where(b => b.name.StartsWith("Invite friend ", StringComparison.Ordinal)).ToArray();
        private static Button FriendButton(PreparationMenuView view, ulong id) => VisibleFriends(view).Single(b => b.name == "Invite friend " + id);
        private static void Invoke(object instance, string method, params object[] args) => instance.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args);
        private static IEnumerator Until(Func<bool> condition)
        {
            float until = Time.realtimeSinceStartup + 30;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(condition(), Is.True, "Invite UI lifecycle timed out.");
        }
        private sealed class FakeFriends : ISteamInviteApi
        {
            internal int ReadCount;
            internal bool Reverse, Success = true;
            internal readonly List<(ulong, ulong)> Sends = new List<(ulong, ulong)>();
            public bool LoggedOn => true;
            public ulong LocalUser => 1;
            public bool IsFriend(ulong user) => user >= 2 && user <= 61;
            public SteamInviteLobbyInfo ReadLobby(ulong lobby) => new SteamInviteLobbyInfo {
                Members = new ulong[] { 1, 2 }, Limit = 4, Preparing = true, ValidHost = true };
            public IEnumerable<SteamInviteFriendInfo> ReadFriends()
            {
                ReadCount++;
                IEnumerable<int> ids = Enumerable.Range(2, 60);
                if (Reverse) ids = ids.Reverse();
                return ids.Select(id => new SteamInviteFriendInfo((ulong)id, $"测试好友 {id:00} · Friend",
                    id % 3 == 0 ? SteamFriendPresence.Offline : SteamFriendPresence.Online));
            }
            public bool Send(ulong lobby, ulong friend) { Sends.Add((lobby, friend)); return Success; }
        }
    }
}
#endif
