#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MonsterSupergroup.NetworkCombat.Tests
{
    [InitializeOnLoad]
    public static class GluttonyPrototypeTestRunner
    {
        private const string Key="GluttonyPrototype.TestRunPath";
        static GluttonyPrototypeTestRunner() { TestRunnerApi.RegisterTestCallback(new Results()); }
        [MenuItem("Tools/MonsterSupergroup/Prototypes/Gluttony/Run EditMode Tests")]
        public static void EditTests() => Run(TestMode.EditMode,"MonsterSupergroup.NetworkCombat.Tests.GluttonyPrototypeTests");
        [MenuItem("Tools/MonsterSupergroup/Prototypes/Gluttony/Run PlayMode Tests")]
        public static void PlayTests() => Run(TestMode.PlayMode,"MonsterSupergroup.Gameplay.Tests.GluttonyPrototypePlayModeTests");
        [MenuItem("Tools/MonsterSupergroup/Prototypes/Run Stage One PlayMode Tests")]
        public static void StageOnePlayTests() => Run(TestMode.PlayMode,
            "MonsterSupergroup.Gameplay.Tests.PrototypeAbilityPlayModeTests",
            "MonsterSupergroup.Gameplay.Tests.ModifierSelectionTests",
            "MonsterSupergroup.Gameplay.Tests.GluttonyPrototypePlayModeTests");
        [MenuItem("Tools/MonsterSupergroup/Prototypes/Run Stage Two PlayMode Tests")]
        public static void StageTwoPlayTests() => Run(TestMode.PlayMode,
            "MonsterSupergroup.Gameplay.Tests.MusicPrototypePlayModeTests",
            "MonsterSupergroup.Gameplay.Tests.UpgradeOfferDeferralPlayModeTests",
            "MonsterSupergroup.Gameplay.Tests.MusicCombatEffectsPlayModeTests",
            "MonsterSupergroup.Gameplay.Tests.PrototypeAbilityPlayModeTests",
            "MonsterSupergroup.Gameplay.Tests.ModifierSelectionTests",
            "MonsterSupergroup.Gameplay.Tests.GluttonyPrototypePlayModeTests");
        private static void Run(TestMode mode,params string[] testNames)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play Mode and finish compiling before testing.");
            if (!string.IsNullOrEmpty(SessionState.GetString(Key,"")))
                throw new InvalidOperationException("A Gluttony test run is already active.");
            for(int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene changes before running tests.");
            string path="Logs/GluttonyPrototype/"+mode+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Directory.CreateDirectory(path); SessionState.SetString(Key,path);
            File.WriteAllText(path+"/started.txt",string.Join("\n",testNames));
            var api=ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter { testMode=mode, testNames=testNames }));
        }
        private sealed class Results : IErrorCallbacks
        {
            private static string Path => SessionState.GetString(Key,"");
            public void OnError(string message)
            {
                if (string.IsNullOrEmpty(Path)) return;
                File.WriteAllText(Path+"/summary.txt","FAILED TO START: "+message);
                SessionState.EraseString(Key);
            }
            public void RunStarted(ITestAdaptor test) {}
            public void TestStarted(ITestAdaptor test) {}
            public void TestFinished(ITestResultAdaptor result)
            {
                if (string.IsNullOrEmpty(Path) || result.Test.IsSuite) return;
                File.AppendAllText(Path+"/cases.txt",result.FullName+" "+result.ResultState+"\n"+result.Message+"\n");
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (string.IsNullOrEmpty(Path)) return;
                TestRunnerApi.SaveResultToFile(result,Path+"/results.xml");
                File.WriteAllText(Path+"/summary.txt",$"result={result.ResultState} passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} duration={result.Duration}\n");
                SessionState.EraseString(Key);
            }
        }
    }
}
#endif
