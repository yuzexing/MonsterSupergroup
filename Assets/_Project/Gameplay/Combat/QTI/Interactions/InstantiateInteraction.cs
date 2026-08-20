using System;
using System.Collections.Generic;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/InstantiateInteraction")]
	public class InstantiateInteraction : Interaction
	{
		[Serializable]
		public class SpawnOptions
		{
			public GameObject ToSpawn;

			public InstantiateInteractionTransformMode transformMode;

			public Vector3 spawnPosition;

			public Vector3 spawnRotation;

			public Vector3 spawnScale;

			public Transform spawnTransform;

			public InstantiateInteractionParentMode parentMode;

			public Transform spawnParent;

			public SpawnOptions()
			{
				Reset();
			}

			public void Reset()
			{
				ToSpawn = null;
				transformMode = InstantiateInteractionTransformMode.Original;
				spawnPosition = default(Vector3);
				spawnRotation = default(Vector3);
				spawnScale = Vector3.one;
				spawnTransform = null;
				parentMode = InstantiateInteractionParentMode.Root;
				spawnParent = null;
			}
		}

		public enum InstantiateInteractionTransformMode
		{
			Original = 0,
			Transform = 1,
			Manual = 2
		}

		public enum InstantiateInteractionParentMode
		{
			Root = 0,
			Transform = 1
		}

		public List<SpawnOptions> toInstantiate;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			for (int i = 0; i < toInstantiate.Count; i++)
			{
				SpawnOptions spawnOptions = toInstantiate[i];
				GameObject gameObject = null;
				switch (spawnOptions.transformMode)
				{
				case InstantiateInteractionTransformMode.Original:
				{
					Transform transform = spawnOptions.ToSpawn.transform;
					gameObject = ((spawnOptions.parentMode != InstantiateInteractionParentMode.Transform) ? UnityEngine.Object.Instantiate(spawnOptions.ToSpawn, transform.position, transform.rotation) : UnityEngine.Object.Instantiate(spawnOptions.ToSpawn, transform.position, transform.rotation, spawnOptions.spawnParent));
					break;
				}
				case InstantiateInteractionTransformMode.Transform:
				{
					Transform spawnTransform = spawnOptions.spawnTransform;
					gameObject = ((spawnOptions.parentMode != InstantiateInteractionParentMode.Transform) ? UnityEngine.Object.Instantiate(spawnOptions.ToSpawn, spawnTransform.position, spawnTransform.rotation) : UnityEngine.Object.Instantiate(spawnOptions.ToSpawn, spawnTransform.position + spawnOptions.spawnPosition, Quaternion.Euler(spawnTransform.localRotation.eulerAngles + spawnOptions.spawnRotation), spawnOptions.spawnParent));
					gameObject.transform.localScale = new Vector3(spawnOptions.spawnScale.x * spawnTransform.localScale.x, spawnOptions.spawnScale.y * spawnTransform.localScale.y, spawnOptions.spawnScale.z * spawnTransform.localScale.z);
					break;
				}
				case InstantiateInteractionTransformMode.Manual:
					gameObject = ((spawnOptions.parentMode != InstantiateInteractionParentMode.Transform) ? UnityEngine.Object.Instantiate(spawnOptions.ToSpawn, spawnOptions.spawnPosition, Quaternion.Euler(spawnOptions.spawnRotation)) : UnityEngine.Object.Instantiate(spawnOptions.ToSpawn, spawnOptions.spawnPosition, Quaternion.Euler(spawnOptions.spawnRotation), spawnOptions.spawnParent));
					gameObject.transform.localScale = spawnOptions.spawnScale;
					break;
				}
				gameObject.name = spawnOptions.ToSpawn.name;
			}
			OnEnd();
		}
	}
}
