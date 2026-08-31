using System;
using AstralShift.Control.Controllers;
using Rewired;
using UnityEngine;

namespace AstralShift.Control
{
	public class InputHandler : MonoBehaviour
	{
		public ControllerLifetime controllerLifetime;

		private const int PlayerID = 0;

		private static Player _player;

		private GameController _currentController;

		private bool _controllerChangePending;

		private ControllerMapEnabler.RuleSet mapEnabler_default = new ControllerMapEnabler.RuleSet
		{
			tag = "Normal",
			rules = 
			{
				new ControllerMapEnabler.Rule
				{
					enable = false,
					controllerSetSelector = ControllerSetSelector.SelectAll()
				},
				new ControllerMapEnabler.Rule
				{
					enable = true,
					controllerSetSelector = ControllerSetSelector.SelectAll(),
					categoryNames = new string[1] { "Normal" }
				}
			}
		};

		private ControllerMapEnabler.RuleSet mapEnabler_UI = new ControllerMapEnabler.RuleSet
		{
			tag = "UI",
			rules = 
			{
				new ControllerMapEnabler.Rule
				{
					enable = false,
					controllerSetSelector = ControllerSetSelector.SelectAll()
				},
				new ControllerMapEnabler.Rule
				{
					enable = true,
					controllerSetSelector = ControllerSetSelector.SelectAll(),
					categoryNames = new string[1] { "UI" }
				}
			}
		};

		public GameController CurrentController
		{
			get
			{
				return _currentController;
			}
			set
			{
				_currentController = value;
			}
		}

		public void Init()
		{
			_player = ReInput.players.GetPlayer(0);
			_player.controllers.maps.mapEnabler.ruleSets.Add(mapEnabler_default);
			_player.controllers.maps.mapEnabler.ruleSets.Add(mapEnabler_UI);
			_player.controllers.maps.mapEnabler.enabled = true;
			SubscribeEvents();
			// controllerLifetime.Init();
			
			foreach (ControllerMapEnabler.RuleSet ruleSet in _player.controllers.maps.mapEnabler.ruleSets)
			{
				ruleSet.enabled = false;
			}
			_player.controllers.maps.mapEnabler.ruleSets.Find((ControllerMapEnabler.RuleSet item) => item.tag == "Normal").enabled = true;
			_player.controllers.maps.mapEnabler.Apply();
			_player.controllers.maps.mapEnabler.ruleSets.ForEach(delegate(ControllerMapEnabler.RuleSet ruleset)
			{
				MonoBehaviour.print(ruleset.tag + " : " + ruleset.enabled);
			});
		}

		private void SubscribeEvents()
		{
			SubscribeAction(Button1, 0, ButtonEvents);
			SubscribeAction(Button2, 4, ButtonEvents);
			SubscribeAction(Button3, 5, ButtonEvents);
			SubscribeAction(Button4, 6, ButtonEvents);
			SubscribeAction(LeftStickHorizontal, 1, AxisEvents);
			SubscribeAction(LeftStickVertical, 3, AxisEvents);
			SubscribeAction(LeftStickButton, 18, ButtonEvents);
			SubscribeAction(RightStickHorizontal, 7, AxisEvents);
			SubscribeAction(RightStickVertical, 8, AxisEvents);
			SubscribeAction(RightStickButton, 17, ButtonEvents);
			SubscribeAction(DirectionalUp, 9, ButtonEvents);
			SubscribeAction(DirectionalDown, 12, ButtonEvents);
			SubscribeAction(DirectionalLeft, 11, ButtonEvents);
			SubscribeAction(DirectionalRight, 10, ButtonEvents);
			SubscribeAction(LeftShoulder, 15, ButtonEvents);
			SubscribeAction(RightShoulder, 13, ButtonEvents);
			SubscribeAction(LeftTrigger, 16, ButtonEvents);
			SubscribeAction(RightTrigger, 14, ButtonEvents);
			SubscribeAction(UISubmit, 48, ButtonEvents);
			SubscribeAction(UICancelPressed, 49,
				InputActionEventType.ButtonJustPressed);
			SubscribeAction(UICancelHeld, 49,
				InputActionEventType.ButtonPressed);
			SubscribeAction(UICancelReleased, 49,
				InputActionEventType.ButtonJustReleased);
			SubscribeAction(UICancel, 49, ButtonEvents);
			SubscribeAction(UIVertical, 30, AxisEvents);
			SubscribeAction(Center1, 55, ButtonEvents);
			SubscribeAction(Center2, 52, ButtonEvents);
			SubscribeAction(UIButton3, 61, ButtonEvents);
			SubscribeAction(UIButton4, 62, ButtonEvents);
			SubscribeAction(UILeftStickHorizontal, 29, AxisEvents);
			SubscribeAction(UILeftStickVertical, 30, AxisEvents);
			SubscribeAction(UIRightStickHorizontal, 58, AxisEvents);
			SubscribeAction(UIRightStickVertical, 59, AxisEvents);
			SubscribeAction(UIDirectionalUp, 30,
				InputActionEventType.ButtonJustPressed,
				InputActionEventType.ButtonPressed,
				InputActionEventType.ButtonJustReleased,
				InputActionEventType.ButtonRepeating);
			SubscribeAction(UIDirectionalDown, 30,
				InputActionEventType.NegativeButtonJustPressed,
				InputActionEventType.NegativeButtonPressed,
				InputActionEventType.NegativeButtonJustReleased,
				InputActionEventType.NegativeButtonRepeating);
			SubscribeAction(UIDirectionalLeft, 29,
				InputActionEventType.NegativeButtonJustPressed,
				InputActionEventType.NegativeButtonPressed,
				InputActionEventType.NegativeButtonJustReleased);
			SubscribeAction(UIDirectionalRight, 29, ButtonEvents);
			SubscribeAction(UILeftTrigger, 54, ButtonEvents);
			SubscribeAction(UIRightTrigger, 53, ButtonEvents);
			SubscribeAction(UICenter1, 56, ButtonEvents);
			SubscribeAction(UICenter2, 57, ButtonEvents);
			SubscribeAction(DebugAction1Pressed, 50,
				InputActionEventType.ButtonJustPressed);
			SubscribeAction(DebugAction2Pressed, 51,
				InputActionEventType.ButtonJustPressed);
			SubscribeAction(DebugAction3Pressed, 60,
				InputActionEventType.ButtonJustPressed);
		}

