using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Prototypes/Gluttony", fileName = "GluttonyPrototype")]
    public sealed class GluttonyPrototypeConfig : ScriptableObject
    {
        [Tooltip("Disabled by default. Enable in the Host prototype panel to compare against normal combat.")]
        public GluttonyParameters parameters = GluttonyParameters.Defaults;
    }
}
