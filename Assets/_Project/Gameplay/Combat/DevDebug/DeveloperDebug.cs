using System;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.HellMaiden.UI;
using Newtonsoft.Json;
using UnityEngine;

namespace AstralShift.HellMaiden.DevDebug
{
	public class DeveloperDebug
	{
		private const string prefix = "Debug ";

		public const string DevModeKey = "Debug DevMode";

		public const string LastOpenDebugToolbarKey = "Debug LastOpenDebugToolbar";

		public const string ShowDebugTextKey = "Debug ShowText";

		public const string HealingAmountKey = "Debug HealingAmount";

		public const string PlayWithoutCardsKey = "Debug PlayWithoutCards";

		public const string PlayWithoutMapGenerationKey = "Debug PlayWithoutMapGeneration";

		public const string enableTutorialsPopupKey = "Debug enableTutorialPopup";

		public const string forceTutorialsPopupKey = "Debug forceTutorialPopup";

		public const string LoadSaveFileKey = "Debug Load Editor Save File";

		public const string EnemyAI_PanelKey = "Debug EnemyInfoPanel";

		public const string EnemyAI_PanelPosKey = "Debug EnemyAIPanelPos";

		public const string EnemyAI_PanelSizeKey = "Debug EnemyAIPanelSize";

		public const string EnemyAI_PanelOffsetKey = "Debug EnemyAIPanelOffset";

		public const string EnemyAI_PanelFontColorKey = "Debug EnemyAIPanelFontColor";

		public const string EnemyAI_PanelFontSizeKey = "Debug EnemyAIPanelFontSize";

		public const string CardTesterOverrideSignatureWeapon = "Debug CardTesterOverrideSignature";

		public const string CardTesterSignatureWeaponDataIndex = "Debug CardTesterSignatureWeaponDataIndex";

		public const string CardTesterHandSlotKey = "Debug CardTesterHandSlot";

		public const string CardTesterWeaponIdKey = "Debug CardTesterWeaponId";

		public const string CardTesterEquipmentIDKey = "Debug CardTesterEquipmentID";

		public const string CardTesterMenuGizmos = "Debug CardTesterMenuGizmos";

		public const string CardTesterPoetPools = "Debug PoetPools";

		public const string CardTesterOverridePoetPools = "Debug OverridePoetPools";

		public const string SceneDebug_SelectedScene = "Debug SceneDebug_SelectedScene";

		public static DevMod buildDevMod;

		public static int enemyDamageMode;

		public static bool noEnemyDamageDebug;

		public static bool fatalEneyDamageDebug;

		private static int hudShowState;

		private static WeaponDB _weaponDB;

		private static WeaponData[] _signatureWeapons;

		public static bool devMode
		{
			get
			{
				if (buildDevMod != null)
				{
					return buildDevMod.devMode;
				}
				return false;
			}
		}

		public static int lastOpenDebugToolbar
		{
			get
			{
				if (!devMode)
				{
					return 0;
				}
				if (!(buildDevMod != null))
				{
					return 0;
				}
				return buildDevMod.lastOpenDebugToolbar;
			}
		}

		public static bool ShowDebugText
		{
			get
			{
				if (!devMode)
				{
					return false;
				}
				if (buildDevMod != null)
				{
					return buildDevMod.showDebugText;
				}
				return false;
			}
		}

		public static int healingAmount
		{
			get
			{
				if (!devMode)
				{
					return 0;
				}
				if (!(buildDevMod != null))
				{
					return 0;
				}
				return buildDevMod.healingAmount;
			}
		}

		public static bool PlayWithoutCards
		{
			get
			{
				if (!devMode)
				{
					return false;
				}
				if (buildDevMod != null)
				{
					return buildDevMod.playWithoutCards;
				}
				return false;
			}
		}

		public static bool PlayWithoutMapGeneration
		{
			get
			{
				if (!devMode)
				{
					return false;
				}
				if (buildDevMod != null)
				{
					return buildDevMod.playWithoutMapGeneration;
				}
				return false;
			}
		}

		public static bool EnableTutorialPopups
		{
			get
			{
				if (!devMode)
				{
					return true;
				}
				if (buildDevMod != null)
				{
					return buildDevMod.enableTutorialPopup;
				}
				return false;
			}
		}

