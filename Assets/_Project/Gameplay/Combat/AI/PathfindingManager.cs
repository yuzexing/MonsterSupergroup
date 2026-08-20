using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.MapGeneration;
using Pathfinding;
using UnityEngine;

namespace AstralShift.HellMaiden.AI
{
	public class PathfindingManager : MonoBehaviour
	{
		private enum GridGraphSide
		{
			Top = 0,
			Bottom = 1,
			Right = 2,
			Left = 3
		}

		public AstarPath navmesh;

		public MapGenerator mapGenerator;

		private List<GridGraph> _graphs;

		private TileGenerator[] _tiles;

		private void Awake()
		{
			QuestMapGenerator.OnGenerateEnd = (Action)Delegate.Combine(QuestMapGenerator.OnGenerateEnd, new Action(GenerateNavMesh));
			MapGenerator.OnTilesMoved += UpdateNavMesh;
			QuestMapGenerator.OnTilesSwapped = (Action<TileGenerator, TileGenerator>)Delegate.Combine(QuestMapGenerator.OnTilesSwapped, new Action<TileGenerator, TileGenerator>(ReplaceNavMeshNodes));
			SpecialTilesGenerator.OnTilesSwapped = (Action<TileGenerator, TileGenerator>)Delegate.Combine(SpecialTilesGenerator.OnTilesSwapped, new Action<TileGenerator, TileGenerator>(ReplaceNavMeshNodes));
		}

		private void OnDestroy()
		{
			QuestMapGenerator.OnGenerateEnd = (Action)Delegate.Remove(QuestMapGenerator.OnGenerateEnd, new Action(GenerateNavMesh));
			MapGenerator.OnTilesMoved -= UpdateNavMesh;
			QuestMapGenerator.OnTilesSwapped = (Action<TileGenerator, TileGenerator>)Delegate.Remove(QuestMapGenerator.OnTilesSwapped, new Action<TileGenerator, TileGenerator>(ReplaceNavMeshNodes));
			SpecialTilesGenerator.OnTilesSwapped = (Action<TileGenerator, TileGenerator>)Delegate.Remove(SpecialTilesGenerator.OnTilesSwapped, new Action<TileGenerator, TileGenerator>(ReplaceNavMeshNodes));
		}

		protected void GenerateNavMesh()
		{
			AstarPath.active = navmesh;
			TileGenerator[] array = UnityEngine.Object.FindObjectsByType<TileGenerator>(FindObjectsSortMode.InstanceID);
			foreach (TileGenerator tileGenerator in array)
			{
				GridGraph gridGraph = AstarPath.active.data.DuplicateGraph(AstarPath.active.data.gridGraph) as GridGraph;
				gridGraph.center = tileGenerator.GetCenter();
				gridGraph.name = tileGenerator.name + " " + tileGenerator.GetInstanceID();
				tileGenerator.GridGraph = gridGraph;
			}
			AstarPath.active.data.RemoveGraph(AstarPath.active.data.gridGraph);
			AstarPath.active.Scan();
		}

		protected void ReplaceNavMeshNodes(TileGenerator tileToAdd, TileGenerator tileToRemove)
		{
			if (!(tileToAdd == null) && !(tileToRemove == null))
			{
				AstarPath.active.AddWorkItem((Action)delegate
				{
					tileToAdd.GridGraph.RelocateNodes(tileToAdd.GetCenter(), Quaternion.Euler(90f, 0f, 0f), 1f);
					tileToRemove.GridGraph.RelocateNodes(Vector2.one * 1000f, Quaternion.Euler(90f, 0f, 0f), 1f);
				});
			}
		}

		protected void UpdateNavMesh(TileGenerator[] allTiles, MapGenerator mapGenerator)
		{
			AstarPath.active.AddWorkItem((Action)delegate
			{
				TileGenerator[] array = allTiles;
				foreach (TileGenerator tileGenerator in array)
				{
					if (tileGenerator.gameObject.activeSelf)
					{
						tileGenerator.GridGraph.RelocateNodes(tileGenerator.GetCenter(), Quaternion.Euler(90f, 0f, 0f), 1f);
					}
				}
			});
		}

