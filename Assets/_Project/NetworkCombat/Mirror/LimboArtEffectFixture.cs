using System;
using System.IO;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Three visual-only resources, including deferred enemies. No damage or enemy registration.
    public sealed class LimboArtEffectFixture : MonoBehaviour
    {
        [SerializeField] private GameObject[] effects;
        private int index = -1;
        private GameObject current;
        private bool shown, hidden;
        private StreamWriter log;
        private void Start()
        {
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log = new StreamWriter(Path.Combine(LimboReferenceLaunch.OutputDirectory,"art-effects.jsonl")) {AutoFlush=true};
        }
        private void Update()
        {
            if (NetworkCombatWorld.Instance == null || NetworkClient.localPlayer == null) return;
            var progress = NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot;
            if (progress.Phase == WavePhase.Completed || BootGameplayNetworkManager.CombatHasEnded)
            { if (current != null) Destroy(current); return; }
            double elapsed = progress.Elapsed;
            int desired = Mathf.Min((int)(elapsed/8), effects.Length-1);
            if (desired != index)
            {
                if (current != null) Destroy(current);
                index = desired; shown = hidden = false;
                current = Instantiate(effects[index], NetworkClient.localPlayer.transform.position+Vector3.right*3, Quaternion.identity);
                MonsterSupergroup.Gameplay.Combat.GameplayMapPresentation.Warning(current);
                var warning=current.GetComponent<EnemyAttackWarning>();
                if(warning!=null){warning.SetWarningTime(1, .1f);warning.Show();}
                foreach(var ps in current.GetComponentsInChildren<ParticleSystem>(true))ps.Play();
                Record("show", elapsed);
                Capture("start", elapsed); // Capture short explosion bursts before the 0.7s tail sample.
            }
            double time = elapsed-index*8;
            if (!shown && time >= .7) { shown = true; Capture("shown",elapsed); }
            if (!hidden && time >= 3)
            {
                hidden = true;
                current.GetComponent<EnemyAttackWarning>()?.Hide();
                foreach(var ps in current.GetComponentsInChildren<ParticleSystem>(true))ps.Stop(true,ParticleSystemStopBehavior.StopEmitting);
                Record("hide",elapsed);
            }
            if(hidden && time>=3.6 && time<3.7)Capture("tail",elapsed);
        }
        private void Capture(string phase,double elapsed)
        { ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory,$"art-effect-{index}-{phase}.png"));Record(phase,elapsed); }
        private void Record(string phase,double elapsed)
        { log?.WriteLine(JsonUtility.ToJson(new Row{phase=phase,elapsed=elapsed,index=index,asset=effects[index].name,
            note="Visual-only 8s display slot; explicit 1s warning preview. Not recovered enemy timing or behavior verification."})); }
        private void OnDestroy(){if(current!=null)Destroy(current);log?.Dispose();}
        [Serializable] private class Row{public string phase,asset,note;public double elapsed;public int index;}
    }
}
