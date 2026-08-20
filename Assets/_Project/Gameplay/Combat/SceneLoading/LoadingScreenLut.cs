using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Scenes;
using UnityEngine;

namespace AstralShift.SceneLoading
{
	[CreateAssetMenu(fileName = "LoadingScreenLut", menuName = "AstralShift/Loading Screen LUT")]
	public class LoadingScreenLut : ScriptableObject
	{
		[Serializable]
		public struct Entry
		{
			public SceneEnum scene;

			public GameObject backgroundPrefab;

			[Header("Scene Specific Tips & Myths")]
			public List<string> tipsTxtsKeys;

			public List<string> mythTxtsKeys;
		}

		[SerializeField]
		private List<Entry> entries;

		[SerializeField]
		private GameObject fallback;

		public GameObject Get(SceneEnum scene)
		{
			foreach (Entry entry in entries)
			{
				if (entry.scene == scene)
				{
					return entry.backgroundPrefab;
				}
			}
			return fallback;
		}

		public List<string> GetTips(SceneEnum scene)
		{
			foreach (Entry entry in entries)
			{
				if (entry.scene == scene)
				{
					return entry.tipsTxtsKeys;
				}
			}
			return null;
		}

		public List<string> GetMyths(SceneEnum scene)
		{
			foreach (Entry entry in entries)
			{
				if (entry.scene == scene)
				{
					return entry.mythTxtsKeys;
				}
			}
			return null;
		}
	}
}
