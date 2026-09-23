using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;
using MonsterSupergroup.Builds;
using Debug = UnityEngine.Debug;

namespace MonsterSupergroup.EditorTools
{
    public static class ProjectBuildService
    {
        public static string ActiveProfile => ActivePlan?.Purpose;
        public static ResolvedProjectBuild ActivePlan { get; private set; }
        public static string LastLaunchError { get; private set; }
        public static void ValidateProfile(ProjectBuildProfile profile)
        {
            if (profile.scenes == null || profile.scenes.Length == 0) throw new InvalidDataException("构建未配置场景：" + profile.id);
            foreach (string scene in profile.scenes)
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null) throw new FileNotFoundException("场景缺失：" + scene);
        }
        public static void AssertReady(BuildProfile profile)
        {
            if (ActivePlan != null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
                throw new BuildFailedException("请停止 Play Mode／当前构建，等待编译和导入完成。");
            if (BuildProfile.GetActiveBuildProfile() != profile) throw new BuildFailedException("请先显式激活所选 Profile，并等待编译完成；构建不会自动切换配置。");
            if (AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(profile)).Any(EditorUtility.IsDirty)) throw new BuildFailedException("请先保存 Profile 和业务配置。");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new BuildFailedException("请先保存或关闭未保存的场景。");
        }
        public static string Build(BuildProfile profile, BuildExecutionRequest request = null)
        {
            request ??= new BuildExecutionRequest();
            LastLaunchError = null; ProjectBuildIdentity.ResetLastInfo();
            string guid = profile == null ? null : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
            using var evidence = ProjectBuildInputEvidence.Start(ProjectToolCatalog.ProjectRoot, guid,
                profile == null ? null : AssetDatabase.GetAssetPath(profile), message => Debug.LogWarning("[BuildInputEvidence] " + message));
            return Build(profile, request, guid, evidence);
        }
        private static string Build(BuildProfile profile, BuildExecutionRequest request, string guid, ProjectBuildInputEvidence evidence)
        {
            if (!string.IsNullOrEmpty(guid)) ProjectBuildResults.Invalidate(guid);
            AssertReady(profile);
            var plan = ProjectBuildResolver.Resolve(profile, true, evidence, ProjectBuildInputEvidence.InitialPlan);
            ProjectBuildResolver.ValidateDefines(plan);
            ProjectBuildResolver.ValidateExecution(plan, request);
            string requested = string.IsNullOrWhiteSpace(request.output) ? "Builds/" + profile.name + "/MonsterSupergroup.exe" : request.output;
            if (!requested.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("输出必须是 .exe 文件。");
            var info = ProjectBuildIdentity.Capture(plan.Recipe, plan.Kind, plan.Development);
            evidence?.BindBuildId(info.BuildId);
            info.SetConfiguration(plan.Purpose, plan.Network.ToString(), plan.Distribution.ToString(), plan.Diagnostics.ToString(), plan.Recipe.testAssemblies, plan.Tools, plan.Evidence);
            info.SetProfile(plan.ProfileGuid, plan.ProfilePath, plan.ContentHash, plan.InputHash);
            string parent = Path.GetDirectoryName(Path.GetFullPath(requested));
            string finalDirectory = Path.Combine(parent, info.ArtifactName);
            string stage = Path.Combine(parent, ".staging", info.ArtifactName);
            string path = Path.Combine(stage, Path.GetFileName(requested));
            string finalPath = Path.Combine(finalDirectory, Path.GetFileName(requested));
            if (Directory.Exists(stage) || Directory.Exists(finalDirectory)) throw new IOException("唯一构建目录已存在，拒绝覆盖。");
            var before = ProjectToolRunner.CaptureAssets();
            ProjectBuildTransaction transaction = null;
            BuildReport report = null;
            var errors = new List<Exception>();
            bool published = false;
            try
            {
                transaction = new ProjectBuildTransaction();
                foreach (string validation in plan.Recipe.validations) ProjectToolRunner.InvokeReadOnly(validation);
                Directory.CreateDirectory(stage);
                File.WriteAllText(Path.Combine(stage, "build-plan.json"), plan.ToJson());
                File.WriteAllText(Path.Combine(stage, "build-request.json"), JsonUtility.ToJson(request, true));
                ActivePlan = plan;
                ProjectBuildIdentity.Begin(info, path);
                Debug.Log("[ProjectBuild] " + info.Display + "\n" + plan.Summary);
                evidence?.CaptureFile(ProjectBuildInputEvidence.BeforeUnity);
                try
                {
                    report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions {
                        buildProfile = profile, locationPathName = path,
                        options = (plan.Recipe.testAssemblies ? BuildOptions.IncludeTestAssemblies : 0) |
                            (request.cleanBuildCache ? BuildOptions.CleanBuildCache : 0) | BuildOptions.StrictMode
                    });
                }
                finally { evidence?.CaptureFile(ProjectBuildInputEvidence.AfterUnity); }
                if (report == null || report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Unity 构建未成功：" + report?.summary.result);
            }
            catch (Exception e) { errors.Add(e); }
            finally
            {
                // Run every cleanup, even when Unity skipped its post-build callbacks.
                Try(() => ProjectBuildIdentity.Cleanup(), errors);
                evidence?.CaptureFile(ProjectBuildInputEvidence.AfterIdentityCleanup);
                if (transaction != null)
                {
                    Try(transaction.Dispose, errors);
                    evidence?.CaptureFile(ProjectBuildInputEvidence.AfterTmpRestore);
                }
                Try(() => ProjectToolRunner.AssertAssetsUnchanged(before), errors);
            }
            try
            {
                if (errors.Count != 0) throw new AggregateException("构建或恢复失败。", errors);
                var after = ProjectBuildResolver.Resolve(profile, true, evidence, ProjectBuildInputEvidence.FinalPlan);
                if (after.ContentHash != plan.ContentHash || after.InputHash != plan.InputHash)
                {
                    File.WriteAllText(Path.Combine(stage, "build-plan-after.json"), after.ToJson());
                    throw new BuildFailedException($"构建期间配置或工程输入改变，本次不发布。Content={plan.ContentHash == after.ContentHash}, Input={plan.InputHash == after.InputHash}；差异快照已写入失败目录。");
                }
                ProjectBuildIdentity.Complete(report);
                ProjectBuildPackage.Validate(path, plan, info);
                Directory.Move(stage, finalDirectory);
                ProjectBuildResults.Save(plan, finalPath, info);
                ProjectBuildIdentity.SetLastInfoPath(finalPath);
                published = true;
            }
            finally
            {
                try
                {
                    if (!published)
                    {
                        ProjectBuildResults.Invalidate(guid);
                        ProjectBuildIdentity.InvalidateOutput(path);
                        ProjectBuildIdentity.InvalidateOutput(finalPath);
                        string failed = Directory.Exists(finalDirectory) ? finalDirectory : stage;
                        if (Directory.Exists(failed))
                        {
                            string quarantine = Path.Combine(parent, ".failed", info.ArtifactName);
                            Directory.CreateDirectory(Path.GetDirectoryName(quarantine));
                            Directory.Move(failed, quarantine);
                        }
                    }
                }
                finally { ProjectBuildIdentity.ResetContext(); ActivePlan = null; }
            }
            if (request.runAfterBuild)
            {
                try { Process.Start(new ProcessStartInfo(finalPath) { WorkingDirectory = finalDirectory, UseShellExecute = true }); }
                catch (Exception e) { LastLaunchError = e.Message; Debug.LogWarning("构建成功，但 Player 启动失败：" + e.Message); }
            }
            Debug.Log("[ProjectBuild] Success: " + finalPath);
            return finalPath;
        }
        private static void Try(Action action, List<Exception> errors) { try { action(); } catch (Exception e) { errors.Add(e); } }
        public static string Build(string profileId, string output = null, bool scriptsOnly = false, string buildKind = null,
            string development = null, bool uniqueOutput = false, string network = null, string distribution = null, string diagnostics = null) =>
            throw new BuildFailedException(ProjectBuildResolver.MigrationMessage);
        public static void Legacy(string profile, string output = null, bool scriptsOnly = false) => throw new BuildFailedException(ProjectBuildResolver.MigrationMessage);
        public static void LegacyBatch(string profile)
        {
            try { Legacy(profile); }
            catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
        }
    }

