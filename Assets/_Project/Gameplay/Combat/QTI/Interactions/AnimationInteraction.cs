using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/AnimationInteraction")]
	public class AnimationInteraction : Interaction
	{
		[Serializable]
		public class AnimatorLayer
		{
			public string name;

			public List<string> states;

			public int[] hashes;

			public AnimatorLayer(string name, List<string> states)
			{
				this.name = name;
				this.states = states;
				hashes = new int[this.states.Count];
				for (int i = 0; i < hashes.Length; i++)
				{
					hashes[i] = Animator.StringToHash(this.states[i]);
				}
			}

			public AnimatorLayer()
			{
				name = "";
				states = new List<string>();
				hashes = null;
			}
		}

		public enum AnimationInteractionMode
		{
			Play = 0,
			Parameters = 1
		}

		[Serializable]
		public class AnimatorParameter
		{
			public string name;

			public int hash;

			public AnimatorControllerParameterType type;

			public int intValue;

			public float floatValue;

			public bool boolValue;

			public object Value
			{
				get
				{
					return type switch
					{
						AnimatorControllerParameterType.Int => intValue, 
						AnimatorControllerParameterType.Float => floatValue, 
						AnimatorControllerParameterType.Bool => boolValue, 
						_ => null, 
					};
				}
				set
				{
					switch (type)
					{
					case AnimatorControllerParameterType.Int:
						intValue = (int)value;
						break;
					case AnimatorControllerParameterType.Float:
						floatValue = (float)value;
						break;
					case AnimatorControllerParameterType.Bool:
						boolValue = (bool)value;
						break;
					case (AnimatorControllerParameterType)2:
						break;
					}
				}
			}

			public AnimatorParameter(AnimatorControllerParameter parameter, object value)
			{
				name = parameter.name;
				hash = Animator.StringToHash(name);
				type = parameter.type;
				switch (type)
				{
				case AnimatorControllerParameterType.Int:
					intValue = (int)value;
					break;
				case AnimatorControllerParameterType.Float:
					floatValue = (float)value;
					break;
				case AnimatorControllerParameterType.Bool:
					boolValue = (bool)value;
					break;
				case (AnimatorControllerParameterType)2:
					break;
				}
			}

			public AnimatorParameter(AnimatorControllerParameter parameter)
			{
				name = parameter.name;
				hash = Animator.StringToHash(name);
				type = parameter.type;
				switch (type)
				{
				case AnimatorControllerParameterType.Int:
					intValue = 0;
					break;
				case AnimatorControllerParameterType.Float:
					floatValue = 0f;
					break;
				case AnimatorControllerParameterType.Bool:
					boolValue = false;
					break;
				case (AnimatorControllerParameterType)2:
					break;
				}
			}

			public AnimatorParameter(AnimatorParameter parameter)
			{
				name = parameter.name;
				hash = parameter.hash;
				type = parameter.type;
				switch (type)
				{
				case AnimatorControllerParameterType.Int:
					intValue = parameter.intValue;
					break;
				case AnimatorControllerParameterType.Float:
					floatValue = parameter.floatValue;
					break;
				case AnimatorControllerParameterType.Bool:
					boolValue = parameter.boolValue;
					break;
				case (AnimatorControllerParameterType)2:
					break;
				}
			}

			public void SetParameter(AnimatorControllerParameter parameter, object value)
			{
				name = parameter.name;
				hash = Animator.StringToHash(name);
				type = parameter.type;
				switch (type)
				{
				case AnimatorControllerParameterType.Int:
					intValue = (int)value;
					break;
				case AnimatorControllerParameterType.Float:
					floatValue = (float)value;
					break;
				case AnimatorControllerParameterType.Bool:
					boolValue = (bool)value;
					break;
				case (AnimatorControllerParameterType)2:
					break;
				}
			}

			public void SetParameter(AnimatorControllerParameter parameter)
			{
				name = parameter.name;
				hash = Animator.StringToHash(name);
				type = parameter.type;
				switch (type)
				{
				case AnimatorControllerParameterType.Int:
					intValue = 0;
					break;
				case AnimatorControllerParameterType.Float:
					floatValue = 0f;
					break;
				case AnimatorControllerParameterType.Bool:
					boolValue = false;
					break;
				case (AnimatorControllerParameterType)2:
					break;
				}
			}

			public void SetParameter(AnimatorParameter parameter)
			{
				name = parameter.name;
				hash = parameter.hash;
				type = parameter.type;
				switch (type)
				{
				case AnimatorControllerParameterType.Int:
					intValue = parameter.intValue;
					break;
				case AnimatorControllerParameterType.Float:
					floatValue = parameter.floatValue;
					break;
				case AnimatorControllerParameterType.Bool:
					boolValue = parameter.boolValue;
					break;
				case (AnimatorControllerParameterType)2:
					break;
				}
			}
		}

		[SerializeField]
		private Animator animator;

		public AnimationInteractionMode mode;

		[HideInInspector]
		public RuntimeAnimatorController animatorController;

		[HideInInspector]
		public AnimatorLayer[] layers;

		[HideInInspector]
		public string[] states;

		[HideInInspector]
		public int layerIndex;

		[HideInInspector]
		public int stateIndex;

		[HideInInspector]
		public List<AnimatorParameter> parameters;

		[HideInInspector]
		public List<AnimatorParameter> currentParameters;

		private int _currentAnimationHash;

		private int _nextAnimationHash;

		[Tooltip("Only applicable if there's on end actions.")]
		public bool waitForAnimationEnd;

		public Animator Animator => animator;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (animator != null)
			{
				if (layers == null)
				{
					Debug.LogWarning("Animation Interaction: No layers found!", this);
					return;
				}
				if (mode == AnimationInteractionMode.Play)
				{
					animator.Play(layers[layerIndex].hashes[stateIndex], layerIndex);
				}
				else
				{
					SetParameters();
				}
			}
			else
			{
				Debug.LogWarning("Animation Interaction: No animator assigned!", this);
			}
			if (mode == AnimationInteractionMode.Play && waitForAnimationEnd)
			{
				StartCoroutine(WaitForAnimationEnd());
			}
			else
			{
				OnEnd();
			}
		}

		private void SetParameters()
		{
			if (currentParameters.Count == 0)
			{
				return;
			}
			foreach (AnimatorParameter currentParameter in currentParameters)
			{
				switch (currentParameter.type)
				{
				case AnimatorControllerParameterType.Int:
					animator.SetInteger(currentParameter.hash, currentParameter.intValue);
					break;
				case AnimatorControllerParameterType.Float:
					animator.SetFloat(currentParameter.hash, currentParameter.floatValue);
					break;
				case AnimatorControllerParameterType.Bool:
					animator.SetBool(currentParameter.hash, currentParameter.boolValue);
					break;
				case AnimatorControllerParameterType.Trigger:
					animator.SetTrigger(currentParameter.hash);
					break;
				}
			}
		}

		private IEnumerator WaitForAnimationEnd()
		{
			int targetAnimationHash = Animator.StringToHash(layers[layerIndex].states[stateIndex]);
			yield return new WaitUntil(() => targetAnimationHash == animator.GetCurrentAnimatorStateInfo(layerIndex).shortNameHash);
			float length = animator.GetCurrentAnimatorStateInfo(layerIndex).length;
			yield return new WaitForSeconds(length);
			OnEnd();
		}
	}
}
