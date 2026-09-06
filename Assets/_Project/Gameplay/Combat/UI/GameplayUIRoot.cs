using AstralShift.HellMaiden.UI;
using Cysharp.Threading.Tasks;

namespace MonsterSupergroup.Gameplay.UI
{
    /// <summary>
    /// UI initialization boundary, called by GameplayUILoader. HUD modules and future
    /// menu flows live under this root and have their own responsibilities.
    /// </summary>
    public sealed class GameplayUIRoot : SceneUIManager
    {
        public override UniTask Initialize()
        {
            // LocalPlayerUIBinder handles either player/UI spawn order.
            return UniTask.CompletedTask;
        }
    }
}