		private static readonly InputActionEventType[] ButtonEvents =
		{
			InputActionEventType.ButtonJustPressed,
			InputActionEventType.ButtonPressed,
			InputActionEventType.ButtonJustReleased
		};

		private static readonly InputActionEventType[] AxisEvents =
		{
			InputActionEventType.AxisActiveOrJustInactive
		};

		private static void SubscribeAction(
			Action<InputActionEventData> callback,
			int actionId,
			params InputActionEventType[] eventTypes)
		{
			if (ReInput.mapping.GetAction(actionId) == null)
			{
				return;
			}

			for (int i = 0; i < eventTypes.Length; i++)
			{
				_player.AddInputEventDelegate(
					callback,
					UpdateLoopType.Update,
					eventTypes[i],
					actionId);
			}
		}

		private void Update()
		{
			if (_player.GetAnyButtonDown() && ControllerLifetime.ActiveControllerType != ControllerType.Mouse)
			{
				CurrentController?.AnyInputDown();
			}
		}

		public void OnMouseButtonStateChange(int button, bool state)
		{
			CurrentController?.AnyMouseInputStateChanged(button, state);
		}

		private void Button1(InputActionEventData data)
		{
			CurrentController?.Button1(data);
		}

		private void Button2(InputActionEventData data)
		{
			CurrentController?.Button2(data);
		}

		private void Button3(InputActionEventData data)
		{
			CurrentController?.Button3(data);
		}

		private void Button4(InputActionEventData data)
		{
			CurrentController?.Button4(data);
		}

		private void LeftStickHorizontal(InputActionEventData data)
		{
			CurrentController?.LeftStickHorizontal(data);
		}

		private void LeftStickVertical(InputActionEventData data)
		{
			CurrentController?.LeftStickVertical(data);
		}

		private void LeftStickButton(InputActionEventData data)
		{
			CurrentController?.LeftStickButton(data);
		}

		private void RightStickHorizontal(InputActionEventData data)
		{
			CurrentController?.RightStickHorizontal(data);
		}

		private void RightStickVertical(InputActionEventData data)
		{
			CurrentController?.RightStickVertical(data);
		}

		private void RightStickButton(InputActionEventData data)
		{
			CurrentController?.RightStickButton(data);
		}

		private void DirectionalUp(InputActionEventData data)
		{
			CurrentController?.DirectionalUp(data);
		}

		private void DirectionalDown(InputActionEventData data)
		{
			CurrentController?.DirectionalDown(data);
		}

		private void DirectionalLeft(InputActionEventData data)
		{
			CurrentController?.DirectionalLeft(data);
		}

		private void DirectionalRight(InputActionEventData data)
		{
			CurrentController?.DirectionalRight(data);
		}

		private void LeftShoulder(InputActionEventData data)
		{
			CurrentController?.LeftShoulder(data);
		}

		private void RightShoulder(InputActionEventData data)
		{
			CurrentController?.RightShoulder(data);
		}

		private void LeftTrigger(InputActionEventData data)
		{
			CurrentController?.LeftTrigger(data);
		}

		private void RightTrigger(InputActionEventData data)
		{
			CurrentController?.RightTrigger(data);
		}

		private void Center1(InputActionEventData data)
		{
			CurrentController?.Center1(data);
		}

		private void Center2(InputActionEventData data)
		{
			CurrentController?.Center2(data);
		}

