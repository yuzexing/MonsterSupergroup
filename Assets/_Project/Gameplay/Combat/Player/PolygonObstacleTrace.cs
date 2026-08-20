using System;
using System.Collections.Generic;
using UnityEngine;

public class PolygonObstacleTrace : MonoBehaviour
{
	private readonly List<PolygonCollider2D> polygons = new List<PolygonCollider2D>();

	private readonly List<(Vector2 A, Vector2 B)> edges = new List<(Vector2, Vector2)>();

	private void Awake()
	{
		CachePolygonColliders();
		CachePolygonEdges();
	}

	private void CachePolygonColliders()
	{
		polygons.Clear();
		GetComponents(polygons);
		if (polygons.Count == 0)
		{
			Debug.LogWarning(base.name + ": No Polygon Colliders found. Obstacle Trace will not work properly.");
		}
	}

	private void CachePolygonEdges()
	{
		edges.Clear();
		foreach (PolygonCollider2D polygon in polygons)
		{
			if (polygon.GetTotalPointCount() == 0)
			{
				continue;
			}
			for (int i = 0; i < polygon.pathCount; i++)
			{
				Vector2[] path = polygon.GetPath(i);
				for (int j = 0; j < path.Length; j++)
				{
					path[j] = polygon.transform.TransformPoint(path[j]);
				}
				for (int k = 0; k < path.Length; k++)
				{
					Vector2 item = path[k];
					Vector2 item2 = path[(k + 1) % path.Length];
					edges.Add((item, item2));
				}
			}
		}
	}

	public bool TryGetEntryExit(Vector2 origin, Vector2 direction, float distance, out Vector2 entry, out Vector2 exit)
	{
		entry = Vector2.zero;
		exit = Vector2.zero;
		Vector2[] intersections = GetIntersections(origin, direction, distance);
		if (intersections.Length < 2)
		{
			return false;
		}
		Array.Sort(intersections, (Vector2 a, Vector2 b) => Vector2.Distance(origin, a).CompareTo(Vector2.Distance(origin, b)));
		entry = intersections[0];
		exit = intersections[1];
		return true;
	}

	private Vector2[] GetIntersections(Vector2 origin, Vector2 direction, float distance)
	{
		List<Vector2> list = new List<Vector2>();
		Vector2 p = origin + direction.normalized * distance;
		foreach (var edge in edges)
		{
			if (LineSegmentIntersection(origin, p, edge.A, edge.B, out var intersection))
			{
				list.Add(intersection);
			}
		}
		return list.ToArray();
	}

	private bool LineSegmentIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 intersection)
	{
		intersection = Vector2.zero;
		Vector2 vector = p2 - p1;
		Vector2 vector2 = p4 - p3;
		Vector2 vector3 = p3 - p1;
		float num = vector.x * vector2.y - vector.y * vector2.x;
		if (Mathf.Abs(num) < Mathf.Epsilon)
		{
			return false;
		}
		float num2 = (vector3.x * vector2.y - vector3.y * vector2.x) / num;
		float num3 = (vector3.x * vector.y - vector3.y * vector.x) / num;
		if (num2 < 0f || num2 > 1f || num3 < 0f || num3 > 1f)
		{
			return false;
		}
		intersection = p1 + num2 * vector;
		return true;
	}
}
