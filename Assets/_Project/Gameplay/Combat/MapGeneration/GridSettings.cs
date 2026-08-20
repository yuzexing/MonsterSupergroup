using System.Collections.Generic;
using AstralShift.Helpers;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.MapGeneration
{
	public class GridSettings : MonoBehaviour
	{
		public float squareSize = 0.5f;

		public LayerMask collisionLayerMask;

		private Bounds bounds;

		private int[,] grid;

		private int numSquaresX;

		private int numSquaresY;

		[Tooltip("Draws grid gizmos.")]
		[SerializeField]
		private bool drawGrid;

		[Tooltip("If false, objects will spawn all in a row without random placement. Set false just for debug.")]
		[SerializeField]
		private bool randomGeneration = true;

		public UniTask CreateGrid(BoxCollider2D area)
		{
			Debug.Log("Grid: Creating...");
			Physics2D.SyncTransforms();
			bounds = area.bounds;
			numSquaresX = Mathf.FloorToInt(bounds.size.x / squareSize);
			numSquaresY = Mathf.FloorToInt(bounds.size.y / squareSize);
			grid = new int[numSquaresX, numSquaresY];
			for (int i = 0; i < numSquaresX; i++)
			{
				for (int j = 0; j < numSquaresY; j++)
				{
					float x = bounds.min.x + ((float)i + 0.5f) * squareSize;
					float y = bounds.min.y + ((float)j + 0.5f) * squareSize;
					Vector2 squareCenter = new Vector2(x, y);
					if ((int)collisionLayerMask != 0)
					{
						grid[i, j] = (CheckCollision(squareCenter) ? 1 : 0);
						_ = grid[i, j];
					}
				}
			}
			return UniTask.CompletedTask;
		}

		private bool CheckCollision(Vector2 squareCenter)
		{
			return Physics2D.OverlapBox(squareCenter, new Vector2(squareSize, squareSize), 0f, collisionLayerMask) != null;
		}

		private void DrawGrid(Vector2 center, bool isTouching)
		{
			Vector2 vector = center + new Vector2((0f - squareSize) / 2f, squareSize / 2f);
			Vector2 vector2 = center + new Vector2(squareSize / 2f, squareSize / 2f);
			Vector2 vector3 = center + new Vector2((0f - squareSize) / 2f, (0f - squareSize) / 2f);
			Vector2 vector4 = center + new Vector2(squareSize / 2f, (0f - squareSize) / 2f);
			Color color = (isTouching ? Color.red : Color.green);
			Debug.DrawLine(vector, vector2, color, 10f);
			Debug.DrawLine(vector2, vector4, color, 10f);
			Debug.DrawLine(vector4, vector3, color, 10f);
			Debug.DrawLine(vector3, vector, color, 10f);
		}

		public Vector2 GetGridPosition(Prop prop, float boundsWidth, float boundsHeight)
		{
			int num = 10;
			Vector2 vector = Vector2.zero;
			int num2 = Mathf.RoundToInt(boundsWidth / squareSize);
			int num3 = Mathf.RoundToInt(boundsHeight / squareSize);
			if (num2 <= 0 || num3 <= 0)
			{
				Debug.LogError("Prop collider bounds are smaller than grid's squareSize");
				return -Vector2.one;
			}
			if (randomGeneration)
			{
				Vector2Int[] item = new Vector2Int[2]
				{
					Vector2Int.zero,
					new Vector2Int(numSquaresX / 2, numSquaresY / 2)
				};
				Vector2Int[] item2 = new Vector2Int[2]
				{
					new Vector2Int(0, numSquaresY / 2),
					new Vector2Int(numSquaresX / 2, numSquaresY - num3)
				};
				Vector2Int[] item3 = new Vector2Int[2]
				{
					new Vector2Int(numSquaresX / 2, 0),
					new Vector2Int(numSquaresX - num2, numSquaresY / 2)
				};
				Vector2Int[] item4 = new Vector2Int[2]
				{
					new Vector2Int(numSquaresX / 2, numSquaresY / 2),
					new Vector2Int(numSquaresX - num2, numSquaresY - num3)
				};
				do
				{
					if (num2 > numSquaresX / 2 || num3 > numSquaresY / 2)
					{
						vector = FindUnoccupiedSpace(RandomHelpers.GetRandomInt(0, numSquaresX - num2), RandomHelpers.GetRandomInt(0, numSquaresY - num3), num2, num3, prop.excludeLayers);
					}
					else
					{
						List<Vector2Int[]> list = new List<Vector2Int[]> { item, item2, item3, item4 };
						for (int i = 0; i < 4; i++)
						{
							int randomInt = RandomHelpers.GetRandomInt(0, list.Count);
							int randomInt2 = RandomHelpers.GetRandomInt(list[randomInt][0].x, list[randomInt][1].x);
							int randomInt3 = RandomHelpers.GetRandomInt(list[randomInt][0].y, list[randomInt][1].y);
							vector = FindUnoccupiedSpace(randomInt2, randomInt3, num2, num3, prop.excludeLayers);
							if (vector != -Vector2.one)
							{
								break;
							}
							list.Remove(list[randomInt]);
						}
					}
					num--;
				}
				while (vector == -Vector2.one && num > 0);
			}
			else
			{
				vector = FindUnoccupiedSpace(0, 0, num2, num3, prop.excludeLayers);
			}
			if (vector.x >= 0f && vector.y >= 0f)
			{
				int num4 = (int)(vector.x / squareSize);
				int num5 = (int)(vector.y / squareSize);
				for (int j = 0; j < num2; j++)
				{
					for (int k = 0; k < num3; k++)
					{
						MarkCellAsOccupied(num4 + j, num5 + k);
					}
				}
				return vector;
			}
			Debug.LogWarning("PropSpawner: No unoccupied space large enough for asset: " + prop?.ToString() + "found in " + num + " attempts!");
			return -Vector2.one;
		}

		private bool IsSpaceAvailable(int startX, int startY, int colliderWidth, int colliderHeight, bool excludeLayers)
		{
			int num = 2;
			if (excludeLayers)
			{
				num = 1;
			}
			for (int i = startX; i < startX + colliderWidth; i++)
			{
				for (int j = startY; j < startY + colliderHeight; j++)
				{
					if (i >= numSquaresX || j >= numSquaresY || grid[i, j] >= num)
					{
						return false;
					}
				}
			}
			return true;
		}

		private Vector2 FindUnoccupiedSpace(int startX, int startY, int colliderWidth, int colliderHeight, bool excludeLayers)
		{
			for (int i = startX; i <= numSquaresX - colliderWidth; i++)
			{
				for (int j = startY; j <= numSquaresY - colliderHeight; j++)
				{
					if (IsSpaceAvailable(i, j, colliderWidth, colliderHeight, excludeLayers))
					{
						return new Vector2((float)i * squareSize, (float)j * squareSize);
					}
				}
			}
			return new Vector2(-1f, -1f);
		}

		private void MarkCellAsOccupied(int x, int y)
		{
			if (x >= 0 && x < numSquaresX && y >= 0 && y < numSquaresY)
			{
				grid[x, y] = 2;
			}
		}
	}
}
