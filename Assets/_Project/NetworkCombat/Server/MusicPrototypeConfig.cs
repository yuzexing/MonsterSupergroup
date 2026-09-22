using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Prototypes/Music", fileName = "MusicPrototype")]
    public sealed class MusicPrototypeConfig : ScriptableObject
    {
        public MusicParameters parameters = MusicParameters.Defaults;
    }
}