		private TileGenerator[] TransposeGridArray(TileGenerator[] tiles, int originalRows, int originalColumns)
		{
			TileGenerator[] array = new TileGenerator[tiles.Length];
			for (int i = 0; i < originalRows; i++)
			{
				for (int j = 0; j < originalColumns; j++)
				{
					int num = i * originalColumns + j;
					int num2 = j * originalRows + i;
					array[num2] = tiles[num];
				}
			}
			return array;
		}

		private void ConnectGraphs(int columns, int rows)
		{
			for (int i = 0; i < _tiles.Length; i++)
			{
				int num = i % columns;
				int num2 = i / columns;
				GridGraph gridGraph = _tiles[i].GridGraph;
				if (num2 < rows - 1)
				{
					int num3 = i + columns;
					ConnectEdges(edge2: GetEdgeNodes(_tiles[num3].GridGraph, GridGraphSide.Bottom), edge1: GetEdgeNodes(gridGraph, GridGraphSide.Top));
				}
				if (num2 > 0)
				{
					int num4 = i - columns;
					ConnectEdges(edge2: GetEdgeNodes(_tiles[num4].GridGraph, GridGraphSide.Top), edge1: GetEdgeNodes(gridGraph, GridGraphSide.Bottom));
				}
				if (num > 0)
				{
					int num5 = i - 1;
					ConnectEdges(edge2: GetEdgeNodes(_tiles[num5].GridGraph, GridGraphSide.Right), edge1: GetEdgeNodes(gridGraph, GridGraphSide.Left));
				}
				if (num < columns - 1)
				{
					int num6 = i + 1;
					GridGraph gridGraph2 = _tiles[num6].GridGraph;
					ConnectEdges(GetEdgeNodes(gridGraph, GridGraphSide.Right), GetEdgeNodes(gridGraph2, GridGraphSide.Left));
				}
			}
		}

		private GraphNode[] GetEdgeNodes(GridGraph graph, GridGraphSide side)
		{
			List<GraphNode> list = new List<GraphNode>();
			switch (side)
			{
			case GridGraphSide.Top:
			{
				for (int l = 0; l < graph.Width; l++)
				{
					list.Add(graph.nodes[ToIndex(graph, l, graph.Depth - 1)]);
				}
				break;
			}
			case GridGraphSide.Bottom:
			{
				for (int j = 0; j < graph.Width; j++)
				{
					list.Add(graph.nodes[ToIndex(graph, j, 0)]);
				}
				break;
			}
			case GridGraphSide.Left:
			{
				for (int k = 0; k < graph.Depth; k++)
				{
					list.Add(graph.nodes[ToIndex(graph, 0, k)]);
				}
				break;
			}
			case GridGraphSide.Right:
			{
				for (int i = 0; i < graph.Depth; i++)
				{
					list.Add(graph.nodes[ToIndex(graph, graph.Width - 1, i)]);
				}
				break;
			}
			}
			return list.ToArray();
		}

		private void ConnectEdges(GraphNode[] edge1, GraphNode[] edge2)
		{
			for (int i = 0; i < edge1.Length; i++)
			{
				GraphNode graphNode = edge1[i];
				GraphNode graphNode2 = edge2[i];
				if (graphNode != null && graphNode2 != null && graphNode.Walkable && graphNode2.Walkable)
				{
					GraphNode.Connect(graphNode, graphNode2, (uint)(graphNode2.position - graphNode.position).costMagnitude);
				}
				if (i > 0)
				{
					GraphNode graphNode3 = edge2[i - 1];
					if (graphNode != null && graphNode3 != null && graphNode.Walkable && graphNode3.Walkable)
					{
						GraphNode.Connect(graphNode, graphNode3, (uint)(graphNode3.position - graphNode.position).costMagnitude);
					}
				}
				if (i < edge1.Length - 1)
				{
					GraphNode graphNode4 = edge2[i + 1];
					if (graphNode != null && graphNode4 != null && graphNode.Walkable && graphNode4.Walkable)
					{
						GraphNode.Connect(graphNode, graphNode4, (uint)(graphNode4.position - graphNode.position).costMagnitude);
					}
				}
			}
		}

		public int ToIndex(GridGraph graph, int x, int y)
		{
			return y * graph.Width + x;
		}
	}
}
