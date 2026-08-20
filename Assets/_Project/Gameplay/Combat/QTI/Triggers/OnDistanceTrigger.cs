using System.Collections.Generic;
using System.Linq;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Triggers
{
	public class OnDistanceTrigger : InteractionTrigger
	{
		public enum Mode
		{
			OnEnter = 0,
			OnExit = 1,
			OnStay = 2
		}

		public float distance = 1f;

		public string selectedTag = "Untagged";

		protected Dictionary<IInteractor, bool> _targets;

		public Mode mode;

		protected virtual float CalculateDistance(Vector3 a, Vector3 b)
		{
			return Vector3.Distance(a, b);
		}

		protected override void Awake()
		{
			base.Awake();
			if (_targets == null)
			{
				_targets = new Dictionary<IInteractor, bool>();
			}
		}

		private void Update()
		{
			GameObject[] array = GameObject.FindGameObjectsWithTag(selectedTag);
			if (array.Length == 0)
			{
				return;
			}
			List<IInteractor> list = new List<IInteractor>();
			GameObject[] array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				if (array2[i].TryGetComponent<IInteractor>(out var component))
				{
					_targets.TryAdd(component, value: false);
					list.Add(component);
				}
			}
			List<IInteractor> list2 = _targets.Keys.Except(list).ToList();
			for (int j = 0; j < list2.Count; j++)
			{
				_targets.Remove(list2[j]);
			}
			if (_targets.Count == 0)
			{
				return;
			}
			foreach (IInteractor item in new List<IInteractor>(_targets.Keys))
			{
				IInteractor interactor = item;
				bool flag = _targets[item];
				switch (mode)
				{
				case Mode.OnEnter:
					if (CalculateDistance(base.transform.position, interactor.GetTransform().position) > distance)
					{
						flag = false;
					}
					if (!flag)
					{
						if (CalculateDistance(base.transform.position, interactor.GetTransform().position) <= distance)
						{
							base.Interact(interactor);
							flag = true;
						}
						else
						{
							flag = false;
						}
					}
					break;
				case Mode.OnExit:
					if (flag)
					{
						if (CalculateDistance(base.transform.position, interactor.GetTransform().position) > distance)
						{
							base.Interact(interactor);
							flag = false;
						}
						else
						{
							flag = true;
						}
					}
					else if (CalculateDistance(base.transform.position, interactor.GetTransform().position) <= distance)
					{
						flag = true;
					}
					break;
				case Mode.OnStay:
					if (CalculateDistance(base.transform.position, interactor.GetTransform().position) <= distance)
					{
						base.Interact(interactor);
						return;
					}
					break;
				}
				_targets[interactor] = flag;
			}
		}
	}
}
