using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public struct PlayerVisualState
    {
        public NordicMotion Motion;
        public bool FacingLeft;
        public double StartedAt;
        public float Duration;
    }

    /// <summary>Presentation added after existing NetworkBehaviours; never authorizes gameplay.</summary>
    [DefaultExecutionOrder(90)]
    public sealed class NetworkPlayerPresentation : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnVisualChanged))] private PlayerVisualState visual;
        private NordicPlayerAnimator animator;
        private PlayerMovement player;
        private PlayerVisualState lastSent;
        private double nextSend;
        private bool sent;
        public int SentChanges { get; private set; }
        public PlayerVisualState Visual => visual;
        private void Awake() { animator = GetComponentInChildren<NordicPlayerAnimator>(true); player = GetComponent<PlayerMovement>(); }
        public override void OnStartClient() { if (!isOwned) Apply(visual); }
        private void LateUpdate()
        {
            if (!isOwned || animator == null || player == null || !player.IsRuntimeInitialized || NetworkTime.time < nextSend) return;
            var value = new PlayerVisualState {
                Motion = animator.Motion, FacingLeft = animator.FacingLeft, Duration = animator.MotionDuration,
                StartedAt = NetworkTime.time - (Time.timeAsDouble - animator.MotionStartedAt)
            };
            if (sent && value.Motion == lastSent.Motion && value.FacingLeft == lastSent.FacingLeft &&
                System.Math.Abs(value.StartedAt - lastSent.StartedAt) < .02) return;
            sent = true; lastSent = value; nextSend = NetworkTime.time + .05; SentChanges++;
            CmdSetVisual(value);
        }
        [Command]
        private void CmdSetVisual(PlayerVisualState value)
        {
            if (value.Motion > NordicMotion.Revive || !double.IsFinite(value.StartedAt) || !float.IsFinite(value.Duration)) return;
            value.Duration = Mathf.Clamp(value.Duration, 0, 30);
            value.StartedAt = System.Math.Clamp(value.StartedAt, NetworkTime.time - 60, NetworkTime.time);
            visual = value;
        }
        private void OnVisualChanged(PlayerVisualState before, PlayerVisualState after) { if (!isOwned) Apply(after); }
        private void Apply(PlayerVisualState value)
        {
            if (GameplayRuntimeEnvironment.IsDedicatedServer || animator == null) return;
            animator.ApplyRemote(value.Motion, value.FacingLeft, value.Duration, System.Math.Max(0, NetworkTime.time - value.StartedAt), value.StartedAt);
        }
    }
}
