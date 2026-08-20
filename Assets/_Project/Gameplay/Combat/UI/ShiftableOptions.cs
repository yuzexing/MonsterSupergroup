using System;
using System.Collections.Generic;
using FMODUnity;
using I2.Loc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.UI
{
	public class ShiftableOptions : MonoBehaviour
	{
		private class Entry
		{
			public string raw;

			public bool localize;

			public TMP_Text label;
		}

		private enum ButtonType
		{
			Custom = 0,
			Default = 1
		}

		public ScrollSnap scrollSnap;

		[SerializeField]
		private ButtonType buttonType;

		[SerializeField]
		private CustomUIButton left;

		[SerializeField]
		private CustomUIButton right;

		[SerializeField]
		private Button leftLegacyButton;

		[SerializeField]
		private Button rightLegacyButton;

		[SerializeField]
		protected bool instantiatePrefabs = true;

		public GameObject OptionPrefab;

		private int _currentIndex;

		private int _numberOfChoices;

		public Action<int> OnOptionChanged;

		[SerializeField]
		private EventReference optionSelectFailSound;

		[SerializeField]
		private EventReference optionSelectSuccessSound;

		private readonly List<Entry> _entries = new List<Entry>();

		private const string PrefixKey = "STT_";

		public int CurrentIndex => _currentIndex;

		private bool IsLeftInteractable
		{
			get
			{
				return buttonType switch
				{
					ButtonType.Custom => left.interactable, 
					ButtonType.Default => leftLegacyButton.interactable, 
					_ => left.interactable, 
				};
			}
			set
			{
				switch (buttonType)
				{
				case ButtonType.Custom:
					left.interactable = value;
					break;
				case ButtonType.Default:
					leftLegacyButton.interactable = value;
					break;
				default:
					left.interactable = value;
					break;
				}
			}
		}

		private bool IsRightInteractable
		{
			get
			{
				return buttonType switch
				{
					ButtonType.Custom => right.interactable, 
					ButtonType.Default => rightLegacyButton.interactable, 
					_ => right.interactable, 
				};
			}
			set
			{
				switch (buttonType)
				{
				case ButtonType.Custom:
					right.interactable = value;
					break;
				case ButtonType.Default:
					rightLegacyButton.interactable = value;
					break;
				default:
					right.interactable = value;
					break;
				}
			}
		}

		private void OnEnable()
		{
			LocalizationManager.OnLocalizeEvent += RefreshLocalizedOptions;
			RefreshLocalizedOptions();
		}

		private void OnDisable()
		{
			LocalizationManager.OnLocalizeEvent -= RefreshLocalizedOptions;
		}

		private void Start()
		{
			if (!instantiatePrefabs)
			{
				ReCalculate();
			}
		}

		public void ReCalculate()
		{
			scrollSnap.Init();
			if (!instantiatePrefabs)
			{
				_numberOfChoices = scrollSnap.ActiveChildCount;
			}
			_currentIndex = 0;
		}

		public GameObject GetElement(int index)
		{
			return scrollSnap.GetChild(index);
		}

		public GameObject GetCurrentElement()
		{
			return scrollSnap.GetChild(CurrentIndex);
		}

		public void ShiftLeftImmediate()
		{
			if (!instantiatePrefabs)
			{
				_numberOfChoices = scrollSnap.ActiveChildCount;
			}
			if (_currentIndex > 0)
			{
				if (!optionSelectSuccessSound.IsNull)
				{
					RuntimeManager.PlayOneShot(optionSelectSuccessSound);
				}
				_currentIndex--;
				scrollSnap.GoToElement(_currentIndex, immediate: true);
				if (_currentIndex == 0)
				{
					IsLeftInteractable = false;
					IsRightInteractable = true;
				}
				else
				{
					IsLeftInteractable = true;
					IsRightInteractable = true;
				}
				OnOptionChanged(_currentIndex);
			}
			else if (!optionSelectFailSound.IsNull)
			{
				RuntimeManager.PlayOneShot(optionSelectFailSound);
			}
		}

		public void ShiftLeft()
		{
			if (!instantiatePrefabs)
			{
				_numberOfChoices = scrollSnap.ActiveChildCount;
			}
			if (_currentIndex > 0)
			{
				if (!optionSelectSuccessSound.IsNull)
				{
					RuntimeManager.PlayOneShot(optionSelectSuccessSound);
				}
				_currentIndex--;
				scrollSnap.PreviousElement();
				if (_currentIndex == 0)
				{
					IsLeftInteractable = false;
					IsRightInteractable = true;
				}
				else
				{
					IsLeftInteractable = true;
					IsRightInteractable = true;
				}
				OnOptionChanged(_currentIndex);
			}
			else if (!optionSelectFailSound.IsNull)
			{
				RuntimeManager.PlayOneShot(optionSelectFailSound);
			}
		}

		public void ShiftRightImmediate()
		{
			if (!instantiatePrefabs)
			{
				_numberOfChoices = scrollSnap.ActiveChildCount;
			}
			if (_currentIndex < _numberOfChoices - 1)
			{
				if (!optionSelectSuccessSound.IsNull)
				{
					RuntimeManager.PlayOneShot(optionSelectSuccessSound);
				}
				_currentIndex++;
				scrollSnap.GoToElement(_currentIndex, immediate: true);
				if (_currentIndex == _numberOfChoices - 1)
				{
					IsLeftInteractable = true;
					IsRightInteractable = false;
				}
				else
				{
					IsLeftInteractable = true;
					IsRightInteractable = true;
				}
				OnOptionChanged(_currentIndex);
			}
			else if (!optionSelectFailSound.IsNull)
			{
				RuntimeManager.PlayOneShot(optionSelectFailSound);
			}
		}

		public void ShiftRight()
		{
			if (!instantiatePrefabs)
			{
				_numberOfChoices = scrollSnap.ActiveChildCount;
			}
			if (_currentIndex < _numberOfChoices - 1)
			{
				if (!optionSelectSuccessSound.IsNull)
				{
					RuntimeManager.PlayOneShot(optionSelectSuccessSound);
				}
				_currentIndex++;
				scrollSnap.NextElement();
				if (_currentIndex == _numberOfChoices - 1)
				{
					IsLeftInteractable = true;
					IsRightInteractable = false;
				}
				else
				{
					IsLeftInteractable = true;
					IsRightInteractable = true;
				}
				OnOptionChanged(_currentIndex);
			}
			else if (!optionSelectFailSound.IsNull)
			{
				RuntimeManager.PlayOneShot(optionSelectFailSound);
			}
		}

		public void AddOption(string text, bool selected = false, bool localize = true)
		{
			GameObject obj = scrollSnap.InstantiateAndAddLast(OptionPrefab);
			string raw = text;
			if (localize)
			{
				text = "STT_" + text;
				LocalizationMediator.GetTranslation(ref text);
			}
			TMP_Text componentInChildren = obj.GetComponentInChildren<TMP_Text>();
			componentInChildren.text = text;
			_entries.Add(new Entry
			{
				raw = raw,
				localize = localize,
				label = componentInChildren
			});
			_numberOfChoices++;
			if (selected)
			{
				_currentIndex = _numberOfChoices - 1;
			}
		}

		public void RemoveAllOptions()
		{
			_entries.Clear();
			_numberOfChoices = 0;
			scrollSnap.DestroyAllElements();
		}

		private void RefreshLocalizedOptions()
		{
			foreach (Entry entry in _entries)
			{
				string term = (entry.localize ? ("STT_" + entry.raw) : entry.raw);
				if (entry.localize && LocalizationMediator.GetTranslationPath(ref term))
				{
					entry.label.text = LocalizationManager.GetTranslation(term);
				}
				else
				{
					entry.label.text = term;
				}
			}
		}

		public void LockOptions(int index = -1)
		{
			if (index != -1)
			{
				_currentIndex = index;
			}
			if (_currentIndex == 0)
			{
				IsLeftInteractable = false;
			}
			if (_currentIndex == _numberOfChoices - 1)
			{
				IsRightInteractable = false;
			}
			scrollSnap.GoToElement(_currentIndex, immediate: true);
		}
	}
}
