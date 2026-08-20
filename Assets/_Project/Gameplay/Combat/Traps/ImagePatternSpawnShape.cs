using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat.Spawners.SpawnShapes;
using UnityEngine;

public class ImagePatternSpawnShape : SpawnShape
{
	public Texture2D pattern;

	[HideInInspector]
	public List<Vector2> copiedPositions = new List<Vector2>();

	public int patternSize = 10;

	[Tooltip("The step determines the amount of pixels that the system will check. The higher this value is, the lower number of pixels it checks, so it\ufffds more performant.")]
	[Range(1f, 80f)]
	public int xStep;

	[Tooltip("The step determines the amount of pixels that the system will check. The higher this value is, the lower number of pixels it checks, so it\ufffds more performant.")]
	[Range(1f, 80f)]
	public int yStep;

	private List<Vector2> patternPositions = new List<Vector2>();

	private int w;

	private int h;

	private Color lastColorChecked = Color.black;

	private int spawnCounter;

	private void Start()
	{
		GrabPositionsFromPattern();
	}

	public void GrabPositionsFromPattern()
	{
		patternPositions = new List<Vector2>();
		w = pattern.width;
		h = pattern.height;
		lastColorChecked = Color.white;
		for (int i = 0; i < h; i += yStep)
		{
			for (int j = 0; j < w; j += xStep)
			{
				if (MatchRequirements(pattern.GetPixel(j, i)))
				{
					Vector2 item = new Vector2((float)j - (float)w / 2f, (float)i - (float)h / 2f) / w * patternSize;
					patternPositions.Add(item);
				}
			}
		}
		Debug.Log("Position Amount = " + patternPositions.Count);
	}

	private bool MatchRequirements(Color color)
	{
		if (IsWhite(lastColorChecked))
		{
			lastColorChecked = color;
			return false;
		}
		lastColorChecked = color;
		return true;
	}

	private bool IsWhite(Color color)
	{
		Color color2 = new Color(0.7f, 0.7f, 0.7f);
		if (color.r <= color2.r && color.g <= color2.g && color.b <= color2.b)
		{
			return false;
		}
		return true;
	}

	public ImagePatternSpawnShape(Color lastColorChecked)
	{
		this.lastColorChecked = lastColorChecked;
	}

	public override Vector2 GetEnemyPosition(Vector2 center, int count, int idx)
	{
		if (idx == 0)
		{
			copiedPositions = new List<Vector2>();
			for (int i = 0; i < patternPositions.Count; i++)
			{
				copiedPositions.Add(patternPositions[i]);
			}
		}
		Vector2 vector = copiedPositions.FirstOrDefault();
		copiedPositions.Remove(vector);
		return center + vector;
	}

	public override bool ValidVertexCount(int count)
	{
		return count == patternPositions.Count;
	}
}
