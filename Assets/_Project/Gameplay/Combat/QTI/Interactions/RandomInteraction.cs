using System;
using System.Reflection;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	public class RandomInteraction : Interaction
	{
		public MonoBehaviour targetScript;

		[HideInInspector]
		public string selectedVariable;

		public Vector2 randomRange;

		public Vector2Int randomRangeInt;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			RandomizeSelectedVariable();
			OnEnd();
		}

		private void RandomizeSelectedVariable()
		{
			if (!(targetScript != null) || string.IsNullOrEmpty(selectedVariable))
			{
				return;
			}
			Type type = targetScript.GetType();
			FieldInfo field = type.GetField(selectedVariable, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				object obj = new UnityEngine.Object();
				if (field.FieldType == typeof(float))
				{
					obj = GetRandomFloat(randomRange);
				}
				else if (field.FieldType == typeof(int))
				{
					obj = GetRandomInt(randomRangeInt);
				}
				else if (field.FieldType == typeof(double))
				{
					obj = GetRandomFloat(randomRange);
				}
				else if (field.FieldType == typeof(Vector2))
				{
					obj = GetRandomFloat(randomRange);
					obj = Vector2.one * (float)obj;
				}
				else if (field.FieldType == typeof(Vector3))
				{
					obj = GetRandomFloat(randomRange);
					obj = Vector3.one * (float)obj;
				}
				else if (field.FieldType == typeof(Vector2Int))
				{
					obj = GetRandomInt(randomRangeInt);
					obj = Vector2.one * (int)obj;
				}
				else if (field.FieldType == typeof(Vector3Int))
				{
					obj = GetRandomInt(randomRangeInt);
					obj = Vector3.one * (int)obj;
				}
				else if (field.FieldType == typeof(bool))
				{
					obj = UnityEngine.Random.Range(0, 2);
				}
				if (obj != null)
				{
					field.SetValue(targetScript, Convert.ChangeType(obj, field.FieldType));
				}
				return;
			}
			PropertyInfo property = type.GetProperty(selectedVariable, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite)
			{
				object obj2 = new UnityEngine.Object();
				if (property.PropertyType == typeof(float))
				{
					obj2 = GetRandomFloat(randomRange);
				}
				else if (property.PropertyType == typeof(int))
				{
					obj2 = GetRandomInt(randomRangeInt);
				}
				else if (property.PropertyType == typeof(double))
				{
					obj2 = GetRandomFloat(randomRange);
				}
				else if (property.PropertyType == typeof(Vector2))
				{
					obj2 = GetRandomFloat(randomRange);
					obj2 = Vector2.one * (float)obj2;
				}
				else if (property.PropertyType == typeof(Vector3))
				{
					obj2 = GetRandomFloat(randomRange);
					obj2 = Vector3.one * (float)obj2;
				}
				else if (property.PropertyType == typeof(Vector2Int))
				{
					obj2 = GetRandomInt(randomRangeInt);
					obj2 = Vector2.one * (int)obj2;
				}
				else if (property.PropertyType == typeof(Vector3Int))
				{
					obj2 = GetRandomInt(randomRangeInt);
					obj2 = Vector3.one * (int)obj2;
				}
				else if (property.PropertyType == typeof(bool))
				{
					obj2 = UnityEngine.Random.Range(0, 2);
				}
				if (obj2 != null)
				{
					property.SetValue(targetScript, Convert.ChangeType(obj2, property.PropertyType));
				}
			}
		}

		private float GetRandomFloat(Vector2 bounds)
		{
			return UnityEngine.Random.Range(bounds.x, bounds.y);
		}

		private int GetRandomInt(Vector2Int bounds)
		{
			return UnityEngine.Random.Range(bounds.x, bounds.y);
		}
	}
}
