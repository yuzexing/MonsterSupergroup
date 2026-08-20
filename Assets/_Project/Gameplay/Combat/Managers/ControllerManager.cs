using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.Control;
using AstralShift.Control.Controllers;
using AstralShift.DebugTools;
using UnityEngine;

namespace AstralShift.Managers
{
	public class ControllerManager : MonoBehaviour
	{
		public static ControllerManager Instance;

		public bool AutoInitFoundControllers;

		public InputHandler inputHandler;

		private ControllerStack<GameController> _controllerStack;

		private Dictionary<Type, GameController> _availableControllers;

		public ControllerStack<GameController> Stack => _controllerStack;

		public GameController[] AvailableControllers => _availableControllers?.Values.ToArray();

		public GameController CurrentController
		{
			get
			{
				if (_controllerStack.Count <= 0)
				{
					return null;
				}
				return _controllerStack.Peek();
			}
		}

		public void Init()
		{
			Instance = this;
			inputHandler.Init();
			_controllerStack = new ControllerStack<GameController>();
			_availableControllers = new Dictionary<Type, GameController>();
			if (AutoInitFoundControllers)
			{
				GameController[] array = UnityEngine.Object.FindObjectsByType<GameController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				foreach (GameController controller in array)
				{
					Subscribe(controller, init: true);
				}
			}
			OverrideGameController<NoInputGameController>();
		}

		public T OverrideGameController<T>() where T : GameController
		{
			if (!_availableControllers.TryGetValue(typeof(T), out var value))
			{
				DBL.Log(DBL.Module.Controllers, "Controller type " + typeof(T)?.ToString() + " not available");
				return null;
			}
			DBL.Log(DBL.Module.Controllers, "Overrode <color=yellow><b>" + CurrentController?.GetType().Name + "</b></color> with <color=yellow><b>" + value.GetType().Name + "</b></color>.");
			_controllerStack.Push((T)value);
			inputHandler.CurrentController = value;
			return value as T;
		}

		public bool OverrideGameController(GameController controller)
		{
			if (!AvailableControllers.Contains(controller))
			{
				DBL.Log(DBL.Module.Controllers, "Controller " + controller.name + " not available or subscribed");
				return false;
			}
			DBL.Log(DBL.Module.Controllers, "Overrode <color=yellow><b>" + CurrentController?.GetType().Name + "</b></color> with <color=yellow><b>" + controller.GetType().Name + "</b></color>.");
			_controllerStack.Push(controller);
			inputHandler.CurrentController = controller;
			return true;
		}

		public void YieldGameController()
		{
			if (_controllerStack.Count == 0)
			{
				MonoBehaviour.print("Tried to yield Game Controller but there will be nothing to yield to");
				return;
			}
			GameController gameController = _controllerStack.Pop();
			if (_controllerStack.Count != 0)
			{
				DBL.Log(DBL.Module.Controllers, (CurrentController != null) ? ("yielded <color=yellow><b>" + gameController.GetType().Name + "</b></color> to <color=yellow><b>" + CurrentController.GetType().Name + "</b></color>.") : ("yielded <color=yellow><b>" + gameController.GetType().Name + "</b></color>."));
				inputHandler.CurrentController = _controllerStack.Peek();
			}
		}

		public T ReplaceController<T>() where T : GameController
		{
			if (!_availableControllers.TryGetValue(typeof(T), out var value))
			{
				DBL.Log(DBL.Module.Controllers, "Controller type " + typeof(T)?.ToString() + " not available");
				return null;
			}
			DBL.Log(DBL.Module.Controllers, "Replaced <color=yellow><b>" + CurrentController?.GetType().Name + "</b></color> with <color=yellow><b>" + value.GetType().Name + "</b></color>.");
			if (_controllerStack.Replace((T)value) == null)
			{
				DBL.Log(DBL.Module.Controllers, "No Controller in stack to replace. New <color=yellow><b>" + value.GetType().Name + "</b></color> pushed.");
			}
			inputHandler.CurrentController = _controllerStack.Peek();
			return (T)value;
		}

		public void RenewControllerStack(bool runDeactivate = false)
		{
			if (runDeactivate)
			{
				_controllerStack.Peek().Deactivate();
			}
			inputHandler.CurrentController = null;
			_controllerStack.Clear();
			DBL.Log(DBL.Module.Controllers, "Controller Stack emptied.");
		}

		public void Subscribe(GameController controller, bool init = false)
		{
			if (_availableControllers == null)
			{
				_availableControllers = new Dictionary<Type, GameController>();
			}
			_availableControllers.Add(controller.GetType(), controller);
			Debug.Log(controller.GetType());
			if (init)
			{
				controller.Init();
			}
		}

		public void UnSubscribe(GameController controller)
		{
			if (_availableControllers != null)
			{
				_availableControllers.Remove(controller.GetType());
			}
		}
	}
}
