using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
class DumpOriginalIL
{
    static void Main(string[] args)
    {
        using(var module=ModuleDefinition.ReadModule(args[0]))
        using(var writer=new StreamWriter(args[1]))
        foreach(var type in module.Types.Where(t=>t.Namespace=="AstralShift.HellMaiden.AI.Enemy" || t.Name=="PlayerMovement" || t.Name=="ThornsEnemyAttack"))
        {
            if(!new[]{"EnemyAnimator","MultipleAttackAnimator","EnemyAttack","EnemyAttackMelee","EnemyController","EnemyFactory","BulletProjectile","EnemyExplosionAttackVFX","EnemyAttackExplosion","SequenceEnemyAttack","EnemyAttackDash","EnemyProjectileAttack","ThornsEnemyAttack","PlayerMovement"}.Contains(type.Name))continue;
            foreach(var method in type.Methods.Where(m=>m.HasBody))
            {
                writer.WriteLine("\n"+method.FullName);
                foreach(var instruction in method.Body.Instructions)writer.WriteLine(instruction.ToString());
            }
        }
    }
}
