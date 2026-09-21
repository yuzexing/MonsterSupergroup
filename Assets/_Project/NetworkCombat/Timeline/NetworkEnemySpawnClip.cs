using MonsterSupergroup.Gameplay.Combat.Content;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat
{
    // Authoring data only. Playing or scrubbing this asset never spawns an enemy.
    public sealed class NetworkEnemySpawnClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField, HideInInspector] private int authoringVersion; // 0 = explicit pre-migration schema; 1 = definition-only.
        [SerializeField] private EnemyDefinition enemy;
        public int AuthoringVersion => authoringVersion;
        public EnemyDefinition Enemy => enemy;
        // Legacy authoring/provenance only. Production compilers never fall back to these fields.
        [HideInInspector] public GameObject enemyPrefab;
        [Min(1)] public int count = 6;
        public ReferenceSpawnMode referenceMode;
        [HideInInspector] public string sourceEnemy;
        [HideInInspector] public int sourceVariant;
        public string sourceLocation;
        [TextArea] public string missingEvidence;
        public ReferenceEnemyReadiness referenceReadiness;
        public ReferenceEnemyReadiness referenceSpawnReadiness;
        [TextArea] public string spawnReadinessNote;
        public string recoveredEvidence;
        public AnimationCurve spawnCurve = AnimationCurve.Linear(0, 1, 1, 1);
        public float spawnCooldown;
        public Vector2 speedMultipliers = new Vector2(.9f, 1.1f);
        public float contactRadius;
        public bool expiresOffscreen = true;
        public bool resetOnReposition = true;
        public ClipCaps clipCaps => ClipCaps.None;
        public override double duration => 12;
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Playable.Create(graph);
    }
}
