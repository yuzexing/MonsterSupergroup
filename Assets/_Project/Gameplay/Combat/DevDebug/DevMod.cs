using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using UnityEngine;

[CreateAssetMenu(fileName = "DevMod", menuName = "HellMaiden/Data/DevMod")]
public class DevMod : ScriptableObject
{
	public enum DebugPanelAnchor
	{
		TopLeft = 0,
		TopRight = 1,
		BottomLeft = 2,
		BottomRight = 3
	}

	public bool devMode;

	public int lastOpenDebugToolbar;

	public int healingAmount;

	public bool showDebugText;

	public bool playWithoutCards;

	public bool playWithoutMapGeneration;

	public bool LoadEditorSave;

	public bool enableTutorialPopup;

	public bool forceTutorialPopup;

	[Header("EnemyAI")]
	public bool enemyAIDebugPanel;

	public DebugPanelAnchor panelAnchor;

	public Vector2 panelSize = new Vector2(500f, 600f);

	public Vector2 panelOffset = new Vector2(30f, 30f);

	public Color fontColor = Color.magenta;

	public int fontSize = 20;

	[Header("Card Tester")]
	public bool OverrideSignatureWeapon;

	[SerializeField]
	[HideInInspector]
	private int _signatureWeaponIndex;

	public WeaponData SignatureWeaponData;

	public int HandSlot;

	public uint WeaponID;

	public uint EquipmentID;

	public bool cardMenuGizmos;

	public PoetPoolID[] unlockPools;

	public bool OverrideUnlockedPools;

	public int SignatureWeaponIndex
	{
		get
		{
			return _signatureWeaponIndex;
		}
		set
		{
			_signatureWeaponIndex = value;
		}
	}
}
