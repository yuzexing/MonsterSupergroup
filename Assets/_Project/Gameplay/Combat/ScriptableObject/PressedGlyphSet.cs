using System;
using System.Collections.Generic;
using Rewired.Glyphs;
using UnityEngine;

[CreateAssetMenu(fileName = "PressedGlyphSet", menuName = "Scriptable Objects/PressedGlyphSet")]
public class PressedGlyphSet : GlyphSet
{
	[Serializable]
	public class Entry : EntryBase<Sprite>
	{
		[SerializeField]
		private Sprite pressedSprite;

		public Sprite GetPressedSprite()
		{
			return pressedSprite;
		}
	}

	[Tooltip("The list of glyphs.")]
	[SerializeField]
	private List<Entry> _glyphs;

	public List<Entry> glyphs
	{
		get
		{
			return _glyphs;
		}
		set
		{
			_glyphs = value;
		}
	}

	public override int glyphCount
	{
		get
		{
			if (_glyphs == null)
			{
				return 0;
			}
			return _glyphs.Count;
		}
	}

	public override EntryBase GetEntry(int index)
	{
		if (_glyphs == null)
		{
			return null;
		}
		if ((uint)index >= (uint)_glyphs.Count)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		return _glyphs[index];
	}
}
