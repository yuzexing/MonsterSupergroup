using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon Card Template Visual Data LUT", menuName = "HellMaiden/Data/Cards/Visuals/Weapon Card Template Visual Data LUT")]
public class WeaponTemplateVisualDataLUT : ScriptableObject
{
	[SerializeField]
	protected List<WeaponTemplateVisualData> values;

	private Dictionary<PoetPoolID, WeaponTemplateVisualData> _lut;

	public List<WeaponTemplateVisualData> Values => values;

	public Dictionary<PoetPoolID, WeaponTemplateVisualData> LUT
	{
		get
		{
			if (_lut != null)
			{
				return _lut;
			}
			return CreateDictionary();
		}
	}

	private Dictionary<PoetPoolID, WeaponTemplateVisualData> CreateDictionary()
	{
		_lut = new Dictionary<PoetPoolID, WeaponTemplateVisualData>();
		for (int i = 0; i < Enum.GetNames(typeof(PoetPoolID)).Length; i++)
		{
			_lut.Add((PoetPoolID)i, Values[i]);
		}
		return _lut;
	}

	public List<WeaponTemplateVisualData> GetValues()
	{
		if (values == null)
		{
			values = new List<WeaponTemplateVisualData>();
		}
		return values;
	}

	public void SaveValues(List<WeaponTemplateVisualData> values)
	{
		this.values = values;
	}
}
