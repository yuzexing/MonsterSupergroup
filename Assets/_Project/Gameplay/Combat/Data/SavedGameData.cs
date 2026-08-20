using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PixelCrushers
{
	[Serializable]
	public class SavedGameData : ISerializationCallbackReceiver
	{
		[Serializable]
		public class SaveRecord
		{
			public string key;

			public int sceneIndex;

			public string data;

			public SaveRecord()
			{
			}

			public SaveRecord(string key, int sceneIndex, string data)
			{
				this.key = key;
				this.sceneIndex = sceneIndex;
				this.data = data;
			}
		}

		[SerializeField]
		private int m_version;

		[SerializeField]
		private string m_sceneName;

		private Dictionary<string, SaveRecord> m_dict = new Dictionary<string, SaveRecord>();

		[SerializeField]
		private List<SaveRecord> m_list = new List<SaveRecord>();

		public int version
		{
			get
			{
				return m_version;
			}
			set
			{
				m_version = value;
			}
		}

		public string sceneName
		{
			get
			{
				return m_sceneName;
			}
			set
			{
				m_sceneName = value;
			}
		}

		public Dictionary<string, SaveRecord> Dict => m_dict;

		public void OnBeforeSerialize()
		{
			m_list.Clear();
			foreach (KeyValuePair<string, SaveRecord> item in m_dict)
			{
				m_list.Add(item.Value);
			}
		}

		public void OnAfterDeserialize()
		{
			m_dict = new Dictionary<string, SaveRecord>();
			for (int i = 0; i < m_list.Count; i++)
			{
				if (m_list[i] != null)
				{
					m_dict.Add(m_list[i].key, m_list[i]);
				}
			}
		}

		public SaveRecord GetDataInfo(string key)
		{
			if (!m_dict.ContainsKey(key))
			{
				return null;
			}
			return m_dict[key];
		}

		public string GetData(string key)
		{
			if (!m_dict.ContainsKey(key))
			{
				return null;
			}
			return m_dict[key].data;
		}

		public void SetData(string key, int sceneIndex, string data)
		{
			if (m_dict.ContainsKey(key))
			{
				m_dict[key].sceneIndex = sceneIndex;
				m_dict[key].data = data;
			}
			else
			{
				m_dict.Add(key, new SaveRecord(key, sceneIndex, data));
			}
		}

		public void DeleteData(string key)
		{
			if (m_dict.ContainsKey(key))
			{
				m_dict.Remove(key);
			}
		}

		public void DeleteObsoleteSaveData(int currentSceneIndex)
		{
			m_dict = m_dict.Where((KeyValuePair<string, SaveRecord> element) => element.Value.sceneIndex == currentSceneIndex || element.Value.sceneIndex == -1).ToDictionary((KeyValuePair<string, SaveRecord> element) => element.Key, (KeyValuePair<string, SaveRecord> element) => element.Value);
		}
	}
}
