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
			controllerLifetime.Init();
		}

		private void SubscribeEvents()
		{
			_player.AddInputEventDelegate(Button1, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 0);
			_player.AddInputEventDelegate(Button1, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 0);
			_player.AddInputEventDelegate(Button1, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 0);
			_player.AddInputEventDelegate(Button2, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 4);
			_player.AddInputEventDelegate(Button2, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 4);
			_player.AddInputEventDelegate(Button2, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 4);
			_player.AddInputEventDelegate(Button3, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 5);
			_player.AddInputEventDelegate(Button3, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 5);
			_player.AddInputEventDelegate(Button3, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 5);
			_player.AddInputEventDelegate(Button4, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 6);
			_player.AddInputEventDelegate(Button4, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 6);
			_player.AddInputEventDelegate(Button4, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 6);
			_player.AddInputEventDelegate(LeftStickHorizontal, UpdateLoopType.Update, InputActionEventType.AxisActiveOrJustInactive, 2);
			_player.AddInputEventDelegate(LeftStickVertical, UpdateLoopType.Update, InputActionEventType.AxisActiveOrJustInactive, 3);
			_player.AddInputEventDelegate(LeftStickButton, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 18);
			_player.AddInputEventDelegate(LeftStickButton, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 18);
			_player.AddInputEventDelegate(LeftStickButton, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 18);
			_player.AddInputEventDelegate(RightStickHorizontal, UpdateLoopType.Update, InputActionEventType.AxisActiveOrJustInactive, 7);
			_player.AddInputEventDelegate(RightStickVertical, UpdateLoopType.Update, InputActionEventType.AxisActiveOrJustInactive, 8);
			_player.AddInputEventDelegate(RightStickButton, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 17);
			_player.AddInputEventDelegate(RightStickButton, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 17);
			_player.AddInputEventDelegate(RightStickButton, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 17);
			_player.AddInputEventDelegate(DirectionalUp, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 9);
			_player.AddInputEventDelegate(DirectionalUp, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 9);
			_player.AddInputEventDelegate(DirectionalUp, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 9);
			_player.AddInputEventDelegate(DirectionalDown, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 12);
			_player.AddInputEventDelegate(DirectionalDown, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 12);
			_player.AddInputEventDelegate(DirectionalDown, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 12);
			_player.AddInputEventDelegate(DirectionalLeft, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 11);
			_player.AddInputEventDelegate(DirectionalLeft, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 11);
			_player.AddInputEventDelegate(DirectionalLeft, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 11);
			_player.AddInputEventDelegate(DirectionalRight, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 10);
			_player.AddInputEventDelegate(DirectionalRight, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 10);
			_player.AddInputEventDelegate(DirectionalRight, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 10);
			_player.AddInputEventDelegate(LeftShoulder, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 15);
			_player.AddInputEventDelegate(LeftShoulder, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 15);
			_player.AddInputEventDelegate(LeftShoulder, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 15);
			_player.AddInputEventDelegate(RightShoulder, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 13);
			_player.AddInputEventDelegate(RightShoulder, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 13);
			_player.AddInputEventDelegate(RightShoulder, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 13);
			_player.AddInputEventDelegate(LeftTrigger, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 16);
			_player.AddInputEventDelegate(LeftTrigger, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 16);
			_player.AddInputEventDelegate(LeftTrigger, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 16);
			_player.AddInputEventDelegate(RightTrigger, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 14);
			_player.AddInputEventDelegate(RightTrigger, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 14);
			_player.AddInputEventDelegate(RightTrigger, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 14);
			_player.AddInputEventDelegate(UISubmit, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 48);
			_player.AddInputEventDelegate(UISubmit, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 48);
			_player.AddInputEventDelegate(UISubmit, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 48);
			_player.AddInputEventDelegate(UICancelPressed, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 49);
			_player.AddInputEventDelegate(UICancelHeld, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 49);
			_player.AddInputEventDelegate(UICancelReleased, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 49);
			_player.AddInputEventDelegate(UICancel, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 49);
			_player.AddInputEventDelegate(UICancel, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 49);
			_player.AddInputEventDelegate(UICancel, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 49);
			_player.AddInputEventDelegate(UIVertical, UpdateLoopType.Update, InputActionEventType.AxisActiveOrJustInactive, 30);
			_player.AddInputEventDelegate(Center1, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 55);
			_player.AddInputEventDelegate(Center1, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 55);
			_player.AddInputEventDelegate(Center1, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 55);
			_player.AddInputEventDelegate(Center2, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 52);
			_player.AddInputEventDelegate(Center2, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 52);
			_player.AddInputEventDelegate(Center2, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 52);
			_player.AddInputEventDelegate(UIButton3, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 61);
			_player.AddInputEventDelegate(UIButton3, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 61);
			_player.AddInputEventDelegate(UIButton3, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 61);
			_player.AddInputEventDelegate(UIButton4, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 62);
			_player.AddInputEventDelegate(UIButton4, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 62);
			_player.AddInputEventDelegate(UIButton4, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 62);
			_player.AddInputEventDelegate(UILeftStickHorizontal, UpdateLoopType.Update, InputActionEventType.AxisActiveOrJustInactive, 29);
			_player.AddInputEventDelegate(UILeftStickVertical, UpdateLoopType.Update, InputActionEventType.AxisActiveOrJustInactive, 30);
			_player.AddInputEventDelegate(UIRightStickHorizontal, UpdateLoopType.Update, InputActionEventType.AxisActiveOrJustInactive, 58);
			_player.AddInputEventDelegate(UIRightStickVertical, UpdateLoopType.Update, InputActionEventType.AxisActiveOrJustInactive, 59);
			_player.AddInputEventDelegate(UIDirectionalUp, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 30);
			_player.AddInputEventDelegate(UIDirectionalUp, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 30);
			_player.AddInputEventDelegate(UIDirectionalUp, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 30);
			_player.AddInputEventDelegate(UIDirectionalUp, UpdateLoopType.Update, InputActionEventType.ButtonRepeating, 30);
			_player.AddInputEventDelegate(UIDirectionalDown, UpdateLoopType.Update, InputActionEventType.NegativeButtonJustPressed, 30);
			_player.AddInputEventDelegate(UIDirectionalDown, UpdateLoopType.Update, InputActionEventType.NegativeButtonPressed, 30);
			_player.AddInputEventDelegate(UIDirectionalDown, UpdateLoopType.Update, InputActionEventType.NegativeButtonJustReleased, 30);
			_player.AddInputEventDelegate(UIDirectionalDown, UpdateLoopType.Update, InputActionEventType.NegativeButtonRepeating, 30);
			_player.AddInputEventDelegate(UIDirectionalLeft, UpdateLoopType.Update, InputActionEventType.NegativeButtonJustPressed, 29);
			_player.AddInputEventDelegate(UIDirectionalLeft, UpdateLoopType.Update, InputActionEventType.NegativeButtonPressed, 29);
			_player.AddInputEventDelegate(UIDirectionalLeft, UpdateLoopType.Update, InputActionEventType.NegativeButtonJustReleased, 29);
			_player.AddInputEventDelegate(UIDirectionalRight, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 29);
			_player.AddInputEventDelegate(UIDirectionalRight, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 29);
			_player.AddInputEventDelegate(UIDirectionalRight, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 29);
			_player.AddInputEventDelegate(UILeftTrigger, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 54);
			_player.AddInputEventDelegate(UILeftTrigger, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 54);
			_player.AddInputEventDelegate(UILeftTrigger, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 54);
			_player.AddInputEventDelegate(UIRightTrigger, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 53);
			_player.AddInputEventDelegate(UIRightTrigger, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 53);
			_player.AddInputEventDelegate(UIRightTrigger, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 53);
			_player.AddInputEventDelegate(UICenter1, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 56);
			_player.AddInputEventDelegate(UICenter1, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 56);
			_player.AddInputEventDelegate(UICenter1, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 56);
			_player.AddInputEventDelegate(UICenter2, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 57);
			_player.AddInputEventDelegate(UICenter2, UpdateLoopType.Update, InputActionEventType.ButtonPressed, 57);
			_player.AddInputEventDelegate(UICenter2, UpdateLoopType.Update, InputActionEventType.ButtonJustReleased, 57);
			_player.AddInputEventDelegate(DebugAction1Pressed, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 50);
			_player.AddInputEventDelegate(DebugAction2Pressed, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 51);
			_player.AddInputEventDelegate(DebugAction3Pressed, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, 60);
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
