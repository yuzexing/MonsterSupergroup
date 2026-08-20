using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class Cell
	{
		public int x;

		public int y;

		public Vector2 worldPosition { get; set; }

		public Tile tile { get; set; }

		public bool locked { get; set; }

		public Cell(Vector2 _worldPosition, int x, int y)
		{
			worldPosition = _worldPosition;
			this.x = x;
			this.y = y;
		}
	}
}
