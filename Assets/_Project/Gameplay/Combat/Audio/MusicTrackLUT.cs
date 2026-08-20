using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Audio
{
	[CreateAssetMenu(fileName = "MusicTrackLUT", menuName = "HellMaiden/Audio/Music Track LUT")]
	public class MusicTrackLUT : ScriptableObject
	{
		[Serializable]
		public struct Entry
		{
			public MusicTrack track;

			public EventReference eventReference;
		}

		[SerializeField]
		private List<Entry> entries = new List<Entry>();

		private Dictionary<MusicTrack, EventReference> _lut;

		public bool TryGetEvent(MusicTrack track, out EventReference eventReference)
		{
			if (_lut == null)
			{
				BuildLUT();
			}
			return _lut.TryGetValue(track, out eventReference);
		}

		private void BuildLUT()
		{
			_lut = new Dictionary<MusicTrack, EventReference>();
			for (int i = 0; i < entries.Count; i++)
			{
				if (!_lut.ContainsKey(entries[i].track))
				{
					_lut.Add(entries[i].track, entries[i].eventReference);
				}
				else
				{
					Debug.LogWarning($"MusicTrackLUT: duplicated entry for {entries[i].track}", this);
				}
			}
		}

		private void OnValidate()
		{
			_lut = null;
		}
	}
}