		public static bool ForceTutorialPopups
		{
			get
			{
				if (!devMode || !EnableTutorialPopups)
				{
					return false;
				}
				if (buildDevMod != null)
				{
					return buildDevMod.forceTutorialPopup;
				}
				return false;
			}
		}

		public static bool LoadEditorSave
		{
			get
			{
				if (!devMode)
				{
					return true;
				}
				if (buildDevMod != null)
				{
					return buildDevMod.LoadEditorSave;
				}
				return false;
			}
		}

		public static bool EnemyAI_Panel
		{
			get
			{
				if (!devMode)
				{
					return false;
				}
				if (buildDevMod != null)
				{
					return buildDevMod.playWithoutMapGeneration;
				}
				return false;
			}
		}

		public static int EnemyAI_PanelPos
		{
			get
			{
				if (!devMode)
				{
					return 0;
				}
				if (!(buildDevMod != null))
				{
					return 0;
				}
				return (int)buildDevMod.panelAnchor;
			}
		}

		public static Vector2 EnemyAI_PanelSize
		{
			get
			{
				if (!devMode)
				{
					return Vector2.zero;
				}
				if (!(buildDevMod != null))
				{
					return Vector2.zero;
				}
				return buildDevMod.panelSize;
			}
		}

		public static Vector2 EnemyAI_PanelOffset
		{
			get
			{
				if (!devMode)
				{
					return Vector2.zero;
				}
				if (!(buildDevMod != null))
				{
					return Vector2.zero;
				}
				return buildDevMod.panelOffset;
			}
		}

		public static Color EnemyAI_PanelFontColor
		{
			get
			{
				if (!devMode)
				{
					return Color.clear;
				}
				if (!(buildDevMod != null))
				{
					return Color.clear;
				}
				return buildDevMod.fontColor;
			}
		}

		public static int EnemyAI_PanelFontSize
		{
			get
			{
				if (!devMode)
				{
					return 10;
				}
				if (!(buildDevMod != null))
				{
					return 10;
				}
				return buildDevMod.fontSize;
			}
		}

		public static bool CardTester_OverrideSignatureWeapon
		{
			get
			{
				if (!devMode)
				{
					return false;
				}
				if ((bool)buildDevMod)
				{
					return buildDevMod.OverrideSignatureWeapon;
				}
				return false;
			}
		}

		public static WeaponData CardTester_SignatureWeapon
		{
			get
			{
				if (!devMode)
				{
					return null;
				}
				if (!buildDevMod)
				{
					return null;
				}
				return buildDevMod.SignatureWeaponData;
			}
		}

		public static int CardTester_HandSlot
		{
			get
			{
				if (!devMode)
				{
					return 0;
				}
				if (!(buildDevMod != null))
				{
					return 10;
				}
				return buildDevMod.HandSlot;
			}
		}

		public static uint CardTester_WeaponID
		{
			get
			{
				if (!devMode)
				{
					return 0u;
				}
				if (!(buildDevMod != null))
				{
					return 0u;
				}
				return buildDevMod.WeaponID;
			}
		}

		public static uint CardTester_EquipmentID
		{
			get
			{
				if (!devMode)
				{
					return 0u;
				}
				if (!(buildDevMod != null))
				{
					return 0u;
				}
				return buildDevMod.EquipmentID;
			}
		}

		public static bool CardTester_MenuGizmos
		{
			get
			{
				if (!devMode)
				{
					return false;
				}
				if (!(buildDevMod != null))
				{
					return false;
				}
				return buildDevMod.cardMenuGizmos;
			}
		}

		public static bool OverrideUnlockPools
		{
			get
			{
				if (!devMode)
				{
					return false;
				}
				if (buildDevMod != null)
				{
					return buildDevMod.OverrideUnlockedPools;
				}
				return false;
			}
		}

		public static PoetPoolID[] unlockedPools
		{
			get
			{
				_ = devMode;
				return new PoetPoolID[1];
			}
		}

		public static void DebugIncreaseHealth()
		{
			if (devMode)
			{
				if (healingAmount >= 0)
				{
					GameDirector.Instance.Player.IncreaseHealth(healingAmount);
				}
				else
				{
					GameDirector.Instance.Player.Damage(-healingAmount, DamageType.Normal);
				}
				DebugPopupHelper.Instance.CreateFloatingDebugText(GameDirector.Instance.Player.transform.position, $"Healed by {healingAmount}");
			}
		}

