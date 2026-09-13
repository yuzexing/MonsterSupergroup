using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class PreparationMenuView
    {
        private bool endPageActive, confirmLeaveEnded, endActionPending;
        private string endPageRun;

        private void UpdateRunEndPresentation()
        {
            bool ended = manager.IsRunEndScreen;
            if (ended && (!endPageActive || endPageRun != manager.RoomSnapshot.RunId))
            {
                foreach (var menu in FindObjectsByType<NetworkGameplayMenuController>(FindObjectsSortMode.None)) menu.CloseMenu();
                foreach (var options in FindObjectsByType<GameOptionsPanel>(FindObjectsSortMode.None)) options.Close();
                selectingWeapon = false; confirmLeaveEnded = false; endActionPending = false;
                endPageRun = manager.RoomSnapshot.RunId; Invalidate();
                if (events != null) events.SetSelectedGameObject(null);
            }
            endPageActive = ended;
            if (!ended) return;
            canvas.sortingOrder = 4000;
            contentGroup.interactable = true;
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
            foreach (var card in FindObjectsByType<CardPickMenu>(FindObjectsSortMode.None)) card.SetPresentationSuppressed(true);
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                var buttons = navigation.Where(b => b != null && b.interactable).ToArray();
                if (buttons.Length > 0)
                {
                    int index = System.Array.FindIndex(buttons, b => b.gameObject == events.currentSelectedGameObject);
                    int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
                    buttons[(index + step + buttons.Length) % buttons.Length].Select();
                }
            }
        }
        private void LateUpdate()
        {
            if (!endPageActive) return;
            foreach (var card in FindObjectsByType<CardPickMenu>(FindObjectsSortMode.None)) card.SetPresentationSuppressed(true);
        }
        private void DrawRunEnd()
        {
            bool transitioning = manager.RoomSnapshot.Phase == PreparationPhase.Transitioning;
            Label(content, "游戏结束", new Rect(200, 158, 880, 80), 52, Cream, TextAnchor.MiddleCenter);
            Label(content, "所有在线玩家均已倒地", new Rect(200, 260, 880, 40), 24, Muted, TextAnchor.MiddleCenter);
            if (transitioning)
            {
                confirmLeaveEnded = false;
                Label(content, manager.RoomSnapshot.NextAction == RunEndAction.Restart ? "正在清理战场，准备重新开始…" : "正在返回准备房间…",
                    new Rect(140, 400, 1000, 70), 26, Accent, TextAnchor.MiddleCenter);
                return;
            }
            if (confirmLeaveEnded)
            {
                Label(content, "确定离开房间并返回首页？", new Rect(200, 352, 880, 48), 24, Cream, TextAnchor.MiddleCenter);
                Button(content, "取消", new Rect(380, 448, 240, 56), () => { confirmLeaveEnded = false; Invalidate(); }, !endActionPending, true);
                Button(content, "确认离开", new Rect(660, 448, 240, 56), () => {
                    endActionPending = true; manager.LeavePreparationRoom(); Invalidate();
                }, !endActionPending);
                return;
            }
            bool host = manager.RoomSnapshot.Members != null && manager.RoomSnapshot.Members.Any(m =>
                m.ParticipantId == manager.RoomSnapshot.SelfId && m.IsHost);
            if (host)
            {
                Button(content, "回到大厅", new Rect(360, 374, 560, 60), () => SubmitEndAction(RunEndAction.ReturnToRoom), !endActionPending, true);
                Button(content, "重新开始", new Rect(360, 454, 560, 60), () => SubmitEndAction(RunEndAction.Restart), !endActionPending);
                Label(content, "保留队伍；重新开始将使用本局初始配置。", new Rect(200, 550, 880, 40), 18, Muted, TextAnchor.MiddleCenter);
            }
            else
            {
                Label(content, "等待房主选择下一步", new Rect(200, 366, 880, 48), 27, Accent, TextAnchor.MiddleCenter);
                Button(content, "离开房间", new Rect(460, 466, 360, 56), () => { confirmLeaveEnded = true; Invalidate(); });
            }
            if (endActionPending) Label(content, "正在处理…", new Rect(200, 606, 880, 34), 19, Accent, TextAnchor.MiddleCenter);
        }
        private void SubmitEndAction(RunEndAction action)
        {
            if (endActionPending) return;
            endActionPending = true; manager.ChooseRunEndAction(action); Invalidate();
        }
    }
}
