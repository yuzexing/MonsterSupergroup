using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetworkExperienceGem : NetworkBehaviour
    {
        [SyncVar] private string runId;
        [SyncVar] private ulong dropId;
        [SyncVar] private float rawExperience;
        [SyncVar] private bool claimed;
        [SyncVar] private PickupEffect effect;
        [SyncVar] private uint definitionId;
        [SyncVar] private uint claimVersion;
        [SyncVar] private uint collectorId;
        [SyncVar] private float flightElapsed;
        [SyncVar] private bool flightPaused;
        [SerializeField] private Transform visual;
        [SerializeField] private FMODUnity.EventReference pullSound, consumeSound;
        private Vector3 visualOrigin;
        private uint presentedClaim;
        private bool consumedSound;
        private Vector3 healthBack;
        private bool presented;
        private static readonly HashSet<NetworkExperienceGem> clientGems = new HashSet<NetworkExperienceGem>();
        public static IReadOnlyCollection<NetworkExperienceGem> ClientGems => clientGems;
        public string RunId => runId;
        public ulong DropId => dropId;
        public float RawExperience => rawExperience;
        public bool Claimed => claimed;
        public PickupEffect Effect => effect == PickupEffect.None ? PickupEffect.Experience : effect;
        public uint ClaimVersion => claimVersion;
        public float FlightElapsed => flightElapsed;
        public Transform Visual => visual;
        internal NetworkExperienceGem PoolPrefab { get; private set; }
        internal int PoolCapacity { get; private set; } = 500;
        internal bool PoolReturned { get; set; }
        private void Awake() { if (visual != null) visualOrigin = visual.localPosition; }
        internal void PrepareForReuse(NetworkExperienceGem prefab, int capacity)
        {
            PoolPrefab = prefab; PoolCapacity = capacity; PoolReturned = false;
            runId = null; dropId = 0; rawExperience = 0; claimed = false;
            effect = PickupEffect.None; claimVersion = 0; collectorId = 0; flightElapsed = 0;
            ResetPresentation();
        }
        public void Initialize(string run, ulong id, float amount, PickupEffect kind = PickupEffect.Experience, uint definition = 1)
        { runId = run; dropId = id; rawExperience = amount; effect = kind; definitionId = definition; claimed = false; }
        internal void ResetPresentation()
        {
            presented = false; presentedClaim = 0; consumedSound = false; flightPaused = false;
            if (visual == null) return;
            visual.localPosition = visualOrigin; visual.gameObject.SetActive(true);
            foreach (var ps in visual.GetComponentsInChildren<ParticleSystem>(true)) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(); }
            foreach (var animator in visual.GetComponentsInChildren<Animator>(true))
            { animator.Rebind(); if (animator.isActiveAndEnabled) animator.Update(0); }
        }
        public void BeginHealthFlight(uint player)
        { claimed = true; collectorId = player; claimVersion++; flightElapsed = 0; flightPaused = false; }
        public void SetFlightProgress(float elapsed, bool paused) { flightElapsed = elapsed; flightPaused = paused; }
        public void CancelHealthFlight()
        { claimed = false; collectorId = 0; flightElapsed = 0; ResetPresentation(); RpcResetHealth(); }
        [ClientRpc] private void RpcResetHealth() => ResetPresentation();
        public void ServerHealthConsumed() { if (isClient) PlayConsumed(); RpcHealthConsumed(); }
        [ClientRpc] private void RpcHealthConsumed() => PlayConsumed();
        private void PlayConsumed()
        { if (consumedSound) return; consumedSound = true; PresentHealthArrival(); if (!consumeSound.IsNull) FMODUnity.RuntimeManager.PlayOneShot(consumeSound, visual.position); }
        internal void PresentHealthArrival()
        {
            if (visual != null && NetworkClient.spawned.TryGetValue(collectorId, out var player))
                visual.position = player.transform.position;
        }
        public void SetClaimed(bool value) => claimed = value;
        public override void OnStartServer() { if (!NetworkClient.active && visual != null) visual.gameObject.SetActive(false); }
        public override void OnStartClient()
        {
            MonsterSupergroup.Gameplay.Combat.GameplayMapPresentation.Body(visual, transform);
            MonsterSupergroup.Gameplay.Combat.GameplayPlanarEffect.Attach(gameObject);
            clientGems.Add(this);
            if (visual != null) visual.gameObject.SetActive(!claimed || Effect == PickupEffect.RestoreHealth);
        }
        [Server]
        public void ServerPresentCollection(uint playerId)
        {
            // Host RPCs are queued; borrow the flight before unspawning its shared identity.
            if (isClient) PresentCollection(playerId);
            RpcPresentCollection(playerId);
        }
        [ClientRpc]
        private void RpcPresentCollection(uint playerId) => PresentCollection(playerId);
        private void PresentCollection(uint playerId)
        {
            if (presented) return;
            presented = true;
            if (visual == null || !NetworkClient.spawned.TryGetValue(playerId, out var player)) return;
            NetworkExperienceWorld.Current?.PresentXpFlight(this, player.transform);
            visual.gameObject.SetActive(false);
        }
        private void LateUpdate()
        {
            if (!isClient || visual == null || Effect != PickupEffect.RestoreHealth || !claimed ||
                !NetworkClient.spawned.TryGetValue(collectorId, out var player)) return;
            var definition = NetworkExperienceWorld.Current?.Definition(Effect);
            if (definition == null) return;
            if (presentedClaim != claimVersion)
            {
                presentedClaim = claimVersion;
                Vector3 start = transform.TransformPoint(visualOrigin);
                healthBack = start + (start - player.transform.position).normalized * definition.BackDistance;
                if (!pullSound.IsNull && flightElapsed < .2f) FMODUnity.RuntimeManager.PlayOneShot(pullSound, transform.position);
            }
            Vector3 origin = transform.TransformPoint(visualOrigin);
            Vector3 back = healthBack;
            if (flightElapsed < definition.BackDuration)
                visual.position = Vector3.LerpUnclamped(origin, back, definition.BackCurve.Evaluate(flightElapsed / definition.BackDuration));
            else
            {
                float t = definition.JumpCurve.Evaluate(Mathf.Clamp01((flightElapsed - definition.BackDuration) / definition.JumpDuration));
                visual.position = Vector3.LerpUnclamped(back, player.transform.position, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * definition.JumpHeight;
            }
        }
        public override void OnStopClient() => clientGems.Remove(this);
        private void OnDestroy() => clientGems.Remove(this);
    }

    /// <summary>Disposable presentation only. Destruction/disconnect never changes committed XP.</summary>
    public sealed class ExperienceCollectionFlight : MonoBehaviour
    {
        private Transform target;
        private Vector3 origin, back;
        private float started;
        private string runId;
        private bool turnedOffParticles;
        private System.Action returned;
        private ParticleSystem[] cachedParticles;
        private bool[] originalLoop;
        public void Begin(Transform receiver, string run, System.Action returnToPool = null)
        {
            enabled = true; turnedOffParticles = false; returned = returnToPool;
            if (cachedParticles == null)
            {
                cachedParticles = GetComponentsInChildren<ParticleSystem>(true);
                originalLoop = new bool[cachedParticles.Length];
                for (int i = 0; i < cachedParticles.Length; i++) originalLoop[i] = cachedParticles[i].main.loop;
            }
            for (int i = 0; i < cachedParticles.Length; i++)
            { var main = cachedParticles[i].main; main.loop = originalLoop[i]; cachedParticles[i].Clear(); cachedParticles[i].Play(); }
            target = receiver; runId = run; started = Time.unscaledTime; origin = transform.position;
            back = origin + (origin - target.position).normalized; // WorldItem baseMoveBackAmount = 1.
        }
        private void Update()
        {
            if (!NetworkClient.active || target == null || NetworkExperienceWorld.Current?.RunId != runId)
            { Finish(); return; }
            float elapsed = Time.unscaledTime - started;
            if (elapsed >= 0.4f) { Finish(); return; }
            if (elapsed < 0.1f)
            {
                float t = elapsed / 0.1f;
                transform.position = Vector3.Lerp(origin, back, t);
            }
            else
            {
                if (!turnedOffParticles)
                {
                    turnedOffParticles = true;
                    foreach (var sprite in GetComponentsInChildren<SpriteRenderer>()) sprite.enabled = false;
                    foreach (var particles in GetComponentsInChildren<ParticleSystem>())
                    { var main = particles.main; main.loop = false; particles.Play(); }
                }
                float t = (elapsed - 0.1f) / 0.3f;
                float progress = t * t;
                transform.position = Vector3.Lerp(back, target.position, progress) + Vector3.up * Mathf.Sin(progress * Mathf.PI);
            }
        }
        private void Finish()
        { enabled = false; target = null; var callback = returned; returned = null; if (callback != null) callback(); else Destroy(gameObject); }
    }
}
