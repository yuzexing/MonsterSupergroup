using AstralShift.HellMaiden.UI;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class SceneUILoader : SceneLoader
	{
		[FormerlySerializedAs("hud")]
		[SerializeField]
		private SceneUIManager UIPrefab;

		[Header("Card Visuals Factory")]
		[SerializeField]
		private bool releaseCardVisualFactoryCache = true;

		public override async UniTask LoadAsync()
		{
			if (releaseCardVisualFactoryCache)
			{
				CardVisualsFactory.ReleaseVisualDataCache();
				UICardRenderingManager.Instance.DisposeUnusedStaticTextures();
			}
			if ((bool)UIPrefab)
			{
				await Object.Instantiate(UIPrefab).Initialize();
			}
		}
	}
}
