using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	[Serializable]
	[GeneratePropertyBag]
	[NodeDescription("Boss Phase Selector", "", "Select Branch of Current Phase (In Order)\n [controller]", "", "Boss Logic", "d6e213f36d2cf479bdc8e82591c16b9d", false, "C:\\Users\\Hizagui-Tower\\Documents\\Repositories\\Divina\\Assets\\Scripts\\AstralShift\\HellMaiden\\AI\\Enemy\\Boss\\BossPhaseSelectorSequence.cs")]
	public class BossPhaseSelectorSequence : Composite
	{
		private sealed class BossPhaseSelectorSequence_22598b2c01954aabbe995d398865d0e5_PropertyBag : ContainerPropertyBag<BossPhaseSelectorSequence>
		{
			private class CurrentStatus_Property : Property<BossPhaseSelectorSequence, Status>
			{
				public override string Name => "CurrentStatus";

				public override bool IsReadOnly => false;

				public override Status GetValue(ref BossPhaseSelectorSequence container)
				{
					return container.CurrentStatus;
				}

				public override void SetValue(ref BossPhaseSelectorSequence container, Status value)
				{
					container.CurrentStatus = value;
				}

				public CurrentStatus_Property()
				{
					PropertyInfo[] properties = typeof(BossPhaseSelectorSequence).GetProperties(BindingFlags.Instance | BindingFlags.Public);
					foreach (PropertyInfo propertyInfo in properties)
					{
						if (!(propertyInfo.Name != "CurrentStatus") && !(propertyInfo.DeclaringType != typeof(Node)))
						{
							AddAttributes(propertyInfo.GetCustomAttributes());
							break;
						}
					}
				}
			}

			private class Parent_Property : Property<BossPhaseSelectorSequence, Node>
			{
				public override string Name => "Parent";

				public override bool IsReadOnly => true;

				public override Node GetValue(ref BossPhaseSelectorSequence container)
				{
					return container.Parent;
				}

				public override void SetValue(ref BossPhaseSelectorSequence container, Node value)
				{
					throw new InvalidOperationException("Property is ReadOnly");
				}

				public Parent_Property()
				{
					PropertyInfo[] properties = typeof(BossPhaseSelectorSequence).GetProperties(BindingFlags.Instance | BindingFlags.Public);
					foreach (PropertyInfo propertyInfo in properties)
					{
						if (!(propertyInfo.Name != "Parent") && !(propertyInfo.DeclaringType != typeof(Composite)))
						{
							AddAttributes(propertyInfo.GetCustomAttributes());
							break;
						}
					}
				}
			}

			private class Children_Property : Property<BossPhaseSelectorSequence, List<Node>>
			{
				public override string Name => "Children";

				public override bool IsReadOnly => true;

				public override List<Node> GetValue(ref BossPhaseSelectorSequence container)
				{
					return container.Children;
				}

				public override void SetValue(ref BossPhaseSelectorSequence container, List<Node> value)
				{
					throw new InvalidOperationException("Property is ReadOnly");
				}

				public Children_Property()
				{
					PropertyInfo[] properties = typeof(BossPhaseSelectorSequence).GetProperties(BindingFlags.Instance | BindingFlags.Public);
					foreach (PropertyInfo propertyInfo in properties)
					{
						if (!(propertyInfo.Name != "Children") && !(propertyInfo.DeclaringType != typeof(Composite)))
						{
							AddAttributes(propertyInfo.GetCustomAttributes());
							break;
						}
					}
				}
			}

			private class controller_Property : Property<BossPhaseSelectorSequence, BlackboardVariable<BossAttackController>>
			{
				public override string Name => "controller";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<BossAttackController> GetValue(ref BossPhaseSelectorSequence container)
				{
					return container.controller;
				}

				public override void SetValue(ref BossPhaseSelectorSequence container, BlackboardVariable<BossAttackController> value)
				{
					container.controller = value;
				}

				public controller_Property()
				{
					AddAttributes(typeof(BossPhaseSelectorSequence).GetField("controller", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			public BossPhaseSelectorSequence_22598b2c01954aabbe995d398865d0e5_PropertyBag()
			{
				AddProperty(new CurrentStatus_Property());
				AddProperty(new Parent_Property());
				AddProperty(new Children_Property());
				AddProperty(new controller_Property());
				PropertyBag.RegisterList<BossPhaseSelectorSequence, Node>();
			}
		}

		[SerializeReference]
		public BlackboardVariable<BossAttackController> controller;

		private BossAttackController _controller;

		protected override Status OnStart()
		{
			if (_controller == null)
			{
				_controller = controller.Value;
			}
			return Status.Running;
		}

		protected override Status OnUpdate()
		{
			return Status.Success;
		}

		protected override void OnEnd()
		{
			if (base.Children != null && base.Children.Count != 0 && _controller.bossController.CurrentPhaseIndex <= base.Children.Count - 1)
			{
				StartNode(base.Children[_controller.bossController.CurrentPhaseIndex]);
			}
		}

		internal static void RegisterBossPhaseSelectorSequence_22598b2c01954aabbe995d398865d0e5_PropertyBag()
		{
			PropertyBag.Register(new BossPhaseSelectorSequence_22598b2c01954aabbe995d398865d0e5_PropertyBag());
		}
	}
}
