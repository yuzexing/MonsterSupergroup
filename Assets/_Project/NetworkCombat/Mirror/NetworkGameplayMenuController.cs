using System.Collections.Generic;
using MonsterSupergroup.Gameplay.Options;
using Loc = MonsterSupergroup.Gameplay.Options.MenuLocalization;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MonsterSupergroup.NetworkCombat
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed partial class NetworkGameplayMenuController : MonoBehaviour
    {
        [SerializeField] private CardPickMenu cardPickMenu;
        private BootGameplayNetworkManager manager;
        private PreparationMenuCatalog catalog;
        private PlayerMovement owner;
        private float nextRefresh;
        private int lastToggleFrame = -1;
        private bool spells;
        private int selectedSlot;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLock;
        private readonly List<Canvas> hiddenHudCanvases = new List<Canvas>();
        public bool IsOpen { get; private set; }
        public bool IsConfirming { get; private set; }
        public bool IsExiting { get; private set; }
        public GameplayMenuSnapshot Snapshot { get; private set; }
        public bool ShowingSpells => spells;
        public int SelectedSlot => selectedSlot;

        private void Awake()
        {
            manager = NetworkManager.singleton as BootGameplayNetworkManager;
            catalog = PreparationMenuCatalog.Load();
            if (cardPickMenu == null) cardPickMenu = GetComponentInChildren<CardPickMenu>(true);
            if (manager == null || !manager.IsGameplayTransitioning) BuildView();
        }
        private System.Collections.IEnumerator Start()
        {
            if (canvasRoot != null) yield break;
            // An orphan additive load can be scheduled for unload before its UI Start/Awake callbacks.
            // Wait for the accepted scene; destroying the old scene also cancels this coroutine.
            while (manager != null && manager.IsGameplayTransitioning) yield return null;
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded) BuildView();
        }
        private void OnEnable()
        {
            GameplayMenuInput.ToggleRequested += HandleEscape;
            Loc.Changed += OnMenuLanguageChanged;
        }
        private void OnMenuLanguageChanged()
        {
            if (mapCaption != null) mapCaption.text = Loc.Get("{0}    /    难度 · 中等", Loc.Get(catalog.MapName));
            nextRefresh = 0; RefreshStatistics();
        }
        private void OpenOptions()
        {
            if (!IsOpen || IsConfirming || IsExiting || GameOptionsPanel.IsOpen) return;
            menuGroup.interactable = false;
            GameOptionsPanel.Open(canvasRoot.transform, font, () => {
                if (this == null || !isActiveAndEnabled || !IsOpen) return;
                menuGroup.interactable = !IsConfirming; optionsButton.Select(); nextRefresh = 0;
            });
        }
        private void Update()
        {
            if (canvasRoot == null) return;
            manager = NetworkManager.singleton as BootGameplayNetworkManager;
            bool available = manager != null && manager.IsGameplayLoaded && NetworkClient.active &&
                !manager.IsRunEndScreen && manager.RoomSnapshot.Phase != PreparationPhase.Loading;
            if (canvasRoot != null) canvasRoot.SetActive(available || IsExiting);
            if (!available && !IsExiting) { CloseMenu(); return; }
            BindCurrentOwner();
            if (Application.isFocused && Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
            if (IsOpen && !IsExiting && !GameOptionsPanel.IsOpen && Application.isFocused && Input.GetKeyDown(KeyCode.Tab))
                MoveFocus(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
            if (IsOpen && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.2f;
                Snapshot = GameplayMenuSnapshotReader.Read(NetworkClient.localPlayer, catalog, NetworkTime.time);
                RefreshStatistics();
            }
        }
        private void LateUpdate()
        {
            if (!IsOpen) return;
            BindCurrentOwner();
            if (cardPickMenu != null) cardPickMenu.SetPresentationSuppressed(true);
            if (GameOptionsPanel.IsOpen || GameOptionsPanel.LastClosedFrame == Time.frameCount) return;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            Transform modalRoot = IsConfirming ? confirmPanel.transform : menuPanel.transform;
            if (!IsExiting && (selected == null || !selected.transform.IsChildOf(modalRoot)))
                (IsConfirming ? cancelButton : continueButton).Select();
        }
        private void BindCurrentOwner()
        {
            var identity = NetworkClient.localPlayer;
            var current = identity != null && identity.isOwned ? identity.GetComponent<PlayerMovement>() : null;
            if (owner != current)
            {
                if (owner != null) owner.SetMenuInputBlocked(false);
                owner = current;
            }
            if (owner != null && owner.IsMenuInputBlocked != IsOpen) owner.SetMenuInputBlocked(IsOpen);
        }
        public void HandleEscape()
        {
            if (BootGameplayNetworkManager.CombatHasEnded) return;
            if (lastToggleFrame == Time.frameCount || IsExiting || GameOptionsPanel.LastClosedFrame == Time.frameCount) return;
            lastToggleFrame = Time.frameCount;
            if (GameOptionsPanel.IsOpen) GameOptionsPanel.HandleBack();
            else if (IsConfirming) CancelExit();
            else if (IsOpen) CloseMenu();
            else OpenMenu();
        }
        public void OpenMenu()
        {
            manager = NetworkManager.singleton as BootGameplayNetworkManager;
            if (IsOpen || manager == null || !manager.IsGameplayLoaded || !NetworkClient.active ||
                (manager.IsRunEndScreen || manager.RoomSnapshot.Phase == PreparationPhase.Loading)) return;
            IsOpen = true; GameplayMenuInput.SetOpen(true);
            previousCursorVisible = Cursor.visible; previousCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            BindCurrentOwner();
            if (cardPickMenu != null) cardPickMenu.SetPresentationSuppressed(true);
            hiddenHudCanvases.Clear();
            foreach (var canvas in GetComponentsInChildren<Canvas>(true))
                if (canvas.enabled) { hiddenHudCanvases.Add(canvas); canvas.enabled = false; }
            menuPanel.SetActive(true); entryButton.gameObject.SetActive(false);
            nextRefresh = 0; SetPage(spells); continueButton.Select();
        }
        public void CloseMenu()
        {
            if (!IsOpen || IsExiting) return;
            GameOptionsPanel.CloseFor(canvasRoot.transform);
            IsOpen = false; IsConfirming = false; GameplayMenuInput.SetOpen(false);
            menuGroup.interactable = true; cancelButton.interactable = true; confirmButton.interactable = true;
            if (owner != null) owner.SetMenuInputBlocked(false);
            menuPanel.SetActive(false); confirmPanel.SetActive(false); entryButton.gameObject.SetActive(true);
            foreach (var canvas in hiddenHudCanvases) if (canvas != null) canvas.enabled = true;
            hiddenHudCanvases.Clear();
            Cursor.visible = previousCursorVisible; Cursor.lockState = previousCursorLock;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (cardPickMenu != null) cardPickMenu.SetPresentationSuppressed(false);
        }
        public void RequestExit()
        {
            if (!IsOpen || IsConfirming || IsExiting || GameOptionsPanel.IsOpen) return;
            IsConfirming = true;
            bool host = NetworkServer.active;
            int members = 0;
            if (host) foreach (var participant in manager.Session.Participants)
                if (participant.ConnectionState == RunConnectionState.Connected) members++;
            confirmMessage.text = host ? members > 1
                ? "确定结束本局并返回首页？\n其他玩家也会断开连接。" : "确定结束本局并返回首页？"
                : "确定离开本局并返回首页？";
            Loc.Bind(confirmMessage, confirmMessage.text);
            confirmPanel.SetActive(true); menuGroup.interactable = false; cancelButton.Select();
            ConfigureNavigation();
        }
        public void CancelExit()
        {
            if (!IsConfirming || IsExiting) return;
            IsConfirming = false; confirmPanel.SetActive(false); menuGroup.interactable = true;
            ConfigureNavigation(); exitButton.Select();
        }
        public void ConfirmExit()
        {
            if (!IsConfirming || IsExiting || manager == null) return;
            IsExiting = true; Loc.Bind(confirmMessage, "正在退出…");
            cancelButton.interactable = false; confirmButton.interactable = false;
            Debug.Log("[GameplayMenu] Confirmed exit; returning to homepage through session cleanup.");
            manager.LeavePreparationRoom();
        }
        public void SetPage(bool showSpells)
        {
            spells = showSpells;
            characterScroll.gameObject.SetActive(!spells); weaponArea.SetActive(spells);
            SetButtonFill(characterTab, !spells ? Selected : Panel);
            SetButtonFill(spellTab, spells ? Selected : Panel);
            ConfigureNavigation();
        }
        public void SelectWeapon(int slot)
        {
            selectedSlot = Mathf.Clamp(slot, 0, PlayerBuildRuntime.HandSlotCount - 1);
            RefreshStatistics();
        }
        private List<Selectable> NavigationItems()
        {
            if (IsConfirming) return new List<Selectable> { cancelButton, confirmButton };
            var items = new List<Selectable> { continueButton, optionsButton, feedbackButton, exitButton, characterTab, spellTab };
            if (spells) items.AddRange(slotButtons);
            items.Add(spells ? weaponScroll.verticalScrollbar : characterScroll.verticalScrollbar);
            return items;
        }
        private void ConfigureNavigation()
        {
            var items = NavigationItems();
            for (int i = 0; i < items.Count; i++)
            {
                var previous = items[(i + items.Count - 1) % items.Count]; var next = items[(i + 1) % items.Count];
                items[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = previous, selectOnDown = next, selectOnLeft = previous, selectOnRight = next };
            }
        }
        private void MoveFocus(int direction)
        {
            var items = NavigationItems();
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            int index = items.FindIndex(x => x.gameObject == selected);
            items[(index + direction + items.Count) % items.Count].Select();
        }
        private void OnDisable()
        {
            GameplayMenuInput.ToggleRequested -= HandleEscape;
            Loc.Changed -= OnMenuLanguageChanged;
            IsExiting = false; CloseMenu(); GameplayMenuInput.SetOpen(false);
            if (owner != null) owner.SetMenuInputBlocked(false);
            owner = null; Snapshot = null;
            if (canvasRoot != null) canvasRoot.SetActive(false);
        }
        private void OnDestroy()
        {
            if (canvasRoot != null) Destroy(canvasRoot);
            if (font != null) Destroy(font);
        }
    }
}
