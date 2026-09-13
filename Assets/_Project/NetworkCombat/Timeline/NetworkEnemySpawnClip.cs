using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat
{
    // Authoring data only. Playing or scrubbing this asset never spawns an enemy.
    public sealed class NetworkEnemySpawnClip : PlayableAsset, ITimelineClipAsset
    {
        public GameObject enemyPrefab;
        [Min(1)] public int count = 6;
        public ClipCaps clipCaps => ClipCaps.None;
        public override double duration => 12;
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Playable.Create(graph);
    }
}
