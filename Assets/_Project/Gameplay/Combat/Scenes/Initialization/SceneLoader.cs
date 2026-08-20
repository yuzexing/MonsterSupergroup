using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.Initialization
{
	public abstract class SceneLoader : MonoBehaviour
	{
		public abstract UniTask LoadAsync();
	}
}
