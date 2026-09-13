using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat
{
    [TrackColor(.8f, .25f, .2f)]
    [TrackClipType(typeof(NetworkEnemySpawnClip))]
    public sealed class NetworkEnemySpawnTrack : TrackAsset { }
}
