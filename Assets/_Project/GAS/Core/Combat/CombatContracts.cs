namespace MonsterSupergroup.GAS
{
    public interface IRandomSource
    {
        /// <summary>Returns a value in the half-open interval [0, 1).</summary>
        float Next01();
    }

    public interface ICombatSource
    {
        uint CombatId { get; }
    }

    public interface IDamageReceiver
    {
        bool IsAlive { get; }

        /// <summary>Applies requested damage and returns the damage actually accepted by the receiver.</summary>
        DamageInfo ReceiveDamage(DamageInfo requestedDamage);
    }

    public interface IStatusReceiver
    {
        StatusApplicationResult ApplyStatus(StatusApplication application);
    }

    public interface ICombatTarget : IDamageReceiver, IStatusReceiver
    {
    }

    public interface IWeaponRuntime : ICombatSource
    {
        WeaponBehaviourStats Stats { get; }
    }
}