		private void UISubmit(InputActionEventData data)
		{
			CurrentController?.UISubmit(data);
		}

		private void UICancelPressed(InputActionEventData data)
		{
			CurrentController?.UICancelPressed(data);
		}

		private void UICancelHeld(InputActionEventData data)
		{
			CurrentController?.UICancelHeld(data);
		}

		private void UICancelReleased(InputActionEventData data)
		{
			CurrentController?.UICancelReleased(data);
		}

		private void UIButton3(InputActionEventData data)
		{
			CurrentController?.UIButton3(data);
		}

		private void UIButton4(InputActionEventData data)
		{
			CurrentController?.UIButton4(data);
		}

		private void UIRightTrigger(InputActionEventData data)
		{
			CurrentController?.UIRightTrigger(data);
		}

		private void UILeftTrigger(InputActionEventData data)
		{
			CurrentController?.UILeftTrigger(data);
		}

		private void UICenter1(InputActionEventData data)
		{
			CurrentController?.UICenter1(data);
		}

		private void UICenter2(InputActionEventData data)
		{
			CurrentController?.UICenter2(data);
		}

		private void MouseHorizontal(InputActionEventData data)
		{
			CurrentController?.MouseHorizontal(data);
		}

		private void MouseVertical(InputActionEventData data)
		{
			CurrentController?.MouseVertical(data);
		}

		private void MouseLeftButton(InputActionEventData data)
		{
			CurrentController?.MouseLeftButton(data);
		}

		private void MouseRightButton(InputActionEventData data)
		{
			CurrentController?.MouseRightButton(data);
		}

		private void MouseWheel(InputActionEventData data)
		{
			CurrentController?.MouseWheel(data);
		}

		public void MousePosition(Vector2 value)
		{
			CurrentController?.MousePosition(value);
		}

		private void UICancel(InputActionEventData data)
		{
			CurrentController?.UICancel(data);
		}

		private void UIVertical(InputActionEventData data)
		{
			CurrentController?.UIVertical(data);
		}

		private void UIDirectionalRight(InputActionEventData data)
		{
			CurrentController?.UIDirectionalRight(data);
		}

		private void UIDirectionalLeft(InputActionEventData data)
		{
			CurrentController?.UIDirectionalLeft(data);
		}

		private void UIDirectionalUp(InputActionEventData data)
		{
			CurrentController?.UIDirectionalUp(data);
		}

		private void UIDirectionalDown(InputActionEventData data)
		{
			CurrentController?.UIDirectionalDown(data);
		}

		private void UILeftStickHorizontal(InputActionEventData data)
		{
			CurrentController?.UILeftStickHorizontal(data);
		}

		private void UILeftStickVertical(InputActionEventData data)
		{
			CurrentController?.UILeftStickVertical(data);
		}

		private void UIRightStickHorizontal(InputActionEventData data)
		{
			CurrentController?.UIRightStickHorizontal(data);
		}

		private void UIRightStickVertical(InputActionEventData data)
		{
			CurrentController?.UIRightStickVertical(data);
		}

		private void DebugAction1Pressed(InputActionEventData data)
		{
			CurrentController?.DebugAction1Pressed(data);
		}

		private void DebugAction2Pressed(InputActionEventData data)
		{
			CurrentController?.DebugAction2Pressed(data);
		}

		private void DebugAction3Pressed(InputActionEventData data)
		{
			CurrentController?.DebugAction3Pressed(data);
		}

		public static void EnableMenuInputs()
		{
			foreach (ControllerMapEnabler.RuleSet ruleSet in _player.controllers.maps.mapEnabler.ruleSets)
			{
				ruleSet.enabled = false;
			}
			_player.controllers.maps.mapEnabler.ruleSets.Find((ControllerMapEnabler.RuleSet item) => item.tag == "UI").enabled = true;
			_player.controllers.maps.mapEnabler.Apply();
			_player.controllers.maps.mapEnabler.ruleSets.ForEach(delegate(ControllerMapEnabler.RuleSet ruleset)
			{
				MonoBehaviour.print(ruleset.tag + " : " + ruleset.enabled);
			});
			ControllerLifetime.Refresh();
		}

		public static void EnableNormalInputs()
		{
			foreach (ControllerMapEnabler.RuleSet ruleSet in _player.controllers.maps.mapEnabler.ruleSets)
			{
				ruleSet.enabled = false;
			}
			_player.controllers.maps.mapEnabler.ruleSets.Find((ControllerMapEnabler.RuleSet item) => item.tag == "Normal").enabled = true;
			_player.controllers.maps.mapEnabler.Apply();
			_player.controllers.maps.mapEnabler.ruleSets.ForEach(delegate(ControllerMapEnabler.RuleSet ruleset)
			{
				MonoBehaviour.print(ruleset.tag + " : " + ruleset.enabled);
			});
			ControllerLifetime.Refresh();
		}
	}
}
