using System;
using AstralShift.Control.MouseDeadzoneStrategy;
using Rewired;
using Rewired.Integration.UnityUI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.Control
{
	public class ControllerLifetime : MonoBehaviour
	{
		public static Player player;

		public static int playerId;

		public RewiredStandaloneInputModule mouseInputModule;

		private EventSystem _mouseEventSystem;

		public RewiredStandaloneInputModule defaultInputModule;

		private EventSystem _defaultEventSystem;

		private AstralShift.Control.MouseDeadzoneStrategy.MouseDeadzoneStrategy _mouseDeadzone;

		private static bool _enableMouseDeadzone;

		private static bool _unifiedEventSystem;

		public static bool EnableMouseDeadzone
		{
			get
			{
				return _enableMouseDeadzone;
			}
			set
			{
				if (_enableMouseDeadzone != value)
				{
					_enableMouseDeadzone = value;
					Refresh();
				}
			}
		}

		public static bool UnifiedEventSystem
		{
			get
			{
				return _unifiedEventSystem;
			}
			set
			{
				if (_unifiedEventSystem != value)
				{
					_unifiedEventSystem = value;
					Refresh();
				}
			}
		}

		public static Controller LastActiveController { get; private set; }

		public static ControllerType LastActiveControllerType { get; private set; }

		public static Controller ActiveController { get; private set; }

		public static ControllerType ActiveControllerType { get; private set; }

		public static event Action<ControllerType> OnBeforeControllerChanged;

		public static event Action OnControllerChanged;

		public static event Action OnActionsInputsChanged;

		private static event Action OnGlobalRefresh;

		private bool _controllerDelegateRegistered;

		private bool _rewiredEventsRegistered;

		public void Init()
		{
			if (!ReInput.isReady)
			{
				throw new InvalidOperationException("ControllerLifetime cannot initialize before the Rewired Input Manager is ready.");
			}
			if (mouseInputModule == null || defaultInputModule == null)
			{
				throw new InvalidOperationException("ControllerLifetime requires both Rewired UI input modules to be assigned.");
			}
			EventSystem mouseEventSystem = mouseInputModule.GetComponent<EventSystem>();
			EventSystem defaultEventSystem = defaultInputModule.GetComponent<EventSystem>();
			if (mouseEventSystem == null || defaultEventSystem == null)
			{
				throw new InvalidOperationException("Each Rewired UI input module must be on the same GameObject as an EventSystem.");
			}
			player = ReInput.players.GetPlayer(playerId);
			if (player == null)
			{
				throw new InvalidOperationException($"Rewired player {playerId} does not exist.");
			}
			ReInput.configuration.ignoreInputWhenAppNotInFocus = true;
			LastActiveController = player.controllers.GetController(ControllerType.Keyboard, 0);
			LastActiveControllerType = ControllerType.Keyboard;
			ActiveController = player.controllers.GetController(ControllerType.Keyboard, 0);
			ActiveControllerType = ControllerType.Keyboard;
			player.controllers.AddLastActiveControllerChangedDelegate(OnControllerDelegateTriggered);
			_controllerDelegateRegistered = true;
			ReInput.ControllerConnectedEvent += OnControllerConnected;
			ReInput.ControllerDisconnectedEvent += OnControllerDisconnected;
			_rewiredEventsRegistered = true;
			_mouseEventSystem = mouseEventSystem;
			_defaultEventSystem = defaultEventSystem;
			_mouseDeadzone = new AstralShift.Control.MouseDeadzoneStrategy.MouseDeadzoneStrategy();
			OnGlobalRefresh += delegate
			{
				_mouseDeadzone?.ResetState();
				ControllerChangeRefresh(player, ActiveController);
			};
			_unifiedEventSystem = false;
			_enableMouseDeadzone = false;
			ApplyInitialEventSystemState();
		}

		private void ApplyInitialEventSystemState()
		{
			bool useMouseEventSystem = UnifiedEventSystem || ActiveControllerType == ControllerType.Mouse;
			_defaultEventSystem.gameObject.SetActive(!useMouseEventSystem);
			_mouseEventSystem.gameObject.SetActive(useMouseEventSystem);
			EventSystem.current = useMouseEventSystem ? _mouseEventSystem : _defaultEventSystem;
		}

		protected void OnDestroy()
		{
			ControllerLifetime.OnGlobalRefresh = null;
			if (_controllerDelegateRegistered && player != null)
			{
				player.controllers.RemoveLastActiveControllerChangedDelegate(OnControllerDelegateTriggered);
				_controllerDelegateRegistered = false;
			}
			ControllerLifetime.OnBeforeControllerChanged = null;
			ControllerLifetime.OnControllerChanged = null;
			ControllerLifetime.OnActionsInputsChanged = null;
			if (_rewiredEventsRegistered)
			{
				ReInput.ControllerConnectedEvent -= OnControllerConnected;
				ReInput.ControllerDisconnectedEvent -= OnControllerDisconnected;
				_rewiredEventsRegistered = false;
			}
		}

		private void OnControllerConnected(ControllerStatusChangedEventArgs args)
		{
			Refresh();
		}

		private void OnControllerDisconnected(ControllerStatusChangedEventArgs args)
		{
			Refresh();
		}

		private void Update()
		{
			if (EnableMouseDeadzone)
			{
				_mouseDeadzone.Update(ActiveControllerType, ForceSwitchToMouse);
			}
		}

		private void OnControllerDelegateTriggered(Player player, Controller controller)
		{
			if (!EnableMouseDeadzone)
			{
				ControllerChangeRefresh(player, controller);
			}
			else if (_mouseDeadzone.ShouldAllowControllerSwitch(controller, delegate
			{
				ControllerChangeRefresh(player, controller);
			}))
			{
				ControllerChangeRefresh(player, controller);
			}
		}

		private void ForceSwitchToMouse()
		{
			Controller controller = player.controllers.GetController(ControllerType.Mouse, 0);
			if (controller != null)
			{
				ControllerChangeRefresh(player, controller);
			}
		}

		public static void Refresh()
		{
			ControllerLifetime.OnGlobalRefresh?.Invoke();
		}

		private void ControllerChangeRefresh(Player player, Controller controller)
		{
			ControllerLifetime.OnBeforeControllerChanged?.Invoke(controller.type);
			LastActiveController = ActiveController;
			LastActiveControllerType = ActiveControllerType;
			if (controller != null)
			{
				ActiveController = controller;
				ActiveControllerType = controller.type;
			}
			if (UnifiedEventSystem)
			{
				_defaultEventSystem.gameObject.SetActive(value: false);
				_mouseEventSystem.gameObject.SetActive(value: true);
				EventSystem.current = _mouseEventSystem;
				defaultInputModule?.DeactivateModule();
				mouseInputModule?.ActivateModule();
				ControllerLifetime.OnControllerChanged?.Invoke();
				ControllerLifetime.OnActionsInputsChanged?.Invoke();
				return;
			}
			if (ActiveControllerType == ControllerType.Mouse)
			{
				_defaultEventSystem.gameObject.SetActive(value: false);
				_mouseEventSystem.gameObject.SetActive(value: true);
				EventSystem.current = _mouseEventSystem;
				defaultInputModule?.DeactivateModule();
				mouseInputModule?.ActivateModule();
			}
			else
			{
				_mouseEventSystem.gameObject.SetActive(value: false);
				_defaultEventSystem.gameObject.SetActive(value: true);
				EventSystem.current = _defaultEventSystem;
				mouseInputModule?.DeactivateModule();
				defaultInputModule?.ActivateModule();
			}
			ControllerLifetime.OnControllerChanged?.Invoke();
			ControllerLifetime.OnActionsInputsChanged?.Invoke();
		}

		public static void RefreshInputs()
		{
			ControllerLifetime.OnActionsInputsChanged?.Invoke();
		}
	}
}
