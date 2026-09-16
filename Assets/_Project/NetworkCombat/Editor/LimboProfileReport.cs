using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    // Offline inspection of an explicitly captured Player profile; never runs in a build.
    public static class LimboProfileReport
    {
        public static void ExportBatch()
        {
            int exit = 0;
            try
            {
                string Arg(string prefix) => Environment.GetCommandLineArgs().Single(a => a.StartsWith(prefix, StringComparison.Ordinal)).Substring(prefix.Length);
                string input = Arg("--profile-input="), output = Arg("--profile-output=");
                if (!ProfilerDriver.LoadProfile(input, false)) throw new InvalidDataException("Cannot load Player profile: " + input);
                var frames = new List<Frame>();
                for (int index = ProfilerDriver.firstFrameIndex; index <= ProfilerDriver.lastFrameIndex; index++)
                {
                    using var view = ProfilerDriver.GetRawFrameDataView(index, 0);
                    if (!view.valid) continue;
                    var frame = new Frame { index = index, startMs = view.frameStartTimeMs, milliseconds = view.frameTimeMs, thread = view.threadName };
                    // Keep costly frames and a regular sample for the normal baseline.
                    bool keepScopes = true;
                    var scopes = new List<Scope>();
                    for (int i = 0; i < view.sampleCount; i++)
                    {
                        string name = view.GetSampleName(i);
                        double ms = view.GetSampleTimeMs(i);
                        if (name.Contains("DamageNumber.Start()")) frame.damageNumberStartMs += ms;
                        if (keepScopes && ms >= .5) scopes.Add(new Scope { name = name, milliseconds = ms, sample = i });
                    }
                    if (keepScopes) frame.scopes = scopes.OrderByDescending(s => s.milliseconds).Take(40).ToArray();
                    // Main/render work is pipelined. A long main-thread wait can
                    // correspond to the preceding render frame, so retain render
                    // samples independently instead of only at long main frames.
                    {
                        var renderScopes = new List<Scope>();
                        for (int thread = 1; thread < 64; thread++)
                        {
                            using var other = ProfilerDriver.GetRawFrameDataView(index, thread);
                            if (!other.valid) break;
                            if (other.threadName.IndexOf("Render", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            for (int i = 0; i < other.sampleCount; i++)
                            {
                                double ms = other.GetSampleTimeMs(i);
                                if (ms >= .5)
                                {
                                    var metadata = new List<string>();
                                    for (int m = 0; m < other.GetSampleMetadataCount(i); m++) metadata.Add(other.GetSampleMetadataAsString(i, m));
                                    renderScopes.Add(new Scope { name = other.threadName + "/" + other.GetSampleName(i), milliseconds = ms, sample = i, metadata = metadata.ToArray() });
                                }
                            }
                        }
                        frame.renderScopes = renderScopes.OrderByDescending(s => s.milliseconds).Take(30).ToArray();
                    }
                    frames.Add(frame);
                }
                File.WriteAllText(output, JsonUtility.ToJson(new Report { source = input, first = ProfilerDriver.firstFrameIndex,
                    last = ProfilerDriver.lastFrameIndex, frames = frames.ToArray() }, true));
            }
            catch (Exception e) { Debug.LogException(e); exit = 1; }
            finally { EditorApplication.Exit(exit); }
        }
        [Serializable] private class Report
        {
            public string source;
            public string note = "Explicit profiling adds overhead. Scope times are inclusive and overlap; do not add them. Frame timestamps are profiler time, not Limbo combat time.";
            public int first, last;
            public Frame[] frames;
        }
        [Serializable] private class Frame { public int index; public double startMs, milliseconds, damageNumberStartMs; public string thread; public Scope[] scopes, renderScopes; }
        [Serializable] private class Scope { public string name; public double milliseconds; public int sample; public string[] metadata; }
    }
}
