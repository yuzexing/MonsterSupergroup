using System;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	[Serializable]
	public class Tile
	{
		public TileGenerator prefab;

		public PropSpawner setPiece;

		[ConditionalHide("specificPosition", false)]
		public float weight;

		[ConditionalHide("specificPosition", false)]
		public int minAmount;

		[ConditionalHide("specificPosition", false)]
		public int maxAmount;

		public bool specificPosition;

		[ConditionalHide("specificPosition", true)]
		public Vector2Int position;

		public override bool Equals(object obj)
		{
			if ((obj as Tile).prefab == prefab && (obj as Tile).setPiece == setPiece)
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return base.GetHashCode();
		}

		public override string ToString()
		{
			return base.ToString();
		}
	}
}
