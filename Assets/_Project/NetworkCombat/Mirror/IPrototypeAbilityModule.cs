using MonsterSupergroup.Gameplay.Combat;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Selection routes new actions; each module owns its effects and cooldowns.</summary>
    public interface IPrototypeAbilityModule
    {
        PrototypeAbilityId AbilityId { get; }
        string StatusText { get; }
        bool TryHandleAction(PrototypeAbilityAction action);
        bool TryHandleOngoingAction(PrototypeAbilityAction action);
        void ServerCancelEffects();
    }
}
