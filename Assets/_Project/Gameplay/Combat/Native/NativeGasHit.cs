using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using UnityEngine;
using LegacyDamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>
    /// Gameplay-assembly boundary used by HellMaiden hitboxes. It carries the
    /// already-frozen New GAS attack and never contains legacy damage results.
    /// </summary>
    public readonly struct NativeGasHit
    {
        public NativeGasHit(
            Vector2 attackPosition,
            WeaponBehaviour presentationWeapon,
            WeaponRuntimeBehaviour runtime,
            AttackSnapshot attack,
            LegacyDamageType presentationDamageType,
            KnockbackSettings knockbackPresentation)
        {
            AttackPosition = attackPosition;
            PresentationWeapon = presentationWeapon;
            Runtime = runtime;
            Attack = attack;
            PresentationDamageType = presentationDamageType;
            KnockbackPresentation = knockbackPresentation;
        }

        public Vector2 AttackPosition { get; }
        public WeaponBehaviour PresentationWeapon { get; }
        public WeaponRuntimeBehaviour Runtime { get; }
        public AttackSnapshot Attack { get; }
        public LegacyDamageType PresentationDamageType { get; }
        public KnockbackSettings KnockbackPresentation { get; }
    }

    public interface INativeGasDamageable
    {
        bool ResolveNativeGasHit(NativeGasHit hit);
    }
}
