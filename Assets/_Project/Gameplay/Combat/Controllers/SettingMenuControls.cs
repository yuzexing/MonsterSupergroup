using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.Control;
using AstralShift.Helpers.Attributes;
using AstralShift.Managers;
using AstralShift.UI;
using AstralShift.UI.PopupWindows;
using Cysharp.Threading.Tasks;
using Rewired;
using RewiredConsts;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class SettingMenuControls : SettingsTabContentController, ICanSkipNotNullRefValidation
{
	private struct TargetMapping
	{
		public ControllerMap controllerMap;

		public int actionElementMapId;
	}

	[Serializable]
	public struct ForbiddenRebindsStruct
	{
		[ActionIdProperty(typeof(RewiredConsts.Action))]
		public int actionId;
	}

	private const string uiCategory = "UI";

	private SettingsMenuController settingsMenuController;

	[SerializeField]
	private List<KeyRebindingButton> _buttons;

	[SerializeField]
	private GridLayoutGroup buttonGrid;

	[SerializeField]
	private TextMeshProUGUI currentControllerText;

	[SerializeField]
	private GameObject controllerAim;

	[SerializeField]
	private GameObject mouseAim;

	private List<InputMapper> defaultInputMappers = new List<InputMapper>();

	private List<InputMapper> uiInputMappers = new List<InputMapper>();

	[SerializeField]
	private GameObject blockRaycastPanel;

	[SerializeField]
	private TextMeshProUGUI conflictFoundNameTxt;

	[SerializeField]
	private CustomUIButton resetBindsBT;

	private int currentCategory;

	private List<TargetMapping> replaceTargetMappings = new List<TargetMapping>();

	private Dictionary<int, bool> forbiddenRebinds;

	[FormerlySerializedAs("forbiddenRebindsList")]
	[SerializeField]
	private List<ForbiddenRebindsStruct> globalForbiddenRebindsList;

	private CustomUIButton currentRebindButton;

	private List<ForbiddenRebindsStruct> currentlyForbiddenRebinds;

	private List<ControllerType> currentlyForbiddenControllerTypes;

	private bool initialized;

	private PopupWindow conflictPopupWindow;

	[SerializeField]
	private float inputMapperTimeout = 5f;

	private Controller controller;

	private string prefixKey = "CPM_";

	private int mapsToRebindCount;

	private int inputMapedCount;

	private List<InputMapper.ConflictFoundEventData> conflictFoundEventData = new List<InputMapper.ConflictFoundEventData>();

	private Rewired.Player player => ReInput.players.GetPlayer(0);

	private ControllerMap controllerMap
	{
		get
		{
			if (controller == null)
			{
				return null;
			}
			return player.controllers.maps.GetMap(controller.type, controller.id, currentCategory, 0);
		}
	}

	private ControllerMap keyboardMap => player.controllers.maps.GetMap(ControllerType.Keyboard, 0, currentCategory, 0);

	private ControllerMap mouseMap => player.controllers.maps.GetMap(ControllerType.Mouse, 0, currentCategory, 0);

	private ControllerMap joystickMap => player.controllers.maps.GetMap(ControllerType.Joystick, controller.id, currentCategory, 0);

	public Dictionary<int, bool> ForbiddenRebinds
	{
		get
		{
			if (!initialized)
			{
				CreateGlobalForbiddenRebindsDictionary();
			}
			return forbiddenRebinds;
		}
	}

	public bool ShouldSkipNotNullRefAttributeValidation()
	{
		return true;
	}

	private void CreateGlobalForbiddenRebindsDictionary()
	{
		forbiddenRebinds = new Dictionary<int, bool>();
		foreach (ForbiddenRebindsStruct globalForbiddenRebinds in globalForbiddenRebindsList)
		{
			if (!forbiddenRebinds.TryAdd(globalForbiddenRebinds.actionId, value: false))
			{
				Debug.LogWarning("Duplicate forbidden rebind found ignoring entry");
			}
		}
		initialized = true;
	}

	public override void Open(bool instant = false)
	{
		base.Open(instant);
		settingsMenuController = ControllerManager.Instance.CurrentController as SettingsMenuController;
	}

	public override void Close(bool instant = false)
	{
		EventSystem.current.SetSelectedGameObject(null);
		base.Close(instant);
	}

	public override void ApplySettingsIfDirty()
	{
	}

	public override void Init()
	{
		base.Init();
		ReInput.userDataStore.Save();
		ControllerLifetime.OnControllerChanged += OnControllerChanged;
		controller = ControllerLifetime.ActiveController;
		if (controller.type == ControllerType.Mouse || controller.type == ControllerType.Keyboard)
		{
			if (currentControllerText != null)
			{
				currentControllerText.SetText(ControllerType.Keyboard.ToString());
			}
			controllerAim.SetActive(value: false);
			mouseAim.SetActive(value: true);
		}
		else
		{
			if (currentControllerText != null)
			{
				currentControllerText.SetText(controller.name);
			}
			controllerAim.SetActive(value: true);
			mouseAim.SetActive(value: false);
		}
		SettingsManager settingsManager = base.settings;
		settingsManager.OnSettingsSaved = (System.Action)Delegate.Combine(settingsManager.OnSettingsSaved, new System.Action(SaveMapping));
		SettingsManager settingsManager2 = base.settings;
		settingsManager2.OnSettingsRolledBack = (System.Action)Delegate.Combine(settingsManager2.OnSettingsRolledBack, new System.Action(RollbackMapping));
		defaultInputMappers.Add(new InputMapper());
		defaultInputMappers.Add(new InputMapper());
		foreach (InputMapper defaultInputMapper in defaultInputMappers)
		{
			defaultInputMapper.StoppedEvent += OnStopped;
			defaultInputMapper.InputMappedEvent += OnInputMapped;
			defaultInputMapper.ConflictFoundEvent += OnInputConflict;
			defaultInputMapper.TimedOutEvent += OnTimeout;
		}
		uiInputMappers.Add(new InputMapper());
		uiInputMappers.Add(new InputMapper());
		foreach (InputMapper uiInputMapper in uiInputMappers)
		{
			uiInputMapper.StoppedEvent += OnStopped;
			uiInputMapper.InputMappedEvent += OnInputMapped;
			uiInputMapper.ConflictFoundEvent += OnInputConflict;
			uiInputMapper.TimedOutEvent += OnTimeout;
		}
		resetBindsBT.onSubmit.AddListener(LoadDefaultMapping);
	}

	private new void OnDestroy()
	{
		ControllerLifetime.OnControllerChanged -= OnControllerChanged;
		SettingsManager settingsManager = base.settings;
		settingsManager.OnSettingsSaved = (System.Action)Delegate.Remove(settingsManager.OnSettingsSaved, new System.Action(SaveMapping));
		SettingsManager settingsManager2 = base.settings;
		settingsManager2.OnSettingsRolledBack = (System.Action)Delegate.Remove(settingsManager2.OnSettingsRolledBack, new System.Action(RollbackMapping));
	}

	private void OnEnable()
	{
		controller = ControllerLifetime.ActiveController;
		foreach (InputMapper defaultInputMapper in defaultInputMappers)
		{
			defaultInputMapper.options.timeout = inputMapperTimeout;
			defaultInputMapper.options.ignoreMouseXAxis = true;
			defaultInputMapper.options.ignoreMouseYAxis = true;
			defaultInputMapper.options.allowKeyboardModifierKeyAsPrimary = true;
			defaultInputMapper.options.allowAxes = true;
			defaultInputMapper.options.allowKeyboardKeysWithModifiers = false;
		}
		foreach (InputMapper uiInputMapper in uiInputMappers)
		{
			uiInputMapper.options.timeout = inputMapperTimeout;
			uiInputMapper.options.ignoreMouseXAxis = true;
			uiInputMapper.options.ignoreMouseYAxis = true;
			uiInputMapper.options.allowKeyboardModifierKeyAsPrimary = true;
			uiInputMapper.options.allowAxes = true;
			uiInputMapper.options.allowKeyboardKeysWithModifiers = false;
		}
		SettingsManager settingsManager = base.settings;
		settingsManager.OnRefresh = (System.Action)Delegate.Combine(settingsManager.OnRefresh, new System.Action(Refresh));
	}

	private void OnDisable()
	{
		foreach (InputMapper defaultInputMapper in defaultInputMappers)
		{
			defaultInputMapper.Stop();
		}
		SettingsManager settingsManager = base.settings;
		settingsManager.OnRefresh = (System.Action)Delegate.Remove(settingsManager.OnRefresh, new System.Action(Refresh));
	}

	private void Refresh()
	{
		if (_buttons != null && _buttons.Count > 0)
		{
			EventSystem.current.SetSelectedGameObject(null);
			EventSystem.current.SetSelectedGameObject(_buttons[0].gameObject);
			CustomUIButton component = _buttons[0].GetComponent<CustomUIButton>();
			if (component != null)
			{
				currentSelected = component;
			}
		}
	}

	protected override void GenerateButtonNavigation()
	{
		for (int i = 0; i < _buttons.Count; i++)
		{
			CustomUIButton selectable = _buttons[i].GetComponent<CustomUIButton>();
			if (!(selectable == null))
			{
				selectable.onSelect.AddListener(delegate
				{
					currentSelected = selectable;
				});
				selectable.onPointerEnter.AddListener(delegate
				{
					currentSelected = selectable;
				});
				int index = i;
				selectable.onSubmit.AddListener(delegate
				{
					currentRebindButton = selectable;
					selectable.Flashing(flashing: true);
					RemapInput(_buttons[index].ActionsToRebind, _buttons[index].ForbiddenRebinds, _buttons[index].ForbidenControllers);
				});
			}
		}
	}

	public void LoadDefaultMapping()
	{
		player.controllers.maps.LoadDefaultMaps(ControllerType.Mouse);
		player.controllers.maps.LoadDefaultMaps(ControllerType.Keyboard);
		player.controllers.maps.LoadDefaultMaps(ControllerType.Joystick);
		player.controllers.maps.LoadDefaultMaps(ControllerType.Custom);
		ControllerLifetime.RefreshInputs();
		ReInput.userDataStore.Save();
	}

	public void SaveMapping()
	{
		ReInput.userDataStore.Save();
		ControllerLifetime.RefreshInputs();
	}

	public void RollbackMapping()
	{
		ReInput.userDataStore.Load();
		ControllerLifetime.RefreshInputs();
	}

	private void OnControllerChanged()
	{
		if (ControllerLifetime.ActiveController == controller)
		{
			return;
		}
		controller = ControllerLifetime.ActiveController;
		if (controller.type == ControllerType.Mouse || controller.type == ControllerType.Keyboard)
		{
			if (currentControllerText != null)
			{
				string term = prefixKey + ControllerType.Keyboard;
				LocalizationMediator.GetTranslation(ref term);
				currentControllerText.SetText(term);
			}
			controllerAim.SetActive(value: false);
			mouseAim.SetActive(value: true);
		}
		else
		{
			if (currentControllerText != null)
			{
				string term2 = prefixKey + controller.name;
				LocalizationMediator.GetTranslation(ref term2);
				currentControllerText.SetText(term2);
			}
			controllerAim.SetActive(value: true);
			mouseAim.SetActive(value: false);
		}
		if ((ControllerLifetime.LastActiveControllerType == ControllerType.Keyboard || ControllerLifetime.LastActiveControllerType == ControllerType.Mouse) && (ControllerLifetime.ActiveControllerType == ControllerType.Keyboard || ControllerLifetime.ActiveControllerType == ControllerType.Mouse))
		{
			return;
		}
		foreach (InputMapper defaultInputMapper in defaultInputMappers)
		{
			if (defaultInputMapper.status == InputMapper.Status.AwaitingResponse)
			{
				CancelConflictingInput();
			}
			else
			{
				defaultInputMapper.Stop();
			}
		}
		if (conflictPopupWindow != null && conflictPopupWindow.isActiveAndEnabled)
		{
			conflictPopupWindow.Close();
		}
	}

	public void RemapInput(List<KeyRebindInfo> actionsInfo, List<ForbiddenRebindsStruct> forbiddenRebinds, List<ControllerType> forbiddenControllerTypes)
	{
		if (actionsInfo.Count > 2)
		{
			Debug.LogError("Only two key rebindings are supported");
			return;
		}
		mapsToRebindCount = actionsInfo.Count;
		inputMapedCount = 0;
		currentlyForbiddenRebinds = forbiddenRebinds;
		currentlyForbiddenControllerTypes = forbiddenControllerTypes;
		StartCoroutine(StartListeningDelayed(actionsInfo));
	}

	private IEnumerator StartListeningDelayed(List<KeyRebindInfo> actionsInfo)
	{
		yield return new WaitForSecondsRealtime(0.1f);
		for (int i = 0; i < actionsInfo.Count; i++)
		{
			if (controller.type == ControllerType.Keyboard || controller.type == ControllerType.Mouse)
			{
				KeyboardMouseInputMapping(actionsInfo[i]);
			}
			else
			{
				ControllerInputMapping(actionsInfo[i]);
			}
		}
		player.controllers.maps.SetMapsEnabled(state: false, "UI");
		settingsMenuController.blockInputs = true;
		blockRaycastPanel.SetActive(value: true);
	}

	private void KeyboardMouseInputMapping(KeyRebindInfo rebindInfo)
	{
		int num = -1;
		int num2 = -1;
		int num3 = -1;
		currentCategory = ReInput.mapping.GetAction(rebindInfo.actionToRebind).categoryId;
		foreach (ActionElementMap item in keyboardMap.ElementMapsWithAction(rebindInfo.actionToRebind))
		{
			if (item.ShowInField(rebindInfo.actionAxisRange))
			{
				num2 = item.id;
				break;
			}
		}
		foreach (ActionElementMap item2 in mouseMap.ElementMapsWithAction(rebindInfo.actionToRebind))
		{
			if (item2.ShowInField(rebindInfo.actionAxisRange))
			{
				num3 = item2.id;
				break;
			}
		}
		ControllerMap controllerMap = (keyboardMap.ContainsElementMap(num2) ? keyboardMap : ((!mouseMap.ContainsElementMap(num3)) ? null : mouseMap));
		if (controllerMap != null)
		{
			num = ((controllerMap.controllerType == ControllerType.Keyboard) ? num2 : num3);
			replaceTargetMappings.Add(new TargetMapping
			{
				actionElementMapId = num,
				controllerMap = controllerMap
			});
		}
		bool flag = false;
		bool flag2 = false;
		foreach (ControllerType currentlyForbiddenControllerType in currentlyForbiddenControllerTypes)
		{
			if (currentlyForbiddenControllerType == ControllerType.Mouse)
			{
				flag = true;
			}
			if (currentlyForbiddenControllerType == ControllerType.Keyboard)
			{
				flag2 = true;
			}
		}
		if (currentCategory == 1)
		{
			if (!flag2)
			{
				defaultInputMappers[0].Start(new InputMapper.Context
				{
					actionId = rebindInfo.actionToRebind,
					controllerMap = keyboardMap,
					actionRange = rebindInfo.actionAxisRange,
					actionElementMapToReplace = keyboardMap.GetElementMap(num)
				});
			}
			if (!flag)
			{
				defaultInputMappers[1].Start(new InputMapper.Context
				{
					actionId = rebindInfo.actionToRebind,
					controllerMap = mouseMap,
					actionRange = rebindInfo.actionAxisRange,
					actionElementMapToReplace = mouseMap.GetElementMap(num)
				});
			}
		}
		if (currentCategory == 2)
		{
			if (!flag2)
			{
				uiInputMappers[0].Start(new InputMapper.Context
				{
					actionId = rebindInfo.actionToRebind,
					controllerMap = keyboardMap,
					actionRange = rebindInfo.actionAxisRange,
					actionElementMapToReplace = keyboardMap.GetElementMap(num)
				});
			}
			if (!flag)
			{
				uiInputMappers[1].Start(new InputMapper.Context
				{
					actionId = rebindInfo.actionToRebind,
					controllerMap = mouseMap,
					actionRange = rebindInfo.actionAxisRange,
					actionElementMapToReplace = mouseMap.GetElementMap(num)
				});
			}
		}
	}

	private void ControllerInputMapping(KeyRebindInfo rebindInfo)
	{
		int elementMapId = -1;
		currentCategory = ReInput.mapping.GetAction(rebindInfo.actionToRebind).categoryId;
		foreach (ActionElementMap item in controllerMap.ElementMapsWithAction(rebindInfo.actionToRebind))
		{
			if (item.ShowInField(rebindInfo.actionAxisRange))
			{
				elementMapId = item.id;
			}
		}
		ActionElementMap elementMap = controllerMap.GetElementMap(elementMapId);
		if (currentCategory == 1)
		{
			defaultInputMappers[0].Start(new InputMapper.Context
			{
				actionId = rebindInfo.actionToRebind,
				controllerMap = controllerMap,
				actionRange = rebindInfo.actionAxisRange,
				actionElementMapToReplace = elementMap
			});
		}
		if (currentCategory == 2)
		{
			uiInputMappers[0].Start(new InputMapper.Context
			{
				actionId = rebindInfo.actionToRebind,
				controllerMap = controllerMap,
				actionRange = rebindInfo.actionAxisRange,
				actionElementMapToReplace = controllerMap.GetElementMap(elementMapId)
			});
		}
	}

	private void OnInputMapped(InputMapper.InputMappedEventData data)
	{
		player.controllers.maps.SetMapsEnabled(state: true, "UI");
		if (data.actionElementMap.controllerMap.categoryId == 1)
		{
			foreach (InputMapper defaultInputMapper in defaultInputMappers)
			{
				if (defaultInputMapper != data.inputMapper)
				{
					defaultInputMapper.Stop();
				}
			}
		}
		if (data.actionElementMap.controllerMap.categoryId == 2)
		{
			foreach (InputMapper uiInputMapper in uiInputMappers)
			{
				if (uiInputMapper != data.inputMapper)
				{
					uiInputMapper.Stop();
				}
			}
		}
		if (inputMapedCount < replaceTargetMappings.Count && replaceTargetMappings[inputMapedCount].controllerMap != null && data.actionElementMap.controllerMap != replaceTargetMappings[inputMapedCount].controllerMap)
		{
			replaceTargetMappings[inputMapedCount].controllerMap.DeleteElementMap(replaceTargetMappings[inputMapedCount].actionElementMapId);
		}
		inputMapedCount++;
		if (inputMapedCount == mapsToRebindCount)
		{
			replaceTargetMappings.Clear();
			player.controllers.maps.SetMapsEnabled(state: true, "UI");
			settingsMenuController.blockInputs = false;
			blockRaycastPanel.SetActive(value: false);
			ControllerLifetime.RefreshInputs();
		}
	}

	private void OnInputConflict(InputMapper.ConflictFoundEventData data)
	{
		AsyncInputConflict(data);
	}

	private async void AsyncInputConflict(InputMapper.ConflictFoundEventData data)
	{
		if (ForbiddenRebinds.ContainsKey(data.conflicts[0].actionId))
		{
			StopAllInputMappers();
			return;
		}
		foreach (ForbiddenRebindsStruct currentlyForbiddenRebind in currentlyForbiddenRebinds)
		{
			if (currentlyForbiddenRebind.actionId == data.conflicts[0].actionId)
			{
				StopAllInputMappers();
				return;
			}
		}
		conflictFoundEventData.Add(data);
		if (data.conflicts[0].action.categoryId == 1)
		{
			foreach (InputMapper defaultInputMapper in defaultInputMappers)
			{
				if (defaultInputMapper != data.inputMapper)
				{
					defaultInputMapper.Stop();
				}
			}
		}
		if (data.conflicts[0].action.categoryId == 2)
		{
			foreach (InputMapper uiInputMapper in uiInputMappers)
			{
				if (uiInputMapper != data.inputMapper)
				{
					uiInputMapper.Stop();
				}
			}
		}
		if (conflictFoundEventData.Count <= 1)
		{
			player.controllers.maps.SetMapsEnabled(state: true, "UI");
			settingsMenuController.blockInputs = true;
			blockRaycastPanel.SetActive(value: true);
			string term = "STT_ConflictFound";
			LocalizationMediator.GetTranslation(ref term);
			UniTask<PopupWindow> windowTask = PopupLauncher.Instance.RequestPopup(PopupLauncher.PopupType.SmallChoice, new PopupContext(string.Format(term), new System.Action(RebindConflictingInput), new System.Action(CancelConflictingInput)));
			await windowTask;
			conflictPopupWindow = windowTask.GetAwaiter().GetResult();
		}
	}

	private void OnTimeout(InputMapper.TimedOutEventData data)
	{
		currentRebindButton.Flashing(flashing: false);
		EventSystem.current.SetSelectedGameObject(currentRebindButton.gameObject);
		replaceTargetMappings.Clear();
		player.controllers.maps.SetMapsEnabled(state: true, "UI");
		settingsMenuController.blockInputs = false;
		blockRaycastPanel.SetActive(value: false);
		player.controllers.maps.SetMapsEnabled(state: true, "UI");
	}

	private void RebindConflictingInput()
	{
		settingsMenuController.blockInputs = false;
		blockRaycastPanel.SetActive(value: false);
		foreach (InputMapper.ConflictFoundEventData conflictFoundEventDatum in conflictFoundEventData)
		{
			if (conflictFoundEventDatum.IsSwapAllowed(2))
			{
				conflictFoundEventDatum.responseCallback(InputMapper.ConflictResponse.Swap);
			}
			else
			{
				conflictFoundEventDatum.responseCallback(InputMapper.ConflictResponse.Replace);
			}
		}
		conflictFoundEventData.Clear();
		EventSystem.current.SetSelectedGameObject(_buttons[0].gameObject);
		ControllerLifetime.RefreshInputs();
	}

	private void CancelConflictingInput()
	{
		inputMapedCount = 0;
		player.controllers.maps.SetMapsEnabled(state: true, "UI");
		settingsMenuController.blockInputs = false;
		blockRaycastPanel.SetActive(value: false);
		foreach (InputMapper.ConflictFoundEventData conflictFoundEventDatum in conflictFoundEventData)
		{
			conflictFoundEventDatum.responseCallback(InputMapper.ConflictResponse.Cancel);
		}
		conflictFoundEventData.Clear();
		EventSystem.current.SetSelectedGameObject(_buttons[0].gameObject);
		replaceTargetMappings.Clear();
	}

	private void OnStopped(InputMapper.StoppedEventData data)
	{
		currentRebindButton.Flashing(flashing: false);
	}

	private void StopAllInputMappers()
	{
		foreach (InputMapper defaultInputMapper in defaultInputMappers)
		{
			defaultInputMapper.Stop();
		}
		foreach (InputMapper uiInputMapper in uiInputMappers)
		{
			uiInputMapper.Stop();
		}
		foreach (InputMapper.ConflictFoundEventData conflictFoundEventDatum in conflictFoundEventData)
		{
			conflictFoundEventDatum.responseCallback(InputMapper.ConflictResponse.Cancel);
		}
		player.controllers.maps.SetMapsEnabled(state: true, "UI");
		conflictFoundEventData.Clear();
		player.controllers.maps.SetMapsEnabled(state: true, "UI");
		settingsMenuController.blockInputs = false;
		blockRaycastPanel.SetActive(value: false);
	}

	public override void ResetTabSettings()
	{
		SettingsData.Instance.ResetControls();
	}
}
