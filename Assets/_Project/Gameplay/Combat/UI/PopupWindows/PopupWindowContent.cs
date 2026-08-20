using System.Collections.Generic;
using UnityEngine;

public class PopupWindowContent : MonoBehaviour
{
	[SerializeField]
	private List<PopupWindowPage> pages;

	public List<PopupWindowPage> Pages => pages;

	private void Awake()
	{
		for (int i = 0; i < pages.Count; i++)
		{
			pages[i].RectTransform.localPosition = Vector2.zero;
		}
	}
}
