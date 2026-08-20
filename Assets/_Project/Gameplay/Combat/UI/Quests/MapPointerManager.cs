using System.Collections.Generic;
using Animancer;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Quests
{
	public class MapPointerManager : MonoBehaviour
	{
		public static MapPointerManager Instance;

		[SerializeField]
		private AnimancerComponent minimapAnimancer;

		[SerializeField]
		private ClipTransition radarAnimation;

		public GameObject arrowsParent;

		public QuestArrowPointer2D PointerPrefab;

		private List<QuestArrowPointer2D> _freePointers;

		private List<QuestArrowPointer2D> _inUsePointers;

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				Object.Destroy(this);
			}
		}

		private void Start()
		{
			_freePointers = new List<QuestArrowPointer2D>();
			_inUsePointers = new List<QuestArrowPointer2D>();
		}

		public QuestArrowPointer2D GetArrowPointer()
		{
			QuestArrowPointer2D questArrowPointer2D = null;
			if (_freePointers.Count == 0)
			{
				questArrowPointer2D = Object.Instantiate(PointerPrefab, arrowsParent.transform);
				questArrowPointer2D.Init();
			}
			else
			{
				questArrowPointer2D = _freePointers[0];
				_freePointers.RemoveAt(0);
			}
			minimapAnimancer.Stop();
			minimapAnimancer.Play(radarAnimation);
			_inUsePointers.Add(questArrowPointer2D);
			return questArrowPointer2D;
		}

		public void ReturnPointer(QuestArrowPointer2D pointer)
		{
			pointer.Hide();
			_inUsePointers.Remove(pointer);
			_freePointers.Add(pointer);
		}
	}
}