    [Serializable]
    public sealed class ProjectBuildResultPointer
    {
        public bool success;
        public string profileGuid, profilePath, recipe, executable, buildInfoPath, buildId, contentHash, inputHash;
    }
    public static class ProjectBuildResults
    {
        public static string PathFor(string guid)
        {
            if (guid == null || guid.Length != 32 || !guid.All(Uri.IsHexDigit)) throw new ArgumentException("构建结果需要原生 Profile GUID；旧配方指针已停用。");
            return Path.Combine(ProjectToolCatalog.ProjectRoot, "Library/ProjectTools/BuildResults", guid + ".json");
        }
        public static void Invalidate(params string[] guids)
        {
            foreach (string guid in guids.Distinct()) { string path = PathFor(guid); if (File.Exists(path)) File.Delete(path); }
        }
        public static void Save(ResolvedProjectBuild plan, string executable, BuildInfo info)
        {
            var result = new ProjectBuildResultPointer { success = true, profileGuid = plan.ProfileGuid, profilePath = plan.ProfilePath, recipe = plan.Purpose,
                executable = Path.GetFullPath(executable), buildInfoPath = ProjectBuildIdentity.InfoPath(executable), buildId = info.BuildId, contentHash = plan.ContentHash, inputHash = plan.InputHash };
            string path = PathFor(plan.ProfileGuid);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path + ".tmp", JsonUtility.ToJson(result, true), new System.Text.UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            }
            catch { Invalidate(plan.ProfileGuid); throw; }
            finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
        }
    }
}
