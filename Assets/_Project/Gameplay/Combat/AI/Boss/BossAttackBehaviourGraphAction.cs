using System;
using System.Reflection;
using AstralShift.BehaviourGraph.Flow;
using AstralShift.HellMaiden.AI.Boss.Minos;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	[Serializable]
	[GeneratePropertyBag]
	[NodeDescription("Execute Boss Attack", "", "Execute Attack: [Attack]", "", "Boss Logic", "c470a13578d956df563d908a3d95deaf", false, "C:\\Users\\Hizagui-Tower\\Documents\\Repositories\\Divina\\Assets\\Scripts\\AstralShift\\HellMaiden\\AI\\Enemy\\Boss\\BossAttackBehaviourGraphAction.cs")]
	public class BossAttackBehaviourGraphAction : Unity.Behavior.Action, IBehaviourGraphWeighted
	{
		private sealed class BossAttackBehaviourGraphAction_7ccf511b6218408186caab56e855f853_PropertyBag : ContainerPropertyBag<BossAttackBehaviourGraphAction>
		{
			private class CurrentStatus_Property : Property<BossAttackBehaviourGraphAction, Status>
			{
				public override string Name => "CurrentStatus";

				public override bool IsReadOnly => false;

				public override Status GetValue(ref BossAttackBehaviourGraphAction container)
				{
					return container.CurrentStatus;
				}

				public override void SetValue(ref BossAttackBehaviourGraphAction container, Status value)
				{
					container.CurrentStatus = value;
				}

				public CurrentStatus_Property()
				{
					PropertyInfo[] properties = typeof(BossAttackBehaviourGraphAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
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

			private class Parent_Property : Property<BossAttackBehaviourGraphAction, Node>
			{
				public override string Name => "Parent";

				public override bool IsReadOnly => true;

				public override Node GetValue(ref BossAttackBehaviourGraphAction container)
				{
					return container.Parent;
				}

				public override void SetValue(ref BossAttackBehaviourGraphAction container, Node value)
				{
					throw new InvalidOperationException("Property is ReadOnly");
				}

				public Parent_Property()
				{
					PropertyInfo[] properties = typeof(BossAttackBehaviourGraphAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
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

			private class Attack_Property : Property<BossAttackBehaviourGraphAction, BlackboardVariable<BossAttackBehaviour>>
			{
				public override string Name => "Attack";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<BossAttackBehaviour> GetValue(ref BossAttackBehaviourGraphAction container)
				{
					return container.Attack;
				}

				public override void SetValue(ref BossAttackBehaviourGraphAction container, BlackboardVariable<BossAttackBehaviour> value)
				{
					container.Attack = value;
				}

				public Attack_Property()
				{
					AddAttributes(typeof(BossAttackBehaviourGraphAction).GetField("Attack", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			private class initPositions_Property : Property<BossAttackBehaviourGraphAction, BlackboardVariable<bool>>
			{
				public override string Name => "initPositions";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<bool> GetValue(ref BossAttackBehaviourGraphAction container)
				{
					return container.initPositions;
				}

				public override void SetValue(ref BossAttackBehaviourGraphAction container, BlackboardVariable<bool> value)
				{
					container.initPositions = value;
				}

				public initPositions_Property()
				{
					AddAttributes(typeof(BossAttackBehaviourGraphAction).GetField("initPositions", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			public BossAttackBehaviourGraphAction_7ccf511b6218408186caab56e855f853_PropertyBag()
			{
				AddProperty(new CurrentStatus_Property());
				AddProperty(new Parent_Property());
				AddProperty(new Attack_Property());
				AddProperty(new initPositions_Property());
			}
		}

		[SerializeReference]
		public BlackboardVariable<BossAttackBehaviour> Attack;

		[SerializeReference]
		public BlackboardVariable<bool> initPositions;

		private bool successFlag;

		protected override Status OnStart()
		{
			successFlag = false;
			if (Attack.Value == null)
			{
				return Status.Failure;
			}
			ResetAttackCallbacks();
			if ((bool)initPositions)
			{
				(Attack.Value as JudgementBeamAttackBehaviour).InitializeAvailablePositions();
			}
			base.CurrentStatus = Status.Running;
			Attack.Value.Positioning();
			return base.CurrentStatus;
		}

		private void ResetAttackCallbacks()
		{
			Attack.Value.onPositioningEnd = null;
			Attack.Value.onWarningEnd = null;
			Attack.Value.onAttackEnd = null;
			BossAttackBehaviour value = Attack.Value;
			value.onPositioningEnd = (System.Action)Delegate.Combine(value.onPositioningEnd, new System.Action(Attack.Value.Warning));
			BossAttackBehaviour value2 = Attack.Value;
			value2.onWarningEnd = (System.Action)Delegate.Combine(value2.onWarningEnd, new System.Action(Attack.Value.Attack));
			BossAttackBehaviour value3 = Attack.Value;
			value3.onAttackEnd = (System.Action)Delegate.Combine(value3.onAttackEnd, (System.Action)delegate
			{
				successFlag = true;
			});
		}

		protected override Status OnUpdate()
		{
			if (successFlag)
			{
				base.CurrentStatus = Status.Success;
			}
			return base.CurrentStatus;
		}

		public float GetTotalWeight()
		{
			return Attack.Value.BaseWeight;
		}

		public float GetCurrentWeight()
		{
			return Attack.Value.CurrentWeight;
		}

		public void ApplyPityChance()
		{
			Attack.Value.ApplyPityChance();
		}

		public void RestartWeight()
		{
			throw new NotImplementedException();
		}

		internal static void RegisterBossAttackBehaviourGraphAction_7ccf511b6218408186caab56e855f853_PropertyBag()
		{
			PropertyBag.Register(new BossAttackBehaviourGraphAction_7ccf511b6218408186caab56e855f853_PropertyBag());
		}
	}
}
