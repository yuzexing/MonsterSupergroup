using System.Globalization;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using MonsterSupergroup.Gameplay.Options;
using Loc = MonsterSupergroup.Gameplay.Options.MenuLocalization;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class PreparationMenuView
    {
        private RectTransform localHomePanel;
        private InputField localPortInput;
        private Button localHostButton, localJoinButton, localCancelButton;
        private Text localProgress;
        private bool localWasConnecting;
        private int localTabFrame = -1;
        private readonly System.Collections.Generic.List<(Text text, string key)> localLabels = new();

        private void DrawLocalHome()
        {
            if (localHomePanel == null)
            {
                // Keep the input outside the rebuilt page so notices/Steam state cannot reset its caret.
                localHomePanel = Box(content.parent, "Local KCP", new Rect(748, 140, 484, 500), Panel);
                Box(localHomePanel, "Accent", new Rect(0, 0, 4, 500), Accent);
                LocalLabel("本地联机 · 开发", new Rect(24, 22, 436, 38), 27, Cream);
                LocalLabel("固定地址：127.0.0.1", new Rect(24, 78, 436, 30), 19, Muted);
                LocalLabel("端口", new Rect(24, 134, 82, 38), 20, Cream);
                var field = Box(localHomePanel, "Local KCP Port", new Rect(120, 126, 210, 48), Ink);
                field.GetComponent<Image>().raycastTarget = true;
                localPortInput = field.gameObject.AddComponent<InputField>();
                var text = Label(field, "", new Rect(12, 3, 186, 42), 23, Cream, localize: false);
                localPortInput.textComponent = text; localPortInput.targetGraphic = field.GetComponent<Image>();
                localPortInput.contentType = InputField.ContentType.IntegerNumber;
                localPortInput.lineType = InputField.LineType.SingleLine; localPortInput.characterLimit = 5;
                localPortInput.text = manager.LocalRoomPort.ToString(CultureInfo.InvariantCulture);
                LocalLabel("其他进程使用相同端口加入。", new Rect(24, 185, 436, 30), 18, Muted);
                localHostButton = Button(localHomePanel, "创建本地主机", new Rect(24, 237, 436, 56), () => SubmitLocalRoom(true));
                localJoinButton = Button(localHomePanel, "加入本地主机", new Rect(24, 313, 436, 56), () => SubmitLocalRoom(false));
                localCancelButton = Button(localHomePanel, "取消连接", new Rect(24, 399, 160, 48), manager.CancelLocalRoomConnection);
                localLabels.Add((localHostButton.GetComponentInChildren<Text>(), "创建本地主机"));
                localLabels.Add((localJoinButton.GetComponentInChildren<Text>(), "加入本地主机"));
                localLabels.Add((localCancelButton.GetComponentInChildren<Text>(), "取消连接"));
                localProgress = Label(localHomePanel, "", new Rect(205, 389, 255, 68), 18, Accent);
            }
            localHomePanel.gameObject.SetActive(true);
            UpdateLocalHome();
        }

        private void LocalLabel(string key, Rect rect, int size, Color color) =>
            localLabels.Add((Label(localHomePanel, key, rect, size, color), key));

        private void SubmitLocalRoom(bool host)
        {
            if (!ushort.TryParse(localPortInput.text, NumberStyles.None, CultureInfo.InvariantCulture, out ushort port) || port == 0)
            {
                manager.ShowMenuNotice("端口必须是 1 至 65535 的整数。"); localPortInput.Select(); return;
            }
            string error;
            bool accepted = host ? manager.TryCreateLocalPreparationRoom(port, out error) : manager.TryJoinLocalPreparationRoom(port, out error);
            if (!accepted) manager.ShowMenuNotice(error);
            UpdateLocalHome();
        }

        private void UpdateLocalHome()
        {
            if (localHomePanel == null || !localHomePanel.gameObject.activeSelf || GameOptionsPanel.IsOpen) return;
            bool pending = manager.IsLocalRoomConnecting;
            localPortInput.interactable = manager.CanStartLocalRoom;
            localHostButton.interactable = localJoinButton.interactable = manager.CanStartLocalRoom;
            localCancelButton.gameObject.SetActive(pending);
            localCancelButton.interactable = pending && !manager.IsLeavingRoom;
            foreach (var label in localLabels) label.text.text = Loc.Get(label.key);
            localProgress.text = Loc.Get(manager.IsLeavingRoom ? "正在清理连接…" :
                manager.LocalRoomOperation == LocalPreparationOperation.Creating ? "正在创建本地主机…" :
                manager.LocalRoomOperation == LocalPreparationOperation.Joining ? "正在连接并同步房间…" : "无需 Steam 好友邀请");
            WireLocalHomeNavigation();
            if (pending && !localWasConnecting && events != null) localCancelButton.Select();
            localWasConnecting = pending;
        }

        private bool HasLocalHomeFocus => localHomePanel != null && localHomePanel.gameObject.activeSelf &&
            events.currentSelectedGameObject != null && events.currentSelectedGameObject.activeInHierarchy &&
            events.currentSelectedGameObject.GetComponent<Selectable>()?.interactable == true &&
            events.currentSelectedGameObject.transform.IsChildOf(localHomePanel);

        private void WireLocalHomeNavigation()
        {
            var controls = navigation.Cast<Selectable>().Concat(new Selectable[] { localPortInput, localHostButton, localJoinButton, localCancelButton })
                .Distinct().Where(s => s != null && s.isActiveAndEnabled && s.interactable).ToArray();
            // Preserve the original four-button order, followed by port, Host, Join, Cancel.
            controls = controls.Where(s => s != localHostButton && s != localJoinButton && s != localCancelButton && s != localPortInput)
                .Concat(new Selectable[] { localPortInput, localHostButton, localJoinButton, localCancelButton }.Where(s => s.isActiveAndEnabled && s.interactable)).ToArray();
            for (int i = 0; i < controls.Length; i++)
            {
                var previous = controls[(i + controls.Length - 1) % controls.Length];
                var next = controls[(i + 1) % controls.Length];
                controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = previous, selectOnDown = next, selectOnLeft = previous, selectOnRight = next };
            }
            if (Input.GetKeyDown(KeyCode.Tab) && localTabFrame != Time.frameCount && controls.Length > 0)
            {
                localTabFrame = Time.frameCount;
                int index = System.Array.FindIndex(controls, s => s.gameObject == events.currentSelectedGameObject);
                int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
                controls[(index + step + controls.Length) % controls.Length].Select();
            }
        }

        private string RoomConnectionLabel(PreparationRoomSnapshot snapshot, int count)
        {
            if (manager.IsKcpPreparationRoom)
            {
                manager.GetComponent<NetworkBackendBootstrap>().TryGetKcpPort(out ushort port);
                return Loc.Get("本地 KCP · 127.0.0.1:{0} · {1} / 4", port, count);
            }
            return Loc.Get("{0}    {1} / 4", Loc.Get(snapshot.Online ? "好友房间" : "本地单人"), count) +
                (steam != null && steam.CurrentLobbyId != 0 ? Loc.Get("    ·    大厅 {0}", steam.CurrentLobbyId) : "");
        }
    }
}
