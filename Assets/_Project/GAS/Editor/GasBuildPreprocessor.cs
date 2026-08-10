using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace MonsterSupergroup.GAS.Editor
{
    public sealed class GasBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!ModifierRegistryGenerator.IsCurrent())
            {
                throw new BuildFailedException(
                    "The generated GAS registry is stale. Run Tools/MonsterSupergroup/GAS/Rebuild Registry before building.");
            }

            GasValidationIssue firstError = GasAssetValidator.ValidateAllAssets()
                .FirstOrDefault(issue => issue.Severity == GasValidationSeverity.Error);
            if (!string.IsNullOrEmpty(firstError.Message))
            {
                throw new BuildFailedException($"GAS validation failed: {firstError.Message}");
            }
        }
    }
}
