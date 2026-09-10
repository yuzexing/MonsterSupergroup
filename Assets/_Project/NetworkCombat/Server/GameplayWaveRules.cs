using System;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [CreateAssetMenu(menuName = "Network Combat/Gameplay Wave Rules")]
    public sealed class GameplayWaveRules : ScriptableObject
    {
        [SerializeField] private float waveDuration = 30;
        [SerializeField] private int enemiesPerWave = 6;
        [SerializeField] private float spawnInterval = 2;
        [SerializeField] private int maximumAlive = 30;
        [SerializeField] private float spawnRadius = 5;
        [SerializeField] private float minimumPlayerDistance = 2;
        [SerializeField] private int positionAttempts = 16;

        public bool TryCapture(out WaveParameters parameters, out string error)
        {
            try
            {
                parameters = new WaveParameters(waveDuration, enemiesPerWave, spawnInterval,
                    maximumAlive, spawnRadius, minimumPlayerDistance, positionAttempts);
                error = null; return true;
            }
            catch (ArgumentException exception) { parameters = null; error = exception.Message; return false; }
        }
    }
}
