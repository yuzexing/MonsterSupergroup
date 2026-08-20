using AstralShift.HellMaiden.Dialogue;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.Initialization
{
	public class ContextualIntercomLoader : SceneLoader
	{
		// [SerializeField]
		// private ContextualIntercomResolver resolver;

		public override UniTask LoadAsync()
		{
			// resolver.Init();
			return UniTask.CompletedTask;
		}
	}
}
