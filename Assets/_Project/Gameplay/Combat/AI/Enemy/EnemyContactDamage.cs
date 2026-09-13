using AstralShift.HellMaiden.Interactions;
using AstralShift.QTI.Triggers;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
    /// <summary>Optional contact geometry, independent of the active attack slot.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyContactDamage : MonoBehaviour
    {
        [SerializeField] private bool contactEnabled = true;
        [SerializeField] private PlayerDamageInteraction damageInteraction;
        private EnemyController controller;
        private bool runtimeReady;

        public bool ContactEnabled => contactEnabled;
        public PlayerDamageInteraction DamageInteraction => damageInteraction;
        public bool IsActive => damageInteraction != null && damageInteraction.gameObject.activeInHierarchy;

        public void Configure(PlayerDamageInteraction interaction, bool enabled)
        {
            damageInteraction = interaction;
            contactEnabled = enabled;
        }

        public void SetContactEnabled(bool value)
        {
            contactEnabled = value;
            Refresh();
        }

        public void Bind(EnemyController owner)
        {
            controller = owner;
            if (damageInteraction == null) return;
            damageInteraction.FlushPendingCollisionsOnDisable = false;
            damageInteraction.enemyStats = owner.stats;
            foreach (var trigger in damageInteraction.GetComponents<InteractionTrigger>())
                trigger.interaction = damageInteraction;
        }

        public void SetRuntimeReady(bool value)
        {
            runtimeReady = value;
            Refresh();
        }

        private void Awake() => SetRuntimeReady(false);
        private void OnEnable() => Refresh();
        private void OnDisable() => SetGeometryActive(false);

        private void Refresh() => SetGeometryActive(isActiveAndEnabled && contactEnabled &&
            runtimeReady && controller != null && controller.IsAlive);

        private void SetGeometryActive(bool active)
        {
            if (damageInteraction == null) return;
            // OnDisable on legacy interactions otherwise flushes pending contacts.
            if (!active) damageInteraction.DiscardPendingCollisions();
            if (damageInteraction.gameObject.activeSelf != active)
                damageInteraction.gameObject.SetActive(active);
        }
    }
}
