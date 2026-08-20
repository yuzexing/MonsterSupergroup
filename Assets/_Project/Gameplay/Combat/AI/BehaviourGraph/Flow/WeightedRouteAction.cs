using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace AstralShift.BehaviourGraph.Flow
{
	[Serializable]
	[GeneratePropertyBag]
	[NodeDescription("Weighted Route", "This node applies a weight to a branch (route).\nRequires a Weighted Random node as a parent to function properly.", "Base Weight: [baseWeight]   |   Pity Chance: [pityChance]", "", "Flow", "d6665e5c8b59d0cb1f42c62beab58742", false, "C:\\Users\\Hizagui-Tower\\Documents\\Repositories\\Divina\\Assets\\Scripts\\AstralShift\\BehaviourGraph\\WeightedRandom\\WeightedRouteAction.cs")]
	public class WeightedRouteAction : Composite, IBehaviourGraphWeighted
	{
		private sealed class WeightedRouteAction_000df69b83ff4407a102a5575bdc65b2_PropertyBag : ContainerPropertyBag<WeightedRouteAction>
		{
			private class CurrentStatus_Property : Property<WeightedRouteAction, Status>
			{
				public override string Name => "CurrentStatus";

				public override bool IsReadOnly => false;

				public override Status GetValue(ref WeightedRouteAction container)
				{
					return container.CurrentStatus;
				}

				public override void SetValue(ref WeightedRouteAction container, Status value)
				{
					container.CurrentStatus = value;
				}

				public CurrentStatus_Property()
				{
					PropertyInfo[] properties = typeof(WeightedRouteAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
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

			private class Parent_Property : Property<WeightedRouteAction, Node>
			{
				public override string Name => "Parent";

				public override bool IsReadOnly => true;

				public override Node GetValue(ref WeightedRouteAction container)
				{
					return container.Parent;
				}

				public override void SetValue(ref WeightedRouteAction container, Node value)
				{
					throw new InvalidOperationException("Property is ReadOnly");
				}

				public Parent_Property()
				{
					PropertyInfo[] properties = typeof(WeightedRouteAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
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

			private class Children_Property : Property<WeightedRouteAction, List<Node>>
			{
				public override string Name => "Children";

				public override bool IsReadOnly => true;

				public override List<Node> GetValue(ref WeightedRouteAction container)
				{
					return container.Children;
				}

				public override void SetValue(ref WeightedRouteAction container, List<Node> value)
				{
					throw new InvalidOperationException("Property is ReadOnly");
				}

				public Children_Property()
				{
					PropertyInfo[] properties = typeof(WeightedRouteAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
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

			private class baseWeight_Property : Property<WeightedRouteAction, BlackboardVariable<float>>
			{
				public override string Name => "baseWeight";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<float> GetValue(ref WeightedRouteAction container)
				{
					return container.baseWeight;
				}

				public override void SetValue(ref WeightedRouteAction container, BlackboardVariable<float> value)
				{
					container.baseWeight = value;
				}

				public baseWeight_Property()
				{
					AddAttributes(typeof(WeightedRouteAction).GetField("baseWeight", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			private class CurrentWeight_Property : Property<WeightedRouteAction, float>
			{
				public override string Name => "CurrentWeight";

				public override bool IsReadOnly => true;

				public override float GetValue(ref WeightedRouteAction container)
				{
					return container.CurrentWeight;
				}

				public override void SetValue(ref WeightedRouteAction container, float value)
				{
					throw new InvalidOperationException("Property is ReadOnly");
				}

				public CurrentWeight_Property()
				{
					PropertyInfo[] properties = typeof(WeightedRouteAction).GetProperties(BindingFlags.Instance | BindingFlags.Public);
					foreach (PropertyInfo propertyInfo in properties)
					{
						if (!(propertyInfo.Name != "CurrentWeight") && !(propertyInfo.DeclaringType != typeof(WeightedRouteAction)))
						{
							AddAttributes(propertyInfo.GetCustomAttributes());
							break;
						}
					}
				}
			}

			private class pityChance_Property : Property<WeightedRouteAction, BlackboardVariable<bool>>
			{
				public override string Name => "pityChance";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<bool> GetValue(ref WeightedRouteAction container)
				{
					return container.pityChance;
				}

				public override void SetValue(ref WeightedRouteAction container, BlackboardVariable<bool> value)
				{
					container.pityChance = value;
				}

				public pityChance_Property()
				{
					AddAttributes(typeof(WeightedRouteAction).GetField("pityChance", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			private class weightLossFactor_Property : Property<WeightedRouteAction, BlackboardVariable<float>>
			{
				public override string Name => "weightLossFactor";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<float> GetValue(ref WeightedRouteAction container)
				{
					return container.weightLossFactor;
				}

				public override void SetValue(ref WeightedRouteAction container, BlackboardVariable<float> value)
				{
					container.weightLossFactor = value;
				}

				public weightLossFactor_Property()
				{
					AddAttributes(typeof(WeightedRouteAction).GetField("weightLossFactor", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			private class weightRecoveryFactor_Property : Property<WeightedRouteAction, BlackboardVariable<float>>
			{
				public override string Name => "weightRecoveryFactor";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<float> GetValue(ref WeightedRouteAction container)
				{
					return container.weightRecoveryFactor;
				}

				public override void SetValue(ref WeightedRouteAction container, BlackboardVariable<float> value)
				{
					container.weightRecoveryFactor = value;
				}

				public weightRecoveryFactor_Property()
				{
					AddAttributes(typeof(WeightedRouteAction).GetField("weightRecoveryFactor", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			private class minWeightThreshold_Property : Property<WeightedRouteAction, BlackboardVariable<float>>
			{
				public override string Name => "minWeightThreshold";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<float> GetValue(ref WeightedRouteAction container)
				{
					return container.minWeightThreshold;
				}

				public override void SetValue(ref WeightedRouteAction container, BlackboardVariable<float> value)
				{
					container.minWeightThreshold = value;
				}

				public minWeightThreshold_Property()
				{
					AddAttributes(typeof(WeightedRouteAction).GetField("minWeightThreshold", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			private class recoveryInterval_Property : Property<WeightedRouteAction, BlackboardVariable<float>>
			{
				public override string Name => "recoveryInterval";

				public override bool IsReadOnly => false;

				public override BlackboardVariable<float> GetValue(ref WeightedRouteAction container)
				{
					return container.recoveryInterval;
				}

				public override void SetValue(ref WeightedRouteAction container, BlackboardVariable<float> value)
				{
					container.recoveryInterval = value;
				}

				public recoveryInterval_Property()
				{
					AddAttributes(typeof(WeightedRouteAction).GetField("recoveryInterval", BindingFlags.Instance | BindingFlags.Public).GetCustomAttributes());
				}
			}

			private class restartWeight_Property : Property<WeightedRouteAction, bool>
			{
				public override string Name => "restartWeight";

				public override bool IsReadOnly => false;

				public override bool GetValue(ref WeightedRouteAction container)
				{
					return container.restartWeight;
				}

				public override void SetValue(ref WeightedRouteAction container, bool value)
				{
					container.restartWeight = value;
				}
			}

			public WeightedRouteAction_000df69b83ff4407a102a5575bdc65b2_PropertyBag()
			{
				AddProperty(new CurrentStatus_Property());
				AddProperty(new Parent_Property());
				AddProperty(new Children_Property());
				AddProperty(new baseWeight_Property());
				AddProperty(new CurrentWeight_Property());
				AddProperty(new pityChance_Property());
				AddProperty(new weightLossFactor_Property());
				AddProperty(new weightRecoveryFactor_Property());
				AddProperty(new minWeightThreshold_Property());
				AddProperty(new recoveryInterval_Property());
				AddProperty(new restartWeight_Property());
				PropertyBag.RegisterList<WeightedRouteAction, Node>();
			}
		}

		[SerializeReference]
		public BlackboardVariable<float> baseWeight = new BlackboardVariable<float>(1f);

		[SerializeReference]
		[DontCreateProperty]
		public float currentWeight = 1f;

		[SerializeReference]
		public BlackboardVariable<bool> pityChance = new BlackboardVariable<bool>(value: false);

		[SerializeReference]
		public BlackboardVariable<float> weightLossFactor = new BlackboardVariable<float>(0.3f);

		[SerializeReference]
		public BlackboardVariable<float> weightRecoveryFactor = new BlackboardVariable<float>(0.1f);

		[SerializeReference]
		public BlackboardVariable<float> minWeightThreshold = new BlackboardVariable<float>(0.1f);

		[SerializeReference]
		public BlackboardVariable<float> recoveryInterval = new BlackboardVariable<float>(5f);

		private CancellationTokenSource _cancellationTokenSource;

		public bool restartWeight = true;

		public float BaseWeight => baseWeight.Value;

		[CreateProperty]
		public float CurrentWeight => currentWeight;

		public bool PityChance => pityChance.Value;

		public float WeightLossFactor => weightLossFactor.Value;

		public float WeightRecoveryFactor => weightRecoveryFactor.Value;

		public float MinWeightThreshold => minWeightThreshold.Value;

		public float RecoveryInterval => recoveryInterval.Value;

		protected override Status OnStart()
		{
			ApplyPityChance();
			if (base.Children == null)
			{
				return Status.Failure;
			}
			if (base.Children.Count == 0)
			{
				return Status.Failure;
			}
			return StartNode(base.Children[0]);
		}

		protected override Status OnUpdate()
		{
			Status currentStatus = base.Children[0].CurrentStatus;
			if (currentStatus == Status.Success || currentStatus == Status.Failure)
			{
				return currentStatus;
			}
			return Status.Waiting;
		}

		public float GetTotalWeight()
		{
			return baseWeight.Value;
		}

		public float GetCurrentWeight()
		{
			return currentWeight;
		}

		public void RestartWeight()
		{
			if (restartWeight)
			{
				ResetWeight();
				restartWeight = false;
			}
		}

		public void ResetWeight()
		{
			SetWeight(baseWeight.Value);
		}

		public void SetWeight(float value)
		{
			currentWeight = value;
		}

		public void IncreaseWeight(float value)
		{
			currentWeight += value;
		}

		public void DecreaseWeight(float value)
		{
			currentWeight -= value;
		}

		public void ApplyReductionFactor()
		{
			currentWeight *= weightLossFactor.Value;
		}

		public void ApplyPityChance()
		{
			if (PityChance)
			{
				ApplyReductionFactor();
				if (currentWeight < MinWeightThreshold)
				{
					SetWeight(MinWeightThreshold);
				}
				if (_cancellationTokenSource == null)
				{
					StartRestoringWeights();
				}
			}
		}

		public void StartRestoringWeights()
		{
			StopRestoringWeights();
			_cancellationTokenSource = new CancellationTokenSource();
			RestoreWeightsOverTimeAsync(_cancellationTokenSource.Token);
		}

		public void StopRestoringWeights()
		{
			_cancellationTokenSource?.Cancel();
			_cancellationTokenSource = null;
		}

		private async Task RestoreWeightsOverTimeAsync(CancellationToken cancellationToken)
		{
			if (!PityChance)
			{
				return;
			}
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					while (Time.timeScale == 0f)
					{
						await Task.Yield();
						if (cancellationToken.IsCancellationRequested)
						{
							return;
						}
					}
					if (CurrentWeight < BaseWeight)
					{
						IncreaseWeight(WeightRecoveryFactor);
						if (CurrentWeight > BaseWeight)
						{
							ResetWeight();
							StopRestoringWeights();
						}
					}
					float elapsedTime = 0f;
					while (elapsedTime < RecoveryInterval)
					{
						if (!base.GameObject.activeSelf)
						{
							StopRestoringWeights();
							return;
						}
						await Task.Yield();
						if (cancellationToken.IsCancellationRequested)
						{
							return;
						}
						if (Time.timeScale > 0f)
						{
							elapsedTime += Time.deltaTime;
						}
					}
				}
			}
			catch (TaskCanceledException)
			{
			}
		}

		internal static void RegisterWeightedRouteAction_000df69b83ff4407a102a5575bdc65b2_PropertyBag()
		{
			PropertyBag.Register(new WeightedRouteAction_000df69b83ff4407a102a5575bdc65b2_PropertyBag());
		}
	}
}
