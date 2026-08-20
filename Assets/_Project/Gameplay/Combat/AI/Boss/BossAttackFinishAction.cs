using System;
using System.Reflection;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	[Serializable]
	[GeneratePropertyBag]
	[NodeDescription("BossAttackFinish", "", "Finish Attack", "", "Boss Logic", "9a3f4c80e566668d9480af14aa5419bf", false, "C:\\Users\\Hizagui-Tower\\Documents\\Repositories\\Divina\\Assets\\Scripts\\AstralShift\\HellMaiden\\AI\\Enemy\\Boss\\BossAttackFinishBehaviourGraphAction.cs")]
	public class BossAttackFinishAction : Unity.Behavior.Action
	{
		private sealed class BossAttackFinishAction_6876056aad04419ea03775ce30750f80_PropertyBag : ContainerPropertyBag<BossAttackFinishAction>
		{
			private class CurrentStatus_Property : Property<BossAttackFinishAction, Status>
			{
				public override string Name => "CurrentStatus";

				public override bool IsReadOnly => false;

				public override Status GetValue(ref BossAttackFinishAction container)
				{
					return container.CurrentStatus;
				}

				public override void SetValue(ref BossAttackFinishAction container, Status value)
				{
					container.CurrentStatus = value;
				}

				public CurrentStatus_Property()
				{
					PropertyInfo[] properties = typeof(BossAttackFinishAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
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

			private class Parent_Property : Property<BossAttackFinishAction, Node>
			{
				public override string Name => "Parent";

				public override bool IsReadOnly => true;

				public override Node GetValue(ref BossAttackFinishAction container)
				{
					return container.Parent;
				}

				public override void SetValue(ref BossAttackFinishAction container, Node value)
				{
					throw new InvalidOperationException("Property is ReadOnly");
				}

				public Parent_Property()
				{
					PropertyInfo[] properties = typeof(BossAttackFinishAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
					foreach (PropertyInfo propertyInfo in properties)
					{
						if (!(propertyInfo.Name != "Parent") && !(propertyInfo.DeclaringType != typeof(Unity.Behavior.Action)))
						{
							AddAttributes(propertyInfo.GetCustomAttributes());
							break;
						}
					}
				}
			}

			private class controller_Property : Property<BossAttackFinishAction, BlackboardVariable<BossAttackController>>
			{
				public override string Name => "controller";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<BossAttackController> GetValue(ref BossAttackFinishAction container)
				{
					return container.controller;
				}

				public override void SetValue(ref BossAttackFinishAction container, BlackboardVariable<BossAttackController> value)
				{
					container.controller = value;
				}

				public controller_Property()
				{
					AddAttributes(typeof(BossAttackFinishAction).GetField("controller", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			public BossAttackFinishAction_6876056aad04419ea03775ce30750f80_PropertyBag()
			{
				AddProperty(new CurrentStatus_Property());
				AddProperty(new Parent_Property());
				AddProperty(new controller_Property());
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
			return Status.Success;
		}

		protected override Status OnUpdate()
		{
			return Status.Success;
		}

		protected override void OnEnd()
		{
			_controller.FinishAttackPattern();
		}

		internal static void RegisterBossAttackFinishAction_6876056aad04419ea03775ce30750f80_PropertyBag()
		{
			PropertyBag.Register(new BossAttackFinishAction_6876056aad04419ea03775ce30750f80_PropertyBag());
		}
	}
}
