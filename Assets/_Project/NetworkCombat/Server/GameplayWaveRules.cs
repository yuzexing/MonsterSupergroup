using System;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat
{
    [CreateAssetMenu(menuName = "Network Combat/Gameplay Wave Rules")]
    public sealed class GameplayWaveRules : ScriptableObject
    {
        [SerializeField] private float waveDuration = 30;
        [SerializeField] private TimelineAsset timeline;
        [SerializeField] private int maximumAlive = 30;
        [SerializeField] private float spawnRadius = 5;
        [SerializeField] private float minimumPlayerDistance = 2;
        [SerializeField] private int positionAttempts = 16;

        public bool TryCapture(out WaveParameters parameters, out string error)
        {
            try
            {
                var program = NetworkWaveTimelineCompiler.Compile(timeline, waveDuration, out var prefabs);
                parameters = new WaveParameters(program, prefabs, maximumAlive, spawnRadius, minimumPlayerDistance, positionAttempts);
                error = null; return true;
            }
            catch (ArgumentException exception) { parameters = null; error = exception.Message; return false; }
        }
        public TimelineAsset Timeline => timeline;
        public float WaveDuration => waveDuration;
    }
}
