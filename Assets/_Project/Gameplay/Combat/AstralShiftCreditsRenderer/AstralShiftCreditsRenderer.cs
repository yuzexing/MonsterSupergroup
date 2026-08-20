using System;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.AstralShiftCreditsRenderer
{
	public class AstralShiftCreditsRenderer : MonoBehaviour
	{
		[Header("JSON File")]
		public TextAsset jsonData;

		[Header("Core Prefabs")]
		public GameObject sessionPrefab;

		[Header("Group Prefabs")]
		public GameObject groupPrefab_TwoColumns;

		public GameObject groupPrefab_SingleColumn;

		[Header("Category & Entry Prefabs")]
		public GameObject categoryPrefab;

		public GameObject creditEntryPrefab;

		[Header("Scroll Container")]
		public ScrollRect mainScrollContainer;

		public Transform contentParent;

		[Header("Auto-Refresh")]
		public bool refreshOnStart = true;

		[Header("Testing")]
		public bool refresh;

		private JsonSerializerSettings jsonSettings;

		private const string ALIGNMENT_CENTER = "Center";

		private const string ALIGNMENT_LEFT = "Left";

		private const string ALIGNMENT_RIGHT = "Right";

		private void Awake()
		{
			jsonSettings = new JsonSerializerSettings
			{
				Formatting = Formatting.None,
				NullValueHandling = NullValueHandling.Ignore,
				MissingMemberHandling = MissingMemberHandling.Ignore
			};
		}

		public void InitializeCredits()
		{
			if (mainScrollContainer != null && contentParent == null)
			{
				contentParent = mainScrollContainer.content;
			}
			if (refreshOnStart && jsonData != null && !string.IsNullOrEmpty(jsonData.text))
			{
				RenderCredits();
			}
		}

		private void Update()
		{
			if (refresh)
			{
				refresh = false;
				Refresh();
			}
		}

		[ContextMenu("Refresh Credits")]
		public void Refresh()
		{
			Debug.Log("Refreshing credits...");
			RenderCredits();
		}

		[ContextMenu("Clear All")]
		public void ClearAll()
		{
			Debug.Log("Clearing all containers...");
			ClearContent();
		}

		public void RenderCredits()
		{
			if (jsonData == null)
			{
				Debug.LogError("JSON Data file is not assigned!");
			}
			else
			{
				RenderCredits(jsonData.text);
			}
		}

		public void RenderCredits(string customJsonData)
		{
			try
			{
				ClearContent();
				if (contentParent == null)
				{
					Debug.LogError("Content Parent is not assigned!");
					return;
				}
				RootCreditsData rootCreditsData = JsonConvert.DeserializeObject<RootCreditsData>(customJsonData, jsonSettings);
				if (rootCreditsData?.Sessions == null)
				{
					Debug.LogError("No Sessions found in JSON");
					return;
				}
				foreach (Session session in rootCreditsData.Sessions)
				{
					RenderSession(session);
				}
				LayoutRebuilder.ForceRebuildLayoutImmediate(contentParent as RectTransform);
				if (mainScrollContainer != null)
				{
					mainScrollContainer.verticalNormalizedPosition = 1f;
				}
				Debug.Log($"Successfully rendered {rootCreditsData.Sessions.Count} sessions");
			}
			catch (Exception ex)
			{
				Debug.LogError("Error rendering credits: " + ex.Message + "\n" + ex.StackTrace);
			}
		}

		private void RenderSession(Session session)
		{
			if (sessionPrefab == null)
			{
				Debug.LogError("Session Prefab is not assigned!");
				return;
			}
			GameObject gameObject = UnityEngine.Object.Instantiate(sessionPrefab, contentParent);
			gameObject.name = "Session_" + session.Title;
			TMP_Text componentInChildren = gameObject.GetComponentInChildren<TMP_Text>();
			if (componentInChildren != null)
			{
				string term = session.Title;
				LocalizationMediator.GetTranslation(ref term);
				componentInChildren.text = ((!string.IsNullOrEmpty(term)) ? term : session.Title);
				if (string.IsNullOrEmpty(term) && session.Title.StartsWith("CRD_"))
				{
					Debug.LogWarning("No translation found for session term: " + session.Title);
					componentInChildren.text = session.Title.Replace("CRD_", "").Replace('_', ' ');
				}
			}
			if (session.Groups == null)
			{
				return;
			}
			foreach (Group group in session.Groups)
			{
				RenderJSONGroup(group, gameObject.transform);
			}
		}

		private void RenderJSONGroup(Group group, Transform sessionTransform)
		{
			GameObject gameObject = null;
			gameObject = ((!(group.AlignmentType == "Center")) ? groupPrefab_TwoColumns : groupPrefab_SingleColumn);
			if (gameObject == null)
			{
				Debug.LogError("Group Prefab for " + group.AlignmentType + " is not assigned!");
				return;
			}
			GameObject obj = UnityEngine.Object.Instantiate(gameObject, sessionTransform);
			obj.name = "Group_" + group.AlignmentType;
			Transform transform = obj.transform;
			if (group.AlignmentType == "Center")
			{
				GameObject gameObject2 = transform.GetChild(0).gameObject;
				if (!(gameObject2 != null))
				{
					return;
				}
				Transform container = FindOrCreateCategoryContainer(gameObject2.transform);
				{
					foreach (CategoryEntry categoryEntry in group.CategoryEntries)
					{
						RenderCategory(categoryEntry, container);
					}
					return;
				}
			}
			GameObject gameObject3 = transform.GetChild(0).gameObject;
			GameObject gameObject4 = transform.GetChild(1).gameObject;
			Transform transform2 = ((gameObject3 != null) ? FindOrCreateCategoryContainer(gameObject3.transform) : null);
			Transform transform3 = ((gameObject4 != null) ? FindOrCreateCategoryContainer(gameObject4.transform) : null);
			foreach (CategoryEntry categoryEntry2 in group.CategoryEntries)
			{
				if (categoryEntry2.Position == "Left" && transform2 != null)
				{
					RenderCategory(categoryEntry2, transform2);
					continue;
				}
				if (categoryEntry2.Position == "Right" && transform3 != null)
				{
					RenderCategory(categoryEntry2, transform3);
					continue;
				}
				Debug.LogWarning("Category '" + categoryEntry2.Title + "' has position '" + categoryEntry2.Position + "'. Putting in Left column.");
				if (transform2 != null)
				{
					RenderCategory(categoryEntry2, transform2);
				}
			}
		}

		private Transform FindOrCreateCategoryContainer(Transform columnTransform)
		{
			foreach (Transform item in columnTransform)
			{
				if (item.name.Contains("Category") || item.GetComponent<VerticalLayoutGroup>() != null)
				{
					return item;
				}
			}
			return columnTransform;
		}

		private void RenderCategory(CategoryEntry category, Transform container)
		{
			if (categoryPrefab == null)
			{
				Debug.LogError("Category Prefab is not assigned!");
				return;
			}
			GameObject gameObject = UnityEngine.Object.Instantiate(categoryPrefab, container);
			gameObject.name = "Category_" + category.Title;
			TMP_Text componentInChildren = gameObject.GetComponentInChildren<TMP_Text>();
			if (componentInChildren != null)
			{
				string term = category.Title;
				LocalizationMediator.GetTranslation(ref term);
				if (!string.IsNullOrEmpty(term))
				{
					componentInChildren.text = term;
				}
				else
				{
					componentInChildren.text = category.Title;
					if (category.Title.StartsWith("CRD_"))
					{
						Debug.LogWarning("No translation found for category term: " + category.Title);
						componentInChildren.text = category.Title.Replace("CRD_", "").Replace('_', ' ');
					}
				}
				componentInChildren.ForceMeshUpdate();
			}
			Transform transform = FindCreditEntryContainer(gameObject.transform);
			if (category.CreditEntries == null || !(transform != null))
			{
				return;
			}
			foreach (CreditEntry creditEntry in category.CreditEntries)
			{
				RenderCreditEntry(creditEntry, transform);
			}
		}

		private Transform FindCreditEntryContainer(Transform categoryTransform)
		{
			foreach (Transform item in categoryTransform)
			{
				if (item.name.Contains("CreditEntry") || item.GetComponent<VerticalLayoutGroup>() != null)
				{
					return item;
				}
			}
			return categoryTransform;
		}

		private void RenderCreditEntry(CreditEntry credit, Transform container)
		{
			if (creditEntryPrefab == null)
			{
				Debug.LogError("CreditEntry Prefab is not assigned!");
				return;
			}
			GameObject obj = UnityEngine.Object.Instantiate(creditEntryPrefab, container);
			obj.name = "Credit_" + credit.Role;
			TMP_Text[] componentsInChildren = obj.GetComponentsInChildren<TMP_Text>();
			bool flag = false;
			TMP_Text[] array = componentsInChildren;
			foreach (TMP_Text tMP_Text in array)
			{
				if (!flag && tMP_Text.gameObject.name.ToLower().Contains("role"))
				{
					string term = credit.Role;
					LocalizationMediator.GetTranslation(ref term);
					tMP_Text.text = ((!string.IsNullOrEmpty(term)) ? term : credit.Role);
					if (string.IsNullOrEmpty(term) && credit.Role.StartsWith("CRD_RL_"))
					{
						Debug.LogWarning("No translation found for role term: " + credit.Role);
						tMP_Text.text = credit.Role.Replace("CRD_RL_", "").Replace('_', ' ');
					}
					flag = true;
				}
				else if (tMP_Text.gameObject.name.ToLower().Contains("names") && credit.Names != null && credit.Names.Count > 0)
				{
					tMP_Text.text = string.Join("\n", credit.Names);
				}
			}
			if (flag || componentsInChildren.Length == 0)
			{
				return;
			}
			string term2 = credit.Role;
			LocalizationMediator.GetTranslation(ref term2);
			componentsInChildren[0].text = ((!string.IsNullOrEmpty(term2)) ? term2 : credit.Role);
			if (credit.Names != null)
			{
				for (int j = 0; j < Mathf.Min(credit.Names.Count, componentsInChildren.Length - 1); j++)
				{
					componentsInChildren[j + 1].text = credit.Names[j];
				}
			}
		}

		private void ClearContent()
		{
			if (!(contentParent == null))
			{
				for (int num = contentParent.childCount - 1; num >= 0; num--)
				{
					UnityEngine.Object.Destroy(contentParent.GetChild(num).gameObject);
				}
			}
		}

		private void OnValidate()
		{
			if (sessionPrefab == null)
			{
				Debug.LogWarning("Session Prefab is not assigned!", this);
			}
			if (groupPrefab_TwoColumns == null)
			{
				Debug.LogWarning("Two Columns Group Prefab is not assigned!", this);
			}
			if (groupPrefab_SingleColumn == null)
			{
				Debug.LogWarning("Single Column Group Prefab is not assigned!", this);
			}
			if (categoryPrefab == null)
			{
				Debug.LogWarning("Category Prefab is not assigned!", this);
			}
			if (creditEntryPrefab == null)
			{
				Debug.LogWarning("CreditEntry Prefab is not assigned!", this);
			}
			if (mainScrollContainer == null)
			{
				Debug.LogWarning("Main Scroll Container is not assigned!", this);
			}
		}
	}
}
