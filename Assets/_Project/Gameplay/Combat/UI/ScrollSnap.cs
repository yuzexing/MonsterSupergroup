using System;
using UnityEngine;
using UnityEngine.UI;

public class ScrollSnap : MonoBehaviour
{
	public ScrollRect scrollRect;

	public RectTransform contentPanel;

	private float _optionWidth;

	public HorizontalLayoutGroup HLG;

	public float snapSensitivity = 200f;

	public float snapForce;

	public bool allowManualScroll;

	private bool _isSnapped;

	private float _snapSpeed;

	private bool _immediate;

	private int _numberOfElements;

	private int _currentIndex;

	public Action<int> OnValueChange;

	public int ActiveChildCount
	{
		get
		{
			int num = 0;
			for (int i = 0; i < contentPanel.childCount; i++)
			{
				if (contentPanel.GetChild(i).gameObject.activeSelf)
				{
					num++;
				}
			}
			return num;
		}
	}

	public GameObject GetChild(int index)
	{
		int num = 0;
		for (int i = 0; i < contentPanel.childCount; i++)
		{
			GameObject gameObject = contentPanel.GetChild(i).gameObject;
			if (gameObject.activeSelf)
			{
				if (num == index)
				{
					return gameObject;
				}
				num++;
			}
		}
		return null;
	}

	private void Start()
	{
		Init();
	}

	public void Init()
	{
		if (contentPanel.childCount != 0)
		{
			_numberOfElements = ActiveChildCount;
			_optionWidth = contentPanel.GetChild(0).GetComponent<RectTransform>().rect.width;
		}
		if (!allowManualScroll)
		{
			scrollRect.enabled = false;
		}
	}

	private void Update()
	{
		if (_immediate)
		{
			contentPanel.localPosition = new Vector3(0f - (float)_currentIndex * (_optionWidth + HLG.spacing), contentPanel.localPosition.y, contentPanel.localPosition.z);
			_immediate = false;
			_isSnapped = true;
			Debug.Log("Scroll Snap - Value changed to " + _currentIndex);
			OnValueChange?.Invoke(_currentIndex);
			return;
		}
		if (scrollRect.velocity.magnitude < snapSensitivity && !_isSnapped)
		{
			scrollRect.velocity = Vector2.zero;
			_snapSpeed = snapForce * Time.unscaledDeltaTime;
			contentPanel.localPosition = new Vector3(Mathf.MoveTowards(contentPanel.localPosition.x, 0f - (float)_currentIndex * (_optionWidth + HLG.spacing), _snapSpeed), contentPanel.localPosition.y, contentPanel.localPosition.z);
			if (contentPanel.localPosition.x == 0f - (float)_currentIndex * (_optionWidth + HLG.spacing))
			{
				_isSnapped = true;
				Debug.Log("Scroll Snap - Value changed to " + _currentIndex);
				OnValueChange?.Invoke(_currentIndex);
			}
		}
		if (scrollRect.velocity.magnitude > snapSensitivity)
		{
			_isSnapped = false;
			_snapSpeed = 0f;
		}
	}

	public void NextElement()
	{
		if (_currentIndex < _numberOfElements - 1)
		{
			_currentIndex++;
			_isSnapped = false;
		}
	}

	public void PreviousElement()
	{
		if (_currentIndex > 0)
		{
			_currentIndex--;
			_isSnapped = false;
		}
	}

	public void GoToElement(int idx, bool immediate = false)
	{
		_currentIndex = idx;
		_isSnapped = false;
		_immediate = immediate;
	}

	public GameObject InstantiateAndAddLast(GameObject prefab)
	{
		GameObject element = UnityEngine.Object.Instantiate(prefab, contentPanel);
		return AddLast(element);
	}

	public GameObject AddLast(GameObject element)
	{
		element.transform.SetAsLastSibling();
		_numberOfElements++;
		CheckOptionWidth(element);
		return element;
	}

	public GameObject InstantiateAndAddFirst(GameObject prefab)
	{
		GameObject element = UnityEngine.Object.Instantiate(prefab, contentPanel);
		return AddFirst(element);
	}

	public GameObject AddFirst(GameObject element)
	{
		element.transform.SetAsFirstSibling();
		_numberOfElements++;
		_currentIndex++;
		CheckOptionWidth(element);
		return element;
	}

	public void DestroyAllElements()
	{
		for (int i = 0; i < contentPanel.childCount; i++)
		{
			UnityEngine.Object.Destroy(contentPanel.GetChild(i).gameObject);
		}
		_numberOfElements = 0;
		_currentIndex = 0;
	}

	private void CheckOptionWidth(GameObject Element)
	{
		if (_optionWidth == 0f)
		{
			_optionWidth = Element.GetComponent<RectTransform>().rect.width;
		}
	}
}
