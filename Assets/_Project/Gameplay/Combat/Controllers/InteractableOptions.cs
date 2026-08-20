using System;
using System.Collections.Generic;
using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InteractableOptions : MonoBehaviour
{
	[SerializeField]
	private Button confirmButton;

	[SerializeField]
	private TMP_Text labelText;

	[SerializeField]
	private EventReference optionSelectSound;

	private readonly List<string> _options = new List<string>();

	private int _currentIndex = -1;

	public Action<int> OnOptionChanged;

	[SerializeField]
	private Sprite selectedSprite;

	[SerializeField]
	private Sprite unSelectedSprite;

	private EventTrigger trigger;

	private Action _subscribedAction;

	private void Awake()
	{
		if (confirmButton != null)
		{
			confirmButton.onClick.AddListener(OnConfirmButtonClicked);
		}
		SubscribeListeners();
	}

	private void OnDestroy()
	{
		if (confirmButton != null)
		{
			confirmButton.onClick.RemoveListener(OnConfirmButtonClicked);
		}
		UnsubscribeListeners();
	}

	public void AddOption(string text, bool selected = false, bool localize = true)
	{
		_options.Add(text);
		if (selected || _currentIndex == -1)
		{
			_currentIndex = _options.Count - 1;
			RefreshLabel();
		}
	}

	public void RemoveAllOptions()
	{
		_options.Clear();
		_currentIndex = -1;
		RefreshLabel();
	}

	public void LockOptions(int index = -1)
	{
		if (index >= 0 && index < _options.Count)
		{
			_currentIndex = index;
			RefreshLabel();
		}
	}

	public void OnConfirmButtonClicked()
	{
		if (!optionSelectSound.IsNull)
		{
			RuntimeManager.PlayOneShot(optionSelectSound);
		}
		OnOptionChanged?.Invoke(_currentIndex);
	}

	private void RefreshLabel()
	{
		if (!(labelText == null))
		{
			if (_currentIndex >= 0 && _currentIndex < _options.Count)
			{
				labelText.text = _options[_currentIndex];
			}
			else
			{
				labelText.text = string.Empty;
			}
		}
	}

	public void SubscribeListeners()
	{
		trigger = confirmButton.gameObject.AddComponent<EventTrigger>();
		EventTrigger.Entry entry = new EventTrigger.Entry();
		entry.eventID = EventTriggerType.PointerEnter;
		entry.callback.AddListener(delegate
		{
			SwapSprite(confirmButton.image, selectedSprite);
		});
		EventTrigger.Entry entry2 = new EventTrigger.Entry();
		entry2.eventID = EventTriggerType.PointerExit;
		entry2.callback.AddListener(delegate
		{
			SwapSprite(confirmButton.image, unSelectedSprite);
		});
		trigger.triggers.Add(entry);
		trigger.triggers.Add(entry2);
	}

	public void UnsubscribeListeners()
	{
		foreach (EventTrigger.Entry trigger in trigger.triggers)
		{
			if (trigger.eventID == EventTriggerType.PointerEnter)
			{
				trigger.callback.RemoveAllListeners();
			}
		}
	}

	public void SwapSprite(Image targetImage, Sprite newSprite)
	{
		if (targetImage != null && newSprite != null)
		{
			targetImage.sprite = newSprite;
		}
	}

	public void SetSelectedSprite(bool selected)
	{
		SwapSprite(confirmButton.image, selected ? selectedSprite : unSelectedSprite);
	}
}
