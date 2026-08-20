using System.Collections.Generic;
using Rewired;
using UnityEngine;

public class KeyRebindingButton : MonoBehaviour
{
	[SerializeField]
	private List<KeyRebindInfo> actionsToRebind;

	[SerializeField]
	private List<ControllerType> forbidenControllers;

	[SerializeField]
	public List<SettingMenuControls.ForbiddenRebindsStruct> forbiddenRebinds;

	public List<KeyRebindInfo> ActionsToRebind => actionsToRebind;

	public List<ControllerType> ForbidenControllers => forbidenControllers;

	public List<SettingMenuControls.ForbiddenRebindsStruct> ForbiddenRebinds => forbiddenRebinds;
}
