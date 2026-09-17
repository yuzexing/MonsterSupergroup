using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Offline instrumentation of an explicitly isolated runtime copy. Never run on the source package.
static class PatchRuntime
{
    static List<string> report = new List<string>();
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> roots)
    {
        foreach (var t in roots) { yield return t; foreach (var child in Types(t.NestedTypes)) yield return child; }
    }
    static void ReplaceBody(MethodDefinition m, params Instruction[] code)
    {
        m.Body = new MethodBody(m);
        foreach (var i in code) m.Body.Instructions.Add(i);
        report.Add("replace isolation: " + m.FullName);
    }
    static MethodReference Probe(ModuleDefinition m, ModuleDefinition probe, string name, int args)
    {
        return m.ImportReference(probe.Types.Single(t => t.Name == "RecoveryProbe").Methods.Single(x => x.Name == name && x.Parameters.Count == args));
    }
    static void Hook(MethodDefinition m, ModuleDefinition probe, string name, bool end)
    {
        if (!m.HasBody) return;
        var il = m.Body.GetILProcessor();
        var points = end ? m.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray() : new[] {m.Body.Instructions[0]};
        foreach (var point in points)
        {
            il.InsertBefore(point, Instruction.Create(m.IsStatic && m.ReturnType.FullName != "System.Void" ? OpCodes.Dup : OpCodes.Ldarg_0));
            il.InsertBefore(point, Instruction.Create(OpCodes.Ldstr, m.DeclaringType.Name + "." + m.Name));
            il.InsertBefore(point, Instruction.Create(OpCodes.Call, Probe(m.Module, probe, name, 2)));
        }
        report.Add("observe: " + m.FullName);
    }
    public static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("PatchRuntime <original Managed> <isolated Managed> <probe dll>");
        var destination = Path.GetFullPath(args[1]);
        if (!destination.Replace('\\','/').Contains("/Logs/EnemyRecovery/runtime/")) throw new InvalidOperationException("Refusing to patch outside recovery runtime.");
        var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(args[0]);
        using (var probe = ModuleDefinition.ReadModule(args[2]))
        foreach (var path in Directory.GetFiles(args[0], "*.dll"))
        {
            // All assemblies are read from the untouched original on each build, making patching idempotent.
            using (var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters {AssemblyResolver = resolver}))
            {
                var module = asm.MainModule; bool changed = false;
                foreach (var type in Types(module.Types))
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        var call = instruction.Operand as MethodReference;
                        if (call == null) continue;
                        if (call.DeclaringType.FullName == "UnityEngine.PlayerPrefs" && type.FullName != "UnityEngine.PlayerPrefs")
                        {
                            var replacement = probe.Types.Single(t => t.Name == "RecoveryProbe").Methods.FirstOrDefault(x => x.Name == "Prefs" + call.Name &&
                                x.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(call.Parameters.Select(p => p.ParameterType.FullName)));
                            if (replacement == null) throw new InvalidOperationException("Unisolated PlayerPrefs API: " + call.FullName);
                            instruction.Operand = module.ImportReference(replacement); changed = true;
                            report.Add("redirect preference: " + method.FullName + " -> " + call.Name);
                        }
                        else if (call.DeclaringType.FullName == "UnityEngine.Application" && call.Name == "get_persistentDataPath")
                        { instruction.Operand = Probe(module, probe, "IsolatedDataPath", 0); changed = true; }
                    }
                }
                if (asm.Name.Name == "Assembly-CSharp")
                {
                    var game = module.Types.Single(t => t.FullName == "AstralShift.HellMaiden.GameDirector");
                    var awake = game.Methods.Single(m => m.Name == "Awake");
                    var first = awake.Body.Instructions[0];
                    awake.Body.GetILProcessor().InsertBefore(first, Instruction.Create(OpCodes.Call, Probe(module, probe, "Install", 0)));
                    report.Add("bootstrap: GameDirector.Awake prefix");
                    var save = module.Types.Single(t => t.FullName == "Assets.Scripts.SaveSystem.Porting_Helpers.WindowsSaveSystem");
                    ReplaceBody(save.Methods.Single(m => m.Name == "GetSavedGamesFolderPath"), Instruction.Create(OpCodes.Call, Probe(module, probe, "IsolatedDataPath", 0)), Instruction.Create(OpCodes.Ret));
                    var steam = module.Types.Single(t => t.FullName == "AstralShift.Helpers.Steam.SteamManager");
                    ReplaceBody(steam.Methods.Single(m => m.Name == "Awake"), Instruction.Create(OpCodes.Ret));
                    ReplaceBody(steam.Methods.Single(m => m.Name == "get_Initialized"), Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Ret));
                    // Use the existing local profile adapter; this avoids calling Steam account/stat APIs.
                    var local = module.Types.Single(t => t.FullName == "AstralShift.HellMaiden.ProfileData.LocalProfileData");
                    foreach (var t in Types(module.Types)) foreach (var m in t.Methods.Where(m => m.HasBody))
                    foreach (var i in m.Body.Instructions)
                    {
                        var constructor = i.Operand as MethodReference;
                        if (i.OpCode == OpCodes.Newobj && constructor != null && constructor.DeclaringType.FullName == "AstralShift.HellMaiden.ProfileData.SteamProfileData")
                            i.Operand = local.Methods.Single(x => x.IsConstructor && !x.IsStatic && x.Parameters.Count == 0);
                    }
                    var factory = module.Types.Single(t => t.FullName == "AstralShift.HellMaiden.AI.Enemy.EnemyFactory");
                    Hook(factory.Methods.Single(m => m.Name == "CreateEnemy"), probe, "Born", true);
                    foreach (var t in module.Types.Where(t => t.Namespace == "AstralShift.HellMaiden.AI.Enemy"))
                    foreach (var m in t.Methods.Where(m => !m.IsStatic && m.HasBody))
                    {
                        if ((t.Name == "EnemyAttack" && new[] {"AttackWarningEnter","AttackWarningExit","AttackEnter","AttackExit","RecoveryEnter","RecoveryExit"}.Contains(m.Name)) ||
                            (t.Name == "BulletProjectile" && new[] {"Fire","FireEnter","FireExit","HitEnter","ExpireEnter","EndEnter"}.Contains(m.Name)) ||
                            (t.Name == "EnemyAttackExplosion" && m.Name == "AttackWarningEnter") ||
                            (t.Name == "EnemyAttackMelee" && m.Name == "AttackExit") ||
                            (t.Name == "EnemyExplosionAttackVFX" && new[] {"Trigger","Stop","OnEnd"}.Contains(m.Name))) Hook(m, probe, "Event", false);
                    }
                    changed = true;
                    foreach(var t in module.Types.Where(t => t.Namespace == "AstralShift.HellMaiden.Player.Attacks"))
                    foreach(var m in t.Methods.Where(m => !m.IsStatic && m.HasBody))
                        if(new[]{"PlayStartAnimation","PlayAttackAnimation","PlayEndAnimation","PlayLaunchSound","PlayHitSound","PlayLaunchedLoopSound","OnHit","Dispose"}.Contains(m.Name))
                            Hook(m,probe,"AudioComponent",false);
                }
                if (asm.Name.Name == "FMODUnity")
                {
                    var t=module.Types.Single(x=>x.FullName=="FMOD.Studio.EventInstance");
                    foreach(var m in t.Methods.Where(x=>x.HasBody && x.Name=="setParameterByID"))
                    {
                        var il=m.Body.GetILProcessor();var first=m.Body.Instructions[0];
                        il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));il.InsertBefore(first,Instruction.Create(OpCodes.Ldobj,t));
                        il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_1));il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_2));
                        il.InsertBefore(first,Instruction.Create(OpCodes.Call,Probe(module,probe,"AudioParameter",3)));changed=true;
                    }
                    foreach(var m in t.Methods.Where(x=>x.HasBody && new[]{"start","stop","release","setParameterByName"}.Contains(x.Name)))
                    {
                        var il=m.Body.GetILProcessor();var first=m.Body.Instructions[0];
                        il.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));
                        il.InsertBefore(first,Instruction.Create(OpCodes.Ldobj,t));
                        il.InsertBefore(first,Instruction.Create(OpCodes.Ldstr,m.Name));
                        il.InsertBefore(first,m.Name=="setParameterByName"?Instruction.Create(OpCodes.Ldarg_1):Instruction.Create(OpCodes.Ldstr,""));
                        il.InsertBefore(first,m.Name=="setParameterByName"?Instruction.Create(OpCodes.Ldarg_2):Instruction.Create(OpCodes.Ldc_R4,0f));
                        il.InsertBefore(first,Instruction.Create(OpCodes.Call,Probe(module,probe,"AudioEvent",4)));
                        report.Add("observe audio: "+m.FullName);changed=true;
                    }
                }
                if (changed) asm.Write(Path.Combine(destination, Path.GetFileName(path)));
                else File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), true);
            }
        }
        File.Copy(args[2], Path.Combine(destination, Path.GetFileName(args[2])), true);
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(args[2]), "patch-report.txt"), report);
        Console.WriteLine("Patched isolated runtime. Operations: " + report.Count);
        return 0;
    }
}
