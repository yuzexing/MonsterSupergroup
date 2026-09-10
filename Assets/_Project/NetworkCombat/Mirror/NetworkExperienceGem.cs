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
        [SerializeField] private Transform visual;
        private bool presented;
        private static readonly HashSet<NetworkExperienceGem> clientGems = new HashSet<NetworkExperienceGem>();
        public static IReadOnlyCollection<NetworkExperienceGem> ClientGems => clientGems;
        public string RunId => runId;
        public ulong DropId => dropId;
        public float RawExperience => rawExperience;
        public bool Claimed => claimed;
        public void Initialize(string run, ulong id, float amount) { runId = run; dropId = id; rawExperience = amount; }
        public void SetClaimed(bool value) => claimed = value;
        public override void OnStartServer() { if (!NetworkClient.active && visual != null) visual.gameObject.SetActive(false); }
        public override void OnStartClient()
        {
            clientGems.Add(this);
            if (visual != null) visual.gameObject.SetActive(!claimed);
        }
        [ClientRpc]
        public void RpcPresentCollection(uint playerId)
        {
            if (presented) return;
            presented = true;
            if (visual == null || !NetworkClient.spawned.TryGetValue(playerId, out var player)) return;
            visual.SetParent(null, true);
            visual.gameObject.SetActive(true);
            visual.gameObject.AddComponent<ExperienceCollectionFlight>().Begin(player.transform, runId);
            visual = null;
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
        public void Begin(Transform receiver, string run)
        {
            target = receiver; runId = run; started = Time.unscaledTime; origin = transform.position;
            back = origin + (origin - target.position).normalized; // WorldItem baseMoveBackAmount = 1.
        }
        private void Update()
        {
            if (!NetworkClient.active || target == null || NetworkExperienceWorld.Current?.RunId != runId)
            { Destroy(gameObject); return; }
            float elapsed = Time.unscaledTime - started;
            if (elapsed >= 0.4f) { Destroy(gameObject); return; }
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
    }
}
