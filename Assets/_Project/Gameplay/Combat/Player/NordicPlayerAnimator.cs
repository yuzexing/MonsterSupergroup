using Animancer;
using AstralShift.HellMaiden.Player;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    public enum NordicMotion : byte { Idle, Walk, Dash, Hurt, Dead, Revive }

    /// <summary>Axeldor's visual state only. Combat timing remains in the existing player runtime.</summary>
    public sealed class NordicPlayerAnimator : PlayerAnimator
    {
        [SerializeField] private Transform facingRoot;
        [SerializeField] private SpriteRenderer bodySprite;
        [SerializeField] private SpriteRenderer[] bodyParts;
        [SerializeField] private AnimationClip idleClip, walkClip, dashClip, dieClip;
        private Color[] originalColors;
        private PlayerMovement player;
        private CombatantBehaviour combatant;
        private AnimancerState current;
        private bool sampledHealth, alive = true, initialized;
        private int health;
        private float flashUntil;
        private double remoteStartedAt = double.NaN;
        public NordicMotion Motion { get; private set; }
        public bool FacingLeft { get; private set; } = true;
        public double MotionStartedAt { get; private set; }
        public float MotionDuration { get; private set; }
        public SpriteRenderer BodySprite => bodySprite;
        public bool IsDead => sampledHealth && !alive;

        public void Configure(Transform facing, SpriteRenderer body, SpriteRenderer[] parts, AnimationClip idle, AnimationClip walk, AnimationClip dash, AnimationClip die)
        {
            facingRoot = facing; bodySprite = body; bodyParts = parts;
            idleClip = idle; walkClip = walk; dashClip = dash; dieClip = die;
        }
        protected override void OnEnable()
        {
            animancer = GetComponent<AnimancerComponent>();
            player = GetComponentInParent<PlayerMovement>();
            combatant = GetComponentInParent<CombatantBehaviour>();
            if (bodyParts == null || idleClip == null) return; // Editor construction before serialized binding.
            originalColors = new Color[bodyParts.Length];
            for (int i = 0; i < bodyParts.Length; i++) originalColors[i] = bodyParts[i].color;
            initialized = false; sampledHealth = false; flashUntil = 0;
            if (combatant != null) combatant.HealthChanged += OnHealthChanged;
            Play(NordicMotion.Idle, 0, true);
        }
        private void OnHealthChanged(int currentHealth, int maximumHealth)
        {
            if (sampledHealth && currentHealth < health) flashUntil = Time.time + .4f;
            health = currentHealth;
        }
        private void Update()
        {
            if (combatant == null || player == null || !player.IsRuntimeInitialized) return;
            bool nowAlive = combatant.IsAlive;
            if (!sampledHealth)
            {
                sampledHealth = true; alive = nowAlive; health = combatant.CurrentHealth;
                if (!alive) { Play(NordicMotion.Dead, 1, true); current.Time = dieClip.length; current.Speed = 0; }
            }
            else
            {
                if (health > combatant.CurrentHealth) flashUntil = Time.time + .4f;
                health = combatant.CurrentHealth;
                if (nowAlive != alive)
                {
                    alive = nowAlive;
                    Play(alive ? NordicMotion.Revive : NordicMotion.Dead, 1, true);
                }
            }
            if (Motion == NordicMotion.Dead && current != null && current.Time >= dieClip.length)
            {
                bool finished = current.Speed != 0;
                current.Time = dieClip.length; current.Speed = 0;
                if (finished) player.DeadAnimationFinished();
            }
            if (Motion == NordicMotion.Revive && current != null && current.Time <= 0) Play(NordicMotion.Idle);
            float remaining = flashUntil - Time.time;
            Color tint = remaining <= 0 ? Color.white : (Mathf.FloorToInt((.4f - remaining) / .1f) % 2 == 0 ? Color.red : Color.white);
            if (originalColors != null)
                for (int i = 0; i < bodyParts.Length; i++) bodyParts[i].color = originalColors[i] * tint;
        }
        private void Face(float x)
        {
            if (x == 0 || facingRoot == null) return;
            FacingLeft = x < 0;
            facingRoot.localScale = new Vector3(FacingLeft ? 1 : -1, 1, 1);
        }
        private void Play(NordicMotion motion, float duration = 0, bool force = false)
        {
            if (animancer == null || idleClip == null || (!force && initialized && Motion == motion)) return;
            initialized = true; Motion = motion; MotionDuration = duration; MotionStartedAt = Time.timeAsDouble;
            AnimationClip clip = motion == NordicMotion.Walk ? walkClip : motion == NordicMotion.Dash ? dashClip :
                motion == NordicMotion.Dead || motion == NordicMotion.Revive ? dieClip : idleClip;
            current = animancer.Play(clip, 0);
            current.Time = motion == NordicMotion.Revive ? clip.length : 0;
            current.Speed = motion == NordicMotion.Revive ? -1 : motion == NordicMotion.Dash && duration > .001f ? clip.length / duration : 1;
        }
        public override void Movement(float v, float x, float y)
        {
            if (IsDead) return;
            Face(x);
            // Revival is visual only and yields immediately to movement.
            if (Motion == NordicMotion.Revive && v < .2f) return;
            Play(v >= .2f ? NordicMotion.Walk : NordicMotion.Idle);
        }
        public override void Run(float x, float y) => Movement(1, x, y);
        public override void Idle(float x, float y) => Movement(0, x, y);
        public override void Dash(float x, float y) { if (!IsDead) { Face(x); Play(NordicMotion.Dash, player != null ? player.TotalDashTime : .25f); } }
        public override void Hurt(float x, float y) { if (!IsDead) { Face(x); Play(NordicMotion.Hurt); } }
        public override void Dead(float x, float y) { Face(x); Play(NordicMotion.Dead, 1); }
        public override UniTask Teleport() => UniTask.CompletedTask;
        public override void ResetAnimancer() { blockAnimations = false; Play(NordicMotion.Idle, 0, true); }
        public void ApplyRemote(NordicMotion motion, bool left, float duration, double elapsed, double startedAt)
        {
            Face(left ? -1 : 1);
            if (IsDead && motion != NordicMotion.Dead) return;
            bool changed = !initialized || Motion != motion || double.IsNaN(remoteStartedAt) || System.Math.Abs(remoteStartedAt-startedAt) > .02;
            remoteStartedAt = startedAt;
            Play(motion, duration, changed);
            if (changed && current != null)
            {
                double time = elapsed * current.Speed;
                if (motion == NordicMotion.Revive) time += dieClip.length;
                current.Time = (float)(motion == NordicMotion.Dead || motion == NordicMotion.Dash || motion == NordicMotion.Revive
                    ? System.Math.Clamp(time, 0, current.Length) : time);
            }
        }
        public new void OnDisable()
        {
            if (combatant != null) combatant.HealthChanged -= OnHealthChanged;
            if (originalColors != null) for (int i = 0; i < bodyParts.Length; i++) if (bodyParts[i] != null) bodyParts[i].color = originalColors[i];
            if (animancer != null) base.OnDisable();
            initialized = false;
        }
    }
}
