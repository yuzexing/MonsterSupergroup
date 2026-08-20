using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace AstralShift.BehaviourGraph.Flow
{
	[Serializable]
	[GeneratePropertyBag]
	[NodeDescription("Weighted Random", "Selected a branch based on a weighted random algorithm. The children should be a Weighted Route or implement IWeightBehaviour", "", "", "Flow", "c07969a60ae25366ae2a88ac4cb3a4dc", false, "C:\\Users\\Hizagui-Tower\\Documents\\Repositories\\Divina\\Assets\\Scripts\\AstralShift\\BehaviourGraph\\WeightedRandom\\WeightedRandomAction.cs")]
	public class WeightedRandomAction : Composite
	{
		private sealed class WeightedRandomAction_e86309169e3943309c65d188d7799601_PropertyBag : ContainerPropertyBag<WeightedRandomAction>
		{
			private class CurrentStatus_Property : Property<WeightedRandomAction, Status>
			{
				public override string Name => "CurrentStatus";

				public override bool IsReadOnly => false;

				public override Status GetValue(ref WeightedRandomAction container)
				{
					return container.CurrentStatus;
				}

				public override void SetValue(ref WeightedRandomAction container, Status value)
				{
					container.CurrentStatus = value;
				}

				public CurrentStatus_Property()
				{
					PropertyInfo[] properties = typeof(WeightedRandomAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
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

			private class Parent_Property : Property<WeightedRandomAction, Node>
			{
				public override string Name => "Parent";

				public override bool IsReadOnly => true;

				public override Node GetValue(ref WeightedRandomAction container)
				{
					return container.Parent;
				}

				public override void SetValue(ref WeightedRandomAction container, Node value)
				{
					throw new InvalidOperationException("Property is ReadOnly");
				}

				public Parent_Property()
				{
					PropertyInfo[] properties = typeof(WeightedRandomAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
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

			private class Children_Property : Property<WeightedRandomAction, List<Node>>
			{
				public override string Name => "Children";

				public override bool IsReadOnly => true;

				public override List<Node> GetValue(ref WeightedRandomAction container)
				{
					return container.Children;
				}

				public override void SetValue(ref WeightedRandomAction container, List<Node> value)
				{
					throw new InvalidOperationException("Property is ReadOnly");
				}

				public Children_Property()
				{
					PropertyInfo[] properties = typeof(WeightedRandomAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
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

			public WeightedRandomAction_e86309169e3943309c65d188d7799601_PropertyBag()
			{
				AddProperty(new CurrentStatus_Property());
				AddProperty(new Parent_Property());
				AddProperty(new Children_Property());
				PropertyBag.RegisterList<WeightedRandomAction, Node>();
			}
		}

		private int _randomIndex;

		protected override Status OnStart()
		{
			_randomIndex = GetWeightedRandomChild();
			if (_randomIndex < base.Children.Count)
			{
				if (base.Children[_randomIndex] is IBehaviourGraphWeighted behaviourGraphWeighted)
				{
					behaviourGraphWeighted.ApplyPityChance();
				}
				Status status = StartNode(base.Children[_randomIndex]);
				if (status == Status.Success || status == Status.Failure)
				{
					return status;
				}
				return Status.Waiting;
			}
			return Status.Success;
		}

		protected override Status OnUpdate()
		{
			Status currentStatus = base.Children[_randomIndex].CurrentStatus;
			if (currentStatus == Status.Success || currentStatus == Status.Failure)
			{
				return currentStatus;
			}
			return Status.Waiting;
		}

		public int GetWeightedRandomChild()
		{
			float num = 0f;
			for (int i = 0; i < base.Children.Count; i++)
			{
				if (base.Children[i] is IBehaviourGraphWeighted behaviourGraphWeighted)
				{
					behaviourGraphWeighted.RestartWeight();
					num += behaviourGraphWeighted.GetCurrentWeight();
				}
				else
				{
					num += 1f;
				}
			}
			float num2 = UnityEngine.Random.Range(0f, num);
			float num3 = 0f;
			for (int j = 0; j < base.Children.Count; j++)
			{
				num3 = ((!(base.Children[j] is IBehaviourGraphWeighted behaviourGraphWeighted2)) ? (num3 + 1f) : (num3 + behaviourGraphWeighted2.GetCurrentWeight()));
				if (num2 < num3)
				{
					return j;
				}
			}
			return base.Children.Count - 1;
		}

		internal static void RegisterWeightedRandomAction_e86309169e3943309c65d188d7799601_PropertyBag()
		{
			PropertyBag.Register(new WeightedRandomAction_e86309169e3943309c65d188d7799601_PropertyBag());
		}
	}
}
