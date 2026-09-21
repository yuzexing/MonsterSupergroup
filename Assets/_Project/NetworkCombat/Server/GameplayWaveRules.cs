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
        [Header("Optional reference stage (uses the same server spawner)")]
        [SerializeField] private bool referenceStage;
        [SerializeField] private bool referenceValidationOnly;
        [SerializeField] private ReferenceEnemyReadiness referenceFlowReadiness;
        [SerializeField] private string referenceFlowReadinessNote;
        [SerializeField] private ReferenceEndPolicy referenceEndPolicy;
        [SerializeField, HideInInspector] private EnemyDatabase referenceEnemies; // Schema-0 import source only; schema 1 resolves EnemyDefinition.
        [SerializeField] private double referenceEndTime = 60;
        [SerializeField] private double referenceSourceDuration = 841.5766649882;
        [SerializeField] private AnimationCurve referenceXpCurve = AnimationCurve.Linear(0, 0, 1, 1);
        [SerializeField] private float referenceXpAmplitude = 1.5f;
        [SerializeField] private int referenceSeed = 14301;
        [SerializeField] private float offscreenDistance = 2, repositionDistance = 1.5f;
        [SerializeField] private float repositionGrace = 30, offscreenTimeout = 5, maximumOffscreenDistance = 20;
        [SerializeField] private float burstRadius = 6, burstAspect = 1.41f, effectsDelay = 1, activationDelay = 1;
        [SerializeField] private ReferenceBarrierDefinition[] barriers = Array.Empty<ReferenceBarrierDefinition>();
        [SerializeField] private ParticleSystem referenceFormationWarning;
        [SerializeField] private AstralShift.HellMaiden.Combat.Traps.BarrierTrap referenceBarrier;
        [SerializeField] private float referenceBarrierEntryScale = .25f;
        public ParticleSystem ReferenceFormationWarning => referenceFormationWarning;
        public AstralShift.HellMaiden.Combat.Traps.BarrierTrap ReferenceBarrier => referenceBarrier;
        public float ReferenceBarrierEntryScale => referenceBarrierEntryScale;

        public bool TryCapture(out WaveParameters parameters, out string error)
        {
            try
            {
                if (referenceStage)
                {
                    var reference = NetworkWaveTimelineCompiler.CompileReference(timeline, referenceEnemies,
                        referenceEndTime, referenceSourceDuration, referenceXpCurve, referenceXpAmplitude,
                        referenceSeed, offscreenDistance, repositionDistance, repositionGrace, offscreenTimeout,
                        maximumOffscreenDistance, burstRadius, burstAspect, effectsDelay, activationDelay,
                        barriers, out var referencePrefabs, referenceValidationOnly);
                    reference.FlowReadiness = referenceFlowReadiness;
                    reference.FlowReadinessNote = referenceFlowReadinessNote;
                    reference.EndPolicy = referenceEndPolicy;
                    parameters = new WaveParameters(reference, referencePrefabs, maximumAlive, positionAttempts);
                    error = null; return true;
                }
                var program = NetworkWaveTimelineCompiler.Compile(timeline, waveDuration, out var prefabs, out var definitions);
                parameters = new WaveParameters(program, prefabs, maximumAlive, spawnRadius, minimumPlayerDistance, positionAttempts);
                parameters.CaptureDefinitions(definitions);
                error = null; return true;
            }
            catch (ArgumentException exception) { parameters = null; error = exception.Message; return false; }
        }
        public TimelineAsset Timeline => timeline;
        public float WaveDuration => waveDuration;
        public bool IsReferenceStage => referenceStage;
    }
}
