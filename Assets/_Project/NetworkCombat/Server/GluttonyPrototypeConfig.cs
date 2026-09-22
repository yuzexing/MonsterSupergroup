using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Prototypes/Gluttony", fileName = "GluttonyPrototype")]
    public sealed class GluttonyPrototypeConfig : ScriptableObject
    {
        [Tooltip("Enabled by default. The Host prototype panel can disable it for normal-combat comparisons.")]
        public GluttonyParameters parameters = GluttonyParameters.Defaults;
    }
}
