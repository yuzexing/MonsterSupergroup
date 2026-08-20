using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public abstract class SceneUIManager : MonoBehaviour
	{
		public abstract UniTask Initialize();
	}
}
