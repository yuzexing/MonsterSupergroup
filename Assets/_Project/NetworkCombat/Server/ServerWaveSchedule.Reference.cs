using System;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerWaveSchedule
    {
        private sealed class ClipClock
        {
            public bool Started, Ended, Captured, WaitingInterval;
            public int Index, BatchRemaining, ReadyFrame;
            public double NextAt, CaptureAt, ReleaseTrapAt;
        }
        private ClipClock[] clipClocks;
        private int referenceFrame = -1, referenceCursor, pendingClip = -1, trapCount;
        private int[] clipAlive;
        private bool referencePaused;
        public event Action<int> FormationCaptured;
        public event Action<int> FormationReleased;
        public event Action<int> FormationSkipped;
        public event Action<int> ReferenceClipEnded;
        public int ReferenceTrapCount => trapCount;

        private void InitializeReference()
        {
            var reference = settings.Reference;
            string readinessError = reference.ReadinessError();
            if (readinessError != null) throw new ArgumentException(readinessError);
            clipClocks = new ClipClock[reference.Clips.Length];
            state.ReferenceStage = true; state.StageEndTime = reference.EndTime; state.Wave = 0; state.Planned = 0;
            for (int i = 0; i < clipClocks.Length; i++)
            {
                clipClocks[i] = new ClipClock();
                var clip = reference.Clips[i];
                if (clip.Start >= reference.EndTime) continue;
                if (clip.Mode != ReferenceSpawnMode.AliveTarget) state.Planned += clip.Count;
            }
        }

        // Called repeatedly in one server frame to drain independent clips or one formation.
        // Sequential C/L spawns always yield a frame, even for a zero-second wait.
        public bool TickReference(double now, int frame, bool active, int totalAlive, int countedAlive,
            int[] aliveByClip, out WaveSpawnOpportunity opportunity)
        {
            opportunity = default;
            if (settings.Reference == null) throw new InvalidOperationException("Not a reference stage.");
            if (state.Phase == WavePhase.Stopped || state.Phase == WavePhase.Completed) return false;
            if (pendingSequence != 0) throw new InvalidOperationException("Resolve the previous spawn decision first.");
            if (aliveByClip == null || aliveByClip.Length != clipClocks.Length) throw new ArgumentException(nameof(aliveByClip));
            state.Alive = totalAlive; state.CountedAlive = countedAlive; clipAlive = aliveByClip;
            if (referenceFrame != frame)
            {
                referenceFrame = frame; referenceCursor = 0;
                double delta = Math.Max(0, now - lastTime); lastTime = Math.Max(now, lastTime);
                if (!active)
                {
                    referencePaused = true;
                    if (state.TransitionRequestedAt == 0) state.Phase = WavePhase.Paused;
                    return false;
                }
                if (!referencePaused) state.Elapsed += delta;
                referencePaused = false;
                state.Phase = state.TransitionRequestedAt > 0 ? WavePhase.TransitionPending : WavePhase.Running;
                for (int index = 0; index < clipClocks.Length; index++)
                {
                    var clock = clipClocks[index];
                    if (clock.ReleaseTrapAt > 0 && state.Elapsed >= clock.ReleaseTrapAt)
                    { trapCount--; clock.ReleaseTrapAt = 0; FormationReleased?.Invoke(index); }
                }
                if (state.Elapsed >= settings.Reference.EndTime && state.TransitionRequestedAt == 0)
                {
                    if (settings.Reference.EndPolicy == ReferenceEndPolicy.ImmediatePreview)
                    { state.Elapsed = settings.Reference.EndTime; state.Phase = WavePhase.Completed; }
                    else { state.TransitionRequestedAt = state.Elapsed; state.Phase = WavePhase.TransitionPending; }
                    return false;
                }
            }
            if (!active) return false;
            for (; referenceCursor < clipClocks.Length; referenceCursor++)
            {
                int i = referenceCursor; var clock = clipClocks[i]; var clip = settings.Reference.Clips[i];
                if (clock.Ended || state.Elapsed <= clip.Start || clip.Start >= settings.Reference.EndTime ||
                    state.TransitionRequestedAt > 0 && !clock.Started) continue;
                if (!clock.Started)
                {
                    clock.Started = true; state.ActiveClips++;
                    if (clip.Mode == ReferenceSpawnMode.FormationBurst)
                    {
                        if (trapCount == 1) { state.TotalSkipped += clip.Count; state.Skipped += clip.Count; FormationSkipped?.Invoke(i); EndClip(i); continue; }
                        trapCount++; clock.ReleaseTrapAt = state.Elapsed + clip.End - clip.Start;
                        clock.CaptureAt = state.Elapsed + settings.Reference.EffectsDelay;
                        clock.NextAt = clock.CaptureAt + settings.Reference.ActivationDelay;
                    }
                    else
                    {
                        if (clip.Mode == ReferenceSpawnMode.CurveBudget && countedAlive == settings.Limit) { AbandonCurve(i); continue; }
                        clock.NextAt = state.Elapsed;
                        // C enters WaitWhile immediately; L first computes its batch at the outer-loop boundary.
                        clock.ReadyFrame = clip.Mode == ReferenceSpawnMode.CurveBudget ? frame + 1 : frame;
                    }
                }
                if (clip.Mode == ReferenceSpawnMode.FormationBurst)
                {
                    if (!clock.Captured && state.Elapsed >= clock.CaptureAt)
                    { clock.Captured = true; clock.NextAt = state.Elapsed + settings.Reference.ActivationDelay; FormationCaptured?.Invoke(i); }
                    if (state.Elapsed < clock.NextAt) continue;
                }
                else if (frame < clock.ReadyFrame || state.Elapsed < clock.NextAt) continue;

                if (clock.WaitingInterval)
                {
                    clock.WaitingInterval = false;
                    if (clip.Mode == ReferenceSpawnMode.CurveBudget && countedAlive == settings.Limit) { AbandonCurve(i); continue; }
                    // WaitForSeconds has resumed. The source next yields WaitWhile, or (at an L batch end) yield null.
                    clock.ReadyFrame = frame + 1;
                    continue;
                }
                if (clip.Mode == ReferenceSpawnMode.AliveTarget && clock.BatchRemaining == 0)
                {
                    if (state.Elapsed > clip.End) { EndClip(i); continue; }
                    if (clipAlive[i] >= clip.Count || countedAlive == settings.Limit) continue;
                    clock.BatchRemaining = clip.Count - clipAlive[i];
                    clock.ReadyFrame = frame + 1;
                    continue;
                }
                pendingClip = i; pendingSequence = nextOpportunity + 1;
                opportunity = new WaveSpawnOpportunity(pendingSequence, 0, clock.Index + 1, clip.PrefabIndex,
                    clip.Start + (clip.Mode == ReferenceSpawnMode.CurveBudget ? clip.Timestamps[clock.Index] : state.Elapsed - clip.Start),
                    i, clip.Mode == ReferenceSpawnMode.FormationBurst ? clock.Index : -1);
                return true;
            }
            return false;
        }

        private bool ResolveReference(WaveSpawnOpportunity opportunity, bool spawned)
        {
            if (pendingSequence == 0 || opportunity.Sequence != pendingSequence || opportunity.ClipIndex != pendingClip) return false;
            int i = pendingClip; var clock = clipClocks[i]; var clip = settings.Reference.Clips[i];
            pendingSequence = 0; pendingClip = -1; nextOpportunity = opportunity.Sequence; state.TotalAttempts++;
            if (spawned)
            {
                state.Spawned++; state.TotalSpawned++; state.Alive++;
                if (clip.Mode != ReferenceSpawnMode.FormationBurst) state.CountedAlive++;
            }
            else { state.Skipped++; state.TotalSkipped++; }
            clock.Index++;
            if (clip.Mode == ReferenceSpawnMode.FormationBurst)
            { if (clock.Index >= clip.Count) EndClip(i); }
            else
            {
                clock.ReadyFrame = referenceFrame + 1;
                clock.WaitingInterval = true;
                if (clip.Mode == ReferenceSpawnMode.CurveBudget)
                {
                    if (clock.Index >= clip.Timestamps.Length) EndClip(i);
                    else if (state.CountedAlive == settings.Limit) AbandonCurve(i);
                    else clock.NextAt = state.Elapsed + clip.Timestamps[clock.Index] - clip.Timestamps[clock.Index - 1];
                }
                else { clock.BatchRemaining--; clock.NextAt = state.Elapsed + clip.Cooldown; }
                referenceCursor++;
            }
            return true;
        }
        private void AbandonCurve(int i)
        {
            state.AbandonedBudget += Math.Max(0, settings.Reference.Clips[i].Timestamps.Length - clipClocks[i].Index);
            EndClip(i);
        }
        private void EndClip(int i)
        {
            if (clipClocks[i].Ended) return;
            clipClocks[i].Ended = true; state.ActiveClips--; ReferenceClipEnded?.Invoke(i);
        }

        public bool TryAcquireBarrierSlot() { if (trapCount == 1) return false; trapCount++; return true; }
        public void SetReferenceWait(int count, string reason)
        {
            if (state.Phase != WavePhase.TransitionPending) return;
            state.TransitionWaitCount = count; state.TransitionWaitReason = reason;
        }
        public bool CompleteReferenceTransition(string runId)
        {
            if (state.RunId != runId || state.Phase != WavePhase.TransitionPending || pendingSequence != 0) return false;
            state.Phase = WavePhase.Completed; state.TransitionWaitCount = 0; state.TransitionWaitReason = "";
            return true;
        }
        public void ReleaseBarrierSlot() { if (trapCount > 0) trapCount--; }
        public double FormationReleaseTime(int clip) => clipClocks[clip].ReleaseTrapAt;
        public void CancelReferenceTraps()
        {
            trapCount = 0;
            if (clipClocks != null) foreach (var clock in clipClocks) clock.ReleaseTrapAt = 0;
        }
    }
}
