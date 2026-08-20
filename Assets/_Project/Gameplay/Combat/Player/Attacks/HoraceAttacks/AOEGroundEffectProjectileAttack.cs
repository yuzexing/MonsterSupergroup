namespace AstralShift.HellMaiden.Player.Attacks.HoraceAttacks
{
	public class AOEGroundEffectProjectileAttack : ProjectileAttack
	{
		protected override void ResolveHitEffect()
		{
			if ((bool)hitEffectResolver)
			{
				((hitEffectResolver as SpawnableHitEffectResolver).HitEffect as GroundEffectAnimationAttackHitEffect).Projectile = this;
				hitEffectResolver.Initialize(_behaviour);
			}
		}
	}
}