		public static void DebugInvulnerabilitySwitch()
		{
			GameDirector.Instance.Player.DebugInvulnerabilitySwitch();
			DebugPopupHelper.Instance.CreateFloatingDebugText(GameDirector.Instance.Player.transform.position, $"Invunerability = {GameDirector.Instance.Player.IsInvulnerable}");
		}

		public static void DebugEnemyDamageSwitch()
		{
			enemyDamageMode++;
			noEnemyDamageDebug = false;
			fatalEneyDamageDebug = false;
			string text = "";
			switch (enemyDamageMode)
			{
			case 0:
				noEnemyDamageDebug = false;
				fatalEneyDamageDebug = false;
				text = "normal";
				break;
			case 1:
				noEnemyDamageDebug = true;
				text = "no damage";
				break;
			case 2:
				enemyDamageMode = -1;
				noEnemyDamageDebug = false;
				fatalEneyDamageDebug = true;
				text = "fatal damage";
				break;
			default:
				text = "Error value outside of set paramenter";
				break;
			}
			DebugPopupHelper.Instance.CreateFloatingDebugText(GameDirector.Instance.Player.transform.position, "Enemy damage = " + text);
		}

		public static void DebugHudShowSwitch()
		{
			hudShowState++;
			if (hudShowState > 2)
			{
				hudShowState = 0;
			}
			string text = "Showing";
			switch (hudShowState)
			{
			case 0:
				text = "Showing";
				CombatUIManager.Instance.ShowHud();
				break;
			case 1:
				text = "Hiding Minimap";
				CombatUIManager.Instance.HideMinimap();
				break;
			case 2:
				text = "Hiding";
				CombatUIManager.Instance.HideHud();
				break;
			}
			DebugPopupHelper.Instance.CreateFloatingDebugText(GameDirector.Instance.Player.transform.position, "Hud " + text);
		}

		public static Vector3 ConvertStringToVector3(string str)
		{
			str = str.Trim('(', ')');
			string[] array = str.Split(",");
			try
			{
				return new Vector3(float.Parse(array[0]), float.Parse(array[1]), float.Parse(array[2]));
			}
			catch (FormatException)
			{
				return Vector2.zero;
			}
		}

		public static Vector2 ConvertStringToVector2(string str)
		{
			str = str.Trim('(', ')');
			string[] array = str.Split(",");
			try
			{
				return new Vector2(float.Parse(array[0]), float.Parse(array[1]));
			}
			catch (FormatException)
			{
				return Vector2.zero;
			}
		}

		public static Color ConvertStringToColor(string str)
		{
			str = str.Trim('R', 'G', 'B', 'A', '(', ')');
			string[] array = str.Split(",");
			try
			{
				return new Color(float.Parse(array[0]), float.Parse(array[1]), float.Parse(array[2]), float.Parse(array[3]));
			}
			catch (FormatException)
			{
				return Color.white;
			}
		}

		public static void GetCurrentAssignedWeaponDB()
		{
		}

		public static void GetSignatureWeapons()
		{
		}

		public static void AddWeapon()
		{
			WeaponData weaponData = GameDirector.Instance.runtimeDB.GetWeaponData(CardTester_WeaponID);
			PlayerHand.Instance.GetHandSlotFromIndex(CardTester_HandSlot - 1).AddWeapon(new RuntimeWeaponData(weaponData));
		}

		public static void RemoveWeapon()
		{
			PlayerHand.Instance.GetHandSlotFromIndex(CardTester_HandSlot - 1).ClearEquipments();
			PlayerHand.Instance.GetHandSlotFromIndex(CardTester_HandSlot - 1).ClearWeapon();
		}

		public static void AddEquipment()
		{
			EquipmentData equipmentData = GameDirector.Instance.runtimeDB.GetEquipmentData(CardTester_EquipmentID);
			PlayerHand.Instance.GetHandSlotFromIndex(CardTester_HandSlot - 1).AddEquipment(new RuntimeEquipmentData(equipmentData));
		}

		public static void ClearEquipments()
		{
			PlayerHand.Instance.ClearEquipments(CardTester_HandSlot);
		}

		public static void ClearWeapons()
		{
			PlayerHand.Instance.ClearWeapons();
		}

		public static void ClearAll()
		{
			PlayerHand.Instance.ClearAll();
		}

		private static object DeserializeJSON(string serialized)
		{
			return JsonConvert.DeserializeObject(serialized);
		}
	}
}
