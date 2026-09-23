using System.IO;
using UnityEditor.Build.Profile;
using UnityEngine;
namespace MonsterSupergroup.EditorTools.Tests
{
    public static class BuildProfileVerification
    {
        public static void Snapshot()
        {
            var plan = ProjectBuildResolver.Resolve(BuildProfile.GetActiveBuildProfile(), true);
            File.WriteAllText("Logs/BuildProfilesImplementation/current-plan.json", plan.ToJson());
            Debug.Log("[ProfileSnapshot] " + plan.ContentHash + " / " + plan.InputHash);
        }
    }
}
