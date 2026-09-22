using UnityEngine;
namespace MonsterSupergroup.NetworkCombat
{
    [CreateAssetMenu(menuName = "MonsterSupergroup/Prototypes/Allure", fileName = "AllurePrototype")]
    public sealed class AllurePrototypeConfig : ScriptableObject
    {
        public AllureParameters parameters = AllureParameters.Defaults;
    }
}
