using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public class WaterMarkBehavior : MonoBehaviour
	{
		[SerializeField]
		private TMP_Text waterMarkTxt;

		[SerializeField]
		private bool onlyInDevBuilds = true;

		private void Awake()
		{
			bool isDebugBuild = Debug.isDebugBuild;
			if (onlyInDevBuilds && !isDebugBuild)
			{
				base.gameObject.SetActive(value: false);
			}
			else if (waterMarkTxt != null)
			{
				string companyName = Application.companyName;
				string productName = Application.productName;
				string version = Application.version;
				waterMarkTxt.text = companyName + " • " + productName + " • v" + version;
			}
		}
	}
}
