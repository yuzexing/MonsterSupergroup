#if UNITY_EDITOR || MONSTER_MENU_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class PreparationMenuProcessProbe : MonoBehaviour
    {
        private string role, directory, profile;
        private BootGameplayNetworkManager manager;
        private bool finished;
        private float timeout;
        private NetworkIdentity Owner => NetworkClient.localPlayer;
        private uint Weapon => role == "host" ? 3u : role == "a" ? 402u : role == "b" ? 6u : 1u;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string role = Arg("--menu-role=") ?? Arg("--local-menu-role=");
            if (role == null) return;
            var probe = new GameObject("Preparation validation").AddComponent<PreparationMenuProcessProbe>();
            probe.role = role; probe.directory = Arg("--menu-artifacts="); probe.profile = Arg("--menu-profile=") ?? "party";
            DontDestroyOnLoad(probe.gameObject);
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            timeout = Time.realtimeSinceStartup + 220;
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    var routine = stack.Peek();
                    if (!routine.MoveNext()) { stack.Pop(); continue; }
                    next = routine.Current;
                    if (next is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
                yield return next;
            }
            Finish(true);
        }
        private void Update()
        {
            if (!finished && timeout > 0 && Time.realtimeSinceStartup > timeout)
            { Debug.LogError("Preparation probe deadline exceeded: " + role); Finish(false); }
        }
        private IEnumerator Run()
        {
            yield return Wait(() => NetworkManager.singleton != null, "Boot");
            manager = (BootGameplayNetworkManager)NetworkManager.singleton;
            if (profile.StartsWith("local-"))
                Require(manager.UsePreparationRoom && manager.GetComponent<NetworkBackendBootstrap>().Selection.Purpose == NetworkRuntimePurpose.Interactive,
                    "Local UI validation must use the normal interactive Boot path");
            else manager.ConfigurePreparationFlow(true);
            yield return manager.EnsureMainMenu();
            if (Arg("--menu-locale=") != null) yield return ConfigureLocaleProbe();
            yield return Wait(() => FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b => b.name == "游戏" && b.isActiveAndEnabled && b.interactable), "interactive home ready");
            Require(!NetworkServer.active && !NetworkClient.active && !manager.IsGameplayLoaded, "Startup bypassed home");
            Stage("home");
            if (role == "host") { yield return Shot("home"); yield return CheckHomeButtons(); }
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            if (profile.StartsWith("local-"))
            {
                yield return LocalRoomScenario(backend); yield break;
            }
            if (profile == "run-end" || profile == "run-end-solo")
            {
                yield return RunEndScenario(backend); yield break;
            }
            if (profile == "combat-menu" || profile == "combat-menu-solo")
            {
                yield return CombatMenuRun(backend); yield break;
            }
            if (profile == "invites")
            {
                yield return SteamInviteUiScenario.Run(manager, Shot);
                Stage("invite-ui-complete"); yield break;
            }
            if (profile == "offline")
            {
                Require(manager.TryStartOfflineRoom(out string error), error);
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "offline preparation");
                Require(!NetworkServer.listen && !manager.transport.ServerActive(), "Offline host opened an external listener");
                yield return CheckPreparation();
                yield return Shot("solo-room");
                yield return SelectAndLaunchSolo();
                manager.LeavePreparationRoom(); yield return WaitClean();
                Stage("offline-complete"); yield break;
            }
            Require(backend.TryPrepareKcp("127.0.0.1", ushort.Parse(Arg("--menu-port=") ?? "7998"), false, out string prepareError), prepareError);
            if (role == "host") { NetworkServer.listen = true; manager.StartHost(); }
            else manager.StartClient();
            if (role == "fifth")
            {
                yield return Wait(() => !NetworkClient.active, "fifth player rejection");
                Require(Owner == null && manager.RoomSnapshot.Phase == PreparationPhase.None, "Fifth player was admitted");
                Require(manager.MenuNotice.Contains("房间已满"), "Full room reason missing");
                Mark("fifth-rejected"); Stage("full-room-rejected"); yield break;
            }
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing && manager.RoomSnapshot.SelfId != 0, "room snapshot");
            yield return CheckPreparation();
            manager.SetOwnLoadout(Weapon);
            yield return Wait(() => Self().WeaponId == Weapon, "own weapon choice");
            Mark("participant-" + role, manager.RoomSnapshot.SelfId.ToString());
            if (role == "host")
            {
                Mark("host-open");
                yield return Wait(() => manager.RoomSnapshot.Members.Length == 4 && Seen("participant-a") && Seen("participant-b") && Seen("participant-c"), "four seats");
                Stage("four-seats"); Mark("four-seats");
                yield return Shot("party-room");
                yield return OpenWeaponSelection();
                yield return Shot("weapon-selection");
                Click("返回房间");
                yield return Wait(() => Seen("fifth-rejected"), "capacity check");
                Require(manager.Session.Participants.Count == 4, "Rejected fifth player left a ghost participant");
            }
            else
            {
                // Clients have no start permission, even when sending the same public message.
                manager.StartPreparedGame();
                yield return new WaitForSecondsRealtime(.2f);
                Require(manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "Client started the run");
            }
            manager.SetOwnReady(true);
            yield return Wait(() => Self().Ready, "ready sync");
            Mark("ready-" + role);
            if (role == "host")
            {
                yield return Wait(() => manager.RoomSnapshot.Members.All(m => m.Ready), "all ready");
                manager.StartPreparedGame(); manager.StartPreparedGame();
                yield return CaptureLoadingPage();
            }
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame && Owner != null, "combat barrier");
            yield return CheckGameplay();
            Mark("playing-" + role);
            if (role == "host")
            {
                yield return Wait(() => Seen("playing-a") && Seen("playing-b") && Seen("playing-c"), "all owner baselines");
                foreach (var participant in manager.Session.Participants)
                {
                    Require(participant.AvatarId != 0, "Missing server Avatar");
                    var avatar = NetworkServer.spawned[participant.AvatarId];
                    Require(avatar.GetComponent<PlayerBuildRuntime>().InitialWeaponId == manager.ServerRoom.Launch.WeaponFor(participant.Id), "Server loadout differs from launch");
                }
                Stage("all-server-builds"); Mark("down-a");
                ulong a = ulong.Parse(Read("participant-a"));
                yield return Wait(() => manager.Session.Participants.Any(p => p.Id == a && p.AvatarId != 0 &&
                    NetworkCombatWorld.Instance.Gateway.Ledger.TryGetState(p.AvatarId, out var state) && !state.Alive), "server confirmed Downed");
                Mark("down-confirmed");
                yield return Wait(() => Seen("a-offline"), "A disconnect");
                Require(manager.Session.Participants.Single(p => p.Id == a).Checkpoint != null, "Missing checkpoint");
                Mark("checkpoint-seen");
                yield return Wait(() => Seen("a-rejoined"), "A Downed reconnect");
                Stage("stable-participant-downed-reconnect");
                Mark("leave-first-room"); manager.LeavePreparationRoom();
                yield return WaitClean();
                yield return Wait(() => Seen("home-a") && Seen("home-b") && Seen("home-c"), "all returned home");
                NetworkServer.listen = true;
                Require(backend.TryPrepareKcp("127.0.0.1", ushort.Parse(Arg("--menu-port=") ?? "7998"), false, out _), "Reopen transport");
                manager.StartHost();
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "second room");
                Mark("second-room");
                yield return Wait(() => manager.RoomSnapshot.Members.Length == 2 && manager.RoomSnapshot.Members.Where(m => !m.IsHost).All(m => m.Ready), "A second ready");
                manager.SetOwnReady(true);
                yield return Wait(() => manager.RoomSnapshot.Members.All(m => m.Ready), "second all ready");
                manager.StartPreparedGame();
                yield return Wait(() => !NetworkServer.active, "loading disconnect cancels host");
                yield return WaitClean();
                Require(manager.MenuNotice.Contains("加载期间"), "Wrong loading failure reason");
                Stage("loading-disconnect-cleanup");
                Require(manager.TryStartOfflineRoom(out string error), error);
                yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "restart solo");
                yield return SelectAndLaunchSolo();
                manager.LeavePreparationRoom(); yield return WaitClean();
                Mark("complete");
            }
            else
            {
                if (role == "a") yield return DownAndReconnect();
                yield return Wait(() => Seen("leave-first-room"), "host leaving");
                yield return WaitClean();
                Require(manager.RoomSnapshot.Phase == PreparationPhase.None, "Client retained stale room");
                Mark("home-" + role);
                if (role == "a")
                {
                    yield return Wait(() => Seen("second-room"), "second host");
                    manager.StartClient();
                    yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Preparing, "second admission");
                    manager.SetOwnReady(true);
                    yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.Loading, "second loading");
                    manager.StopClient(); yield return WaitClean();
                }
                yield return Wait(() => Seen("complete"), "host cleanup and solo restart");
            }
        }
        private IEnumerator CheckPreparation()
        {
            yield return null;
            Require(!manager.IsGameplayLoaded && Owner == null, "Preparation created Gameplay/Avatar");
            Require(FindFirstObjectByType<PreparationMenuView>() != null, "Menu view missing");
            Require(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Count(s => s.enabled) == 1, "Multiple menu input systems");
        }
        private IEnumerator CaptureLoadingPage()
        {
            if (Application.isBatchMode) yield break;
            yield return Wait(() => manager.ServerRoom.Phase == PreparationPhase.Loading, "loading page");
            var pending = new List<(NetworkConnectionToClient connection, GameplayReady message)>();
            var receive = typeof(BootGameplayNetworkManager).GetMethod("ReceiveGameplayReady", BindingFlags.Instance | BindingFlags.NonPublic);
            NetworkServer.RegisterHandler<GameplayReady>((connection, message) => pending.Add((connection, message)));
            yield return Shot("loading");
            Require(!manager.Session.IsRunStarted, "Combat started before the loading barrier");
            NetworkServer.RegisterHandler<GameplayReady>((connection, message) => receive.Invoke(manager, new object[] { connection, message }));
            foreach (var item in pending) receive.Invoke(manager, new object[] { item.connection, item.message });
        }
        private IEnumerator SelectAndLaunchSolo()
        {
            manager.SetOwnLoadout(8);
            yield return Wait(() => Self().WeaponId == 8, "solo weapon");
            ulong id = manager.RoomSnapshot.SelfId;
            manager.StartPreparedGame();
            yield return Wait(() => manager.RoomSnapshot.Phase == PreparationPhase.InGame, "solo start");
            Require(Owner.GetComponent<PlayerBuildRuntime>().InitialWeaponId == 8, "Wrong solo weapon");
            Require(Owner.GetComponent<NetworkRunParticipant>().ParticipantId == id, "Solo ParticipantId changed");
            Stage("solo-playing");
        }
        private IEnumerator CheckGameplay()
        {
            yield return null;
            Require(Owner.GetComponent<NetworkRunParticipant>().ParticipantId == ulong.Parse(Read("participant-" + role)), "ParticipantId changed during launch");
            var build = Owner.GetComponent<PlayerBuildRuntime>();
            Require(build.IsBuildActive && build.InitialWeaponId == Weapon && build.CaptureState().Weapons.Length == 1, "Initial Build differs or contains leftovers");
            Require(Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline, "Missing Build baseline");
            Require(!Owner.GetComponent<PlayerMovement>().IsRunLoadingLocked, "Owner remains loading locked");
            var menu = FindFirstObjectByType<PreparationMenuView>();
            var hits = new List<RaycastResult>();
            Require(EventSystem.current != null, "Gameplay input is missing");
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) {
                position = new Vector2(Screen.width * .1f, Screen.height * .115f) }, hits);
            Require(hits.All(hit => !hit.gameObject.transform.IsChildOf(menu.transform)), "Hidden menu intercepted gameplay input");
            Stage("owner-build-confirmed", "participant=" + manager.RoomSnapshot.SelfId + " avatar=" + Owner.netId + " weapon=" + Weapon);
        }
        private IEnumerator DownAndReconnect()
        {
            yield return Wait(() => Seen("down-a"), "Downed command");
            ulong id = Owner.GetComponent<NetworkRunParticipant>().ParticipantId;
            uint oldAvatar = Owner.netId;
            Owner.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, int.MaxValue, false));
            yield return Wait(() => Seen("down-confirmed"), "confirmed death");
            manager.StopClient(); yield return WaitClean(); Mark("a-offline");
            yield return Wait(() => Seen("checkpoint-seen"), "checkpoint");
            manager.StartClient();
            yield return Wait(() => Owner != null && Owner.GetComponent<NetworkModifierSelection>().HasOwnerBaseline &&
                !Owner.GetComponent<CombatantBehaviour>().IsAlive, "restored Downed baseline");
            Require(Owner.GetComponent<NetworkRunParticipant>().ParticipantId == id && Owner.netId != oldAvatar, "Reconnect identity/avatar incorrect");
            Require(Owner.GetComponent<PlayerBuildRuntime>().InitialWeaponId == Weapon, "Reconnect lost weapon choice");
            var movement = Owner.GetComponent<PlayerMovement>(); var body = Owner.GetComponent<Rigidbody2D>();
            Vector2 position = body.position;
            for (int i = 0; i < 12; i++) { movement.SetDirection(Vector2.right); movement.Dash(); yield return new WaitForFixedUpdate(); }
            Require(Vector2.Distance(position, body.position) < .01f && body.linearVelocity.sqrMagnitude < .0001f, "Downed player moved");
            Mark("a-rejoined");
        }
        private IEnumerator CheckHomeButtons()
        {
            Click("反馈意见"); Click("选项"); yield return null;
            Require(GameOptionsPanel.IsOpen && !NetworkClient.active && !manager.IsGameplayLoaded, "Home options must open without starting a game");
            Click("Options.Back"); yield return null;
            Require(!GameOptionsPanel.IsOpen, "Home options did not close");
            FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.name == "游戏" && b.isActiveAndEnabled).Select();
            Require(EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null, "Keyboard navigation has no selection");
            var events = EventSystem.current;
            var move = new AxisEventData(events) { moveDir = MoveDirection.Down, moveVector = Vector2.down };
            ExecuteEvents.Execute(events.currentSelectedGameObject, move, ExecuteEvents.moveHandler);
            Require(events.currentSelectedGameObject.name == "反馈意见", "Down navigation did not select feedback");
            ExecuteEvents.Execute(events.currentSelectedGameObject, new BaseEventData(events), ExecuteEvents.submitHandler);
            Require(!NetworkClient.active && !manager.IsGameplayLoaded, "Keyboard submission bypassed the home page");
        }
        private IEnumerator OpenWeaponSelection()
        {
            string name = PreparationMenuCatalog.Load().FindWeapon(Weapon).Name + "  ›";
            Click(name); yield return null; yield return null;
            Require(FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b => b.name == "返回房间"), "Weapon selection did not open");
        }
        private static void Click(string name)
        {
            var button = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == name && b.isActiveAndEnabled);
            Require(button != null && button.interactable, "Button unavailable: " + name); button.onClick.Invoke();
        }
        private IEnumerator Shot(string name)
        {
            if (Application.isBatchMode) yield break;
            // Saved game options can override Unity's startup flags. Set only this test window,
            // without changing the user's saved options, and verify the actual capture dimensions.
            var args = Environment.GetCommandLineArgs();
            int widthAt = Array.IndexOf(args, "-screen-width"), heightAt = Array.IndexOf(args, "-screen-height");
            if (widthAt >= 0 && heightAt >= 0 && widthAt + 1 < args.Length && heightAt + 1 < args.Length &&
                int.TryParse(args[widthAt + 1], out int width) && int.TryParse(args[heightAt + 1], out int height))
            {
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                yield return Wait(() => Screen.width == width && Screen.height == height, "requested capture size");
            }
            yield return new WaitForSecondsRealtime(.25f);
            yield return new WaitForEndOfFrame();
            foreach (var camera in Camera.allCameras) Debug.Log($"[MenuRender] camera={camera.name} enabled={camera.enabled} rect={camera.pixelRect} target={camera.targetDisplay}");
            foreach (var ui in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) Debug.Log($"[MenuRender] canvas={ui.name} enabled={ui.enabled} rect={ui.GetComponent<RectTransform>().rect} active={ui.gameObject.activeInHierarchy}");
            var captured = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), captured.EncodeToPNG());
            var colors = captured.GetPixels32();
            bool visible = colors.Any(pixel => pixel.r > 80 || pixel.g > 80 || pixel.b > 80);
            Destroy(captured);
            Require(visible, "Menu screenshot is blank: " + name);
            yield return null;
        }
        private PreparationMember Self() => manager.RoomSnapshot.Members.First(m => m.ParticipantId == manager.RoomSnapshot.SelfId);
        private IEnumerator WaitClean() => Wait(() => !NetworkClient.active && !NetworkServer.active && !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning && !manager.IsLeavingRoom, "session cleanup");
        private static IEnumerator Wait(Func<bool> condition, string stage)
        {
            float until = Time.realtimeSinceStartup + 60;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Require(condition(), "Timed out: " + stage);
        }
        private void Stage(string name, string detail = "") => Debug.Log($"[MenuProcess] stage={name} role={role} {detail}");
        private bool Seen(string name) => File.Exists(Path.Combine(directory, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(directory, name));
        private void Mark(string name, string value = "ok") => File.WriteAllText(Path.Combine(directory, name), value);
        private static void Require(bool pass, string error) { if (!pass) throw new InvalidOperationException(error); }
        private void Finish(bool success)
        {
            if (finished) return; finished = true;
            RestoreLocalePreference();
            Debug.Log($"[MenuProcess] result={(success ? "PASS" : "FAIL")} role={role}"); Application.Quit(success ? 0 : 1);
        }
        private static string Arg(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
    }
}
#endif
