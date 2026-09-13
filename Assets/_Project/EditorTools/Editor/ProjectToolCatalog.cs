using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MonsterSupergroup.EditorTools
{
    [Serializable]
    public sealed class ProjectToolDescriptor
    {
        public string id, name, category, description, audience, impact, output, method, script, asset, profile, condition;
        public string[] parameters = Array.Empty<string>();
        public bool writesAssets, externalSource, graphics, interactive, asynchronous, maintenance;
        public string[] oldMenus = Array.Empty<string>();
        public string source;
    }

    [Serializable]
    public sealed class ProjectBuildProfile
    {
        public string id, name, output;
        public string[] scenes, defines, validations;
        public bool development = true, testAssemblies = true;
    }

    [Serializable]
    public sealed class ProjectToolManifest
    {
        public int version;
        public ProjectToolDescriptor[] tools;
        public ProjectBuildProfile[] builds;
    }

    public static class ProjectToolCatalog
    {
        public const string ManifestPath = "docs/editor-tools/catalog.json";
        public const string DocumentationPath = "docs/editor-tools/README.md";
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static ProjectToolManifest Load()
        {
            var manifest = JsonUtility.FromJson<ProjectToolManifest>(File.ReadAllText(Path.Combine(ProjectRoot, ManifestPath)));
            if (manifest?.tools == null || manifest.builds == null) throw new InvalidDataException("工具清单缺失或损坏。");
            if (manifest.tools.GroupBy(t => t.id).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() != 1) ||
                manifest.builds.GroupBy(t => t.id).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() != 1))
                throw new InvalidDataException("工具或构建配置 ID 重复/为空。");
            return manifest;
        }
        public static ProjectToolDescriptor Find(string id) => Load().tools.FirstOrDefault(t => t.id == id)
            ?? throw new ArgumentException("未知工具 ID: " + id);

        // Domain bindings live in the shared catalog; the executor does not reference test assemblies.
        public static MethodInfo Resolve(string binding)
        {
            int split = binding.LastIndexOf('.');
            if (split < 0) throw new ArgumentException("Invalid tool binding: " + binding);
            string typeName = binding.Substring(0, split), methodName = binding.Substring(split + 1);
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName, false)).FirstOrDefault(t => t != null)
                ?? throw new TypeLoadException(typeName);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(m => m.Name == methodName).ToArray();
            // Importers also have internal Import(string) helpers. The command is the no-argument entry.
            return methods.SingleOrDefault(m => m.GetParameters().Length == 0) ??
                methods.SingleOrDefault(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)) ??
                throw new MissingMethodException("Tool entry must take no arguments or an explicit asset path: " + binding);
        }

        public static void Validate()
        {
            var manifest = Load();
            foreach (var tool in manifest.tools)
            {
                if (string.IsNullOrWhiteSpace(tool.description) || string.IsNullOrWhiteSpace(tool.impact))
                    throw new InvalidDataException("工具说明不完整: " + tool.id);
                if (!string.IsNullOrEmpty(tool.method)) Resolve(tool.method);
                if (!string.IsNullOrEmpty(tool.script) && !File.Exists(tool.script)) throw new FileNotFoundException(tool.script);
            }
            foreach (var build in manifest.builds) ProjectBuildService.ValidateProfile(build);
            Debug.Log($"[ProjectTools] {manifest.tools.Length} tools / {manifest.builds.Length} build profiles validated.");
        }
    }
}
