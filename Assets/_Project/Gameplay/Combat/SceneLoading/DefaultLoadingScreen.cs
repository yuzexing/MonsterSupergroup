using System.Collections.Generic;
using System.Threading.Tasks;
using AstralShift.HellMaiden.Scenes;
using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.SceneLoading
{
	public class DefaultLoadingScreen : LoadingScreen
	{
		[SerializeField]
		private LoadingScreenLut lut;

		[Header("General Tips & Myths")]
		public List<string> tipsTxtsKeys;

		public List<string> mythTxtsKeys;

		private GameObject _activeInstance;

		public override Task Run()
		{
			SceneEnum scene = SceneMaster.Instance.NextScene.ConvertToSceneEnum();
			GameObject original = lut.Get(scene);
			_activeInstance = Object.Instantiate(original, base.transform);
			_activeInstance.GetComponent<LoadingScreenBackground>().SetLoreText(BuildText(scene));
			base.gameObject.SetActive(value: true);
			return Task.CompletedTask;
		}

		public override Task Stop()
		{
			if (_activeInstance != null)
			{
				Object.Destroy(_activeInstance);
			}
			base.gameObject.SetActive(value: false);
			return Task.CompletedTask;
		}

		private string BuildText(SceneEnum scene)
		{
			List<string> list = Combine(tipsTxtsKeys, lut.GetTips(scene));
			List<string> list2 = Combine(mythTxtsKeys, lut.GetMyths(scene));
			bool flag = Random.value < 0.5f;
			List<string> list3 = (flag ? list : list2);
			if (list3.Count == 0)
			{
				list3 = (flag ? list2 : list);
			}
			if (list3.Count == 0)
			{
				return "";
			}
			return LocalizationMediator.GetTranslation(list3[Random.Range(0, list3.Count)]);
		}

		private static List<string> Combine(List<string> general, List<string> specific)
		{
			List<string> list = new List<string>();
			if (specific != null)
			{
				list.AddRange(specific);
			}
			if (general != null)
			{
				list.AddRange(general);
			}
			return list;
		}
	}
}
