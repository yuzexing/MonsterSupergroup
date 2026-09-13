using UnityEditor.Build;
using UnityEditor.Build.Reporting;
namespace MonsterSupergroup.NetworkCombat.Editor
{
    public sealed class MenuLocalizationAssets : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report) => GameLocalizationAssets.Validate();
    }
}
