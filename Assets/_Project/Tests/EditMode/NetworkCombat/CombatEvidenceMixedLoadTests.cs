using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    /// <summary>Opt-in, wall-clock paced Store comparison. Fixture preparation and independent verification are Python tools.</summary>
    public sealed class CombatEvidenceMixedLoadTests
    {
        // Three real-time 600-second repetitions plus template preparation and
        // bounded drain work intentionally exceed Unity's default 180 seconds.
        [Test, Category("CombatEvidenceMixedBenchmark"), Timeout(7200000)]
        public void RunFrozenMixedLoad()
        {
            if (Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_MIXED_BENCHMARK") != "1")
                Assert.Ignore("Opt in with COMBAT_EVIDENCE_MIXED_BENCHMARK=1. This is a Store benchmark, not gameplay or weak-device acceptance.");
            string manifestPath = Required("COMBAT_EVIDENCE_MIXED_MANIFEST");
            string output = Required("COMBAT_EVIDENCE_MIXED_OUTPUT");
            string profile = Environment.GetEnvironmentVariable("COMBAT_EVIDENCE_MIXED_PROFILE") ?? "diagnostic";
            double duration = Number("COMBAT_EVIDENCE_MIXED_SECONDS", 600, .01, 3600);
            int repeats = (int)Number("COMBAT_EVIDENCE_MIXED_REPEATS", 3, 1, 3);
            var manifest = Parse(File.ReadAllText(manifestPath));
            string manifestSha256 = FileHash(manifestPath);
            Assert.That((int)manifest["schemaVersion"], Is.EqualTo(1));
            ClockMapping(manifest);
            string fixture = Path.Combine(Path.GetDirectoryName(manifestPath), (string)manifest["fixture"]);
            Assert.That(FileHash(fixture), Is.EqualTo((string)manifest["fixtureSha256"]));
            Assert.That(profile, Is.EqualTo("standard").Or.EqualTo("diagnostic"));
            // One bounded, private source-cycle template is prepared before any
            // paced Store run. Each submitted payload receives its own clone.
            var templates = LoadTemplates(fixture, manifest);
            var maximumLatenessByRepeat = new double[repeats];
            for (int repeat = 0; repeat < repeats; repeat++)
                maximumLatenessByRepeat[repeat] = Run(manifest, manifestSha256, templates,
                    Path.Combine(output, profile + "-" + (repeat + 1).ToString("D2")), profile, duration);
            // Preserve every predeclared repetition, including genuine Store/GC
            // stalls. Operational failures still throw inside Run immediately.
            Assert.That(maximumLatenessByRepeat.All(value => value <= .1), Is.True,
                "MixedBenchmarkPacingRejectedAfterAllRepeats\n" + EvidenceJson.Encode(new {
                    configuredRepeats = repeats, maximumAllowedSeconds = .1, maximumLatenessSecondsByRepeat = maximumLatenessByRepeat }));
        }

        private static double Run(JObject manifest, string manifestSha256, TemplateSet templates, string output, string profile, double duration)
        {
            if (Directory.Exists(output)) throw new InvalidOperationException("Benchmark output already exists: " + output);
            Directory.CreateDirectory(output);
            var options = Options(profile);
            var memory = options.Memory;
            var storage = new CountingStorage(); options.Storage = storage;
            using var store = new CombatEvidenceStore(Path.Combine(output, "capture"), options);
            using var cancellation = new CancellationTokenSource();
            using var ready = new ManualResetEventSlim();
            var prepared = new PreparedBuffer(32768, 128L << 20, ready, cancellation.Token);
            var preparationClock = Stopwatch.StartNew();
            Exception readerFailure = null;
            long preparationTicks = 0, maximumPreparationTicks = 0, preparedRecords = 0;
            var reader = new Thread(() => {
                try
                {
                    using var items = ReadTemplates(templates, manifest, duration).GetEnumerator();
                    while (true)
                    {
                        long started = Stopwatch.GetTimestamp();
                        bool hasItem = items.MoveNext();
                        long ticks = Stopwatch.GetTimestamp() - started;
                        preparationTicks += ticks; maximumPreparationTicks = Math.Max(maximumPreparationTicks, ticks);
                        if (!hasItem) break;
                        preparedRecords++;
                        prepared.Add(items.Current);
                        // Prepare ahead through the first dense interval, or until either
                        // buffer limit is reached. Capacity applies throughout all cycles.
                        if (items.Current.due >= 30) ready.Set();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { readerFailure = error; }
                finally { ready.Set(); prepared.Complete(); }
            }) { IsBackground = true, Name = "Evidence mixed fixture reader" };
            reader.Start();
            if (!ready.Wait(60000)) { cancellation.Cancel(); prepared.Wake(); reader.Join(5000); throw new TimeoutException("Fixture reader did not start."); }
            double preparationBeforeClockSeconds = preparationClock.Elapsed.TotalSeconds;
            object initialBuffer = prepared.Snapshot();
            var clock = Stopwatch.StartNew();
            using var process = Process.GetCurrentProcess();
            var initialCollections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            store.QueueObservation?.MarkPhase(EvidenceQueuePhase.Load, EvidenceQueueObservation.Now);
            long attempted = 0, accepted = 0, rejected = 0, advances = 0, records = 0;
            long lateOver10 = 0, lateOver100 = 0, sharedFailures = 0;
            double maximumLateness = 0, totalLateness = 0;
            long storeAdvanceTicks = 0, storeRecordTicks = 0, maximumStoreTicks = 0, sharedCaptureTicks = 0;
            double dueWaitSeconds = 0;
            var pacing = new SortedDictionary<int, PacingWindow>();
            long peakWorkingSet = -1;
            int processMemoryUnavailableSamples = 0;
            var samples = new List<object>();
            var shared = new Dictionary<string, SharedLease>();
            string cycle = null;
            double nextSample = 0;
            bool joined = false, exported = false;
            Exception failure = null;
            try
            {
                while (prepared.Take(out var item, out double takeWaitMilliseconds))
                {
                    double waitStarted = clock.Elapsed.TotalSeconds;
                    while (clock.Elapsed.TotalSeconds < item.due)
                    {
                        double remaining = item.due - clock.Elapsed.TotalSeconds;
                        if (remaining > .002) Thread.Sleep(1); else Thread.SpinWait(64);
                    }
                    dueWaitSeconds += clock.Elapsed.TotalSeconds - waitStarted;
                    if (cycle != item.record.captureId)
                    {
                        if (shared.Count != 0) throw new InvalidDataException("Unreleased shared group at cycle boundary.");
                        cycle = item.record.captureId;
                    }
                    attempted++;
                    bool success;
                    long submitStarted = 0, submitTicks = 0;
                    double actual = clock.Elapsed.TotalSeconds;
                    if (item.kind == "advance")
                    {
                        advances++;
                        submitStarted = Stopwatch.GetTimestamp();
                        success = store.TryWriteAdvance(item.record.captureId, item.record.runId, item.record.round, item.advance);
                        submitTicks = Stopwatch.GetTimestamp() - submitStarted; storeAdvanceTicks += submitTicks;
                    }
                    else
                    {
                        records++;
                        try
                        {
                            long sharedStarted = Stopwatch.GetTimestamp();
                            MaterializeShared(item, manifest, memory, shared);
                            sharedCaptureTicks += Stopwatch.GetTimestamp() - sharedStarted;
                            // Payload JSON is a fresh detached graph. Queue charges retain the original
                            // estimates; this comparison does not claim original producer allocation costs.
                            actual = clock.Elapsed.TotalSeconds;
                            submitStarted = Stopwatch.GetTimestamp();
                            success = store.TryWrite(item.record, true);
                            submitTicks = Stopwatch.GetTimestamp() - submitStarted; storeRecordTicks += submitTicks;
                        }
                        catch (SharedBudgetException) { sharedFailures++; success = false; }
                        finally { ReleaseCreatorUses(item, shared); }
                    }
                    maximumStoreTicks = Math.Max(maximumStoreTicks, submitTicks);
                    double lateness = Math.Max(0, actual - item.due);
                    maximumLateness = Math.Max(maximumLateness, lateness); totalLateness += lateness;
                    if (lateness > .01) lateOver10++;
                    if (lateness > .1) lateOver100++;
                    Window(pacing, item.due).planned++;
                    var window = Window(pacing, actual); window.submitted++;
                    if (success) window.accepted++; else window.rejected++;
                    window.maximumLatenessSeconds = Math.Max(window.maximumLatenessSeconds, lateness);
                    window.storeCallMilliseconds += Milliseconds(submitTicks);
                    window.readerWaitMilliseconds += takeWaitMilliseconds;
                    if (success) accepted++; else rejected++;
                    if (clock.Elapsed.TotalSeconds >= nextSample)
                    {
                        long working = WorkingSet(process, out string memorySource);
                        if (working <= 0) processMemoryUnavailableSamples++;
                        peakWorkingSet = Math.Max(peakWorkingSet, working);
                        samples.Add(new { seconds = clock.Elapsed.TotalSeconds, pendingBytes = store.PendingBytes,
                            memoryUsedBytes = memory.Used, processWorkingSetBytes = working, processMemorySource = memorySource,
                            fixtureBuffer = prepared.Snapshot(), currentPlannedSeconds = item.due, currentLatenessSeconds = lateness,
                            gcCollections = new[] { GC.CollectionCount(0) - initialCollections[0], GC.CollectionCount(1) - initialCollections[1], GC.CollectionCount(2) - initialCollections[2] } });
                        nextSample = clock.Elapsed.TotalSeconds + 1;
                    }
                }
                if (readerFailure != null) throw new InvalidDataException("Fixture reader failed.", readerFailure);
                // Wait through the final requested interval even when the final source event precedes it.
                while (clock.Elapsed.TotalSeconds < duration) Thread.Sleep(1);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                cancellation.Cancel();
                prepared.Wake();
                if (!reader.Join(5000) && failure == null) failure = new TimeoutException("Fixture reader did not stop.");
                foreach (var lease in shared.Values) lease.payload?.Dispose();
                shared.Clear();
            }
            double producedSeconds = clock.Elapsed.TotalSeconds;
            store.QueueObservation?.MarkPhase(EvidenceQueuePhase.Close, EvidenceQueueObservation.Now);
            store.RequestClose();
            var drain = Stopwatch.StartNew();
            while (!(joined = store.WaitForClose(100)) && drain.Elapsed.TotalSeconds < 1800) { }
            if (joined) exported = store.ExportQueueObservation(Path.Combine(output, "writer-observation.json"));
            else store.ExportPartialQueueObservation(Path.Combine(output, "writer-observation.partial.json"));
            var result = new {
                schemaVersion = 1, fixtureId = (string)manifest["fixtureId"], fixtureSha256 = (string)manifest["fixtureSha256"],
                recordClockMapping = ClockMapping(manifest), manifestSha256,
                profile, queueBytes = options.QueueBytes, reservedBytes = options.ReservedBytes, memoryLimitBytes = memory.Limit,
                durationSeconds = duration, productionWallSeconds = producedSeconds, drainSeconds = drain.Elapsed.TotalSeconds,
                attempted, accepted, rejected, advances, records, sharedCaptureFailures = sharedFailures,
                peakPendingBytes = store.PeakPendingBytes, pendingFinalBytes = store.PendingBytes,
                peakMemoryBytes = memory.Peak, memoryFinalBytes = memory.Used, peakProcessWorkingSetBytes = peakWorkingSet,
                processMemoryUnavailableSamples, processMemorySamplesValid = samples.Count > 0 && processMemoryUnavailableSamples == 0,
                dropped = store.Dropped, lastFailure = store.LastFailure, joined, observationExported = exported,
                maximumLatenessSeconds = maximumLateness, meanLatenessSeconds = attempted == 0 ? 0 : totalLateness / attempted,
                lateOver10Milliseconds = lateOver10, lateOver100Milliseconds = lateOver100,
                paceValid = maximumLateness <= .1, metrics = store.Metrics.Snapshot(),
                eventOpenCount = storage.opens, eventFlushCount = storage.flushes, eventDurableFlushCount = storage.durableFlushes,
                fixturePreparation = new { preparationBeforeClockSeconds, initialBuffer, finalBuffer = prepared.Snapshot(),
                    preparedRecords, readMilliseconds = Milliseconds(preparationTicks), maximumItemPreparationMilliseconds = Milliseconds(maximumPreparationTicks),
                    accounting = "Additional fixture-only buffer, excluded from Store budget: at most 32768 queued items and 128 MiB conservative source-size charge (2048 + 8*UTF8 bytes per item), plus one reader and one producer item. Charge is an estimate, not measured managed heap; actual Editor working set includes this buffer and the separately reported template." },
                fixtureTemplate = templates.observation,
                producer = new { storeAdvanceMilliseconds = Milliseconds(storeAdvanceTicks), storeRecordMilliseconds = Milliseconds(storeRecordTicks),
                    maximumStoreCallMilliseconds = Milliseconds(maximumStoreTicks), sharedCaptureMilliseconds = Milliseconds(sharedCaptureTicks), dueWaitSeconds },
                pacingWindowSeconds = .1, pacingWindows = pacing.Values,
                failure = failure?.ToString(), samples,
                independentLogicalVerification = "Required: CombatEvidenceMixedBenchmark.py verify",
                limitations = manifest["limitations"]
            };
            File.WriteAllText(Path.Combine(output, "benchmark.json"), EvidenceJson.Encode(result), new UTF8Encoding(false));
            Assert.That(failure, Is.Null, failure?.ToString());
            Assert.That(joined, Is.True, "Benchmark safety timeout preserves partial results; not a successful close.");
            Assert.That(exported, Is.True);
            // Baseline may reject. The independent verifier decides completeness for every variant.
            Assert.That(memory.Used, Is.Zero);
            Assert.That(store.LastFailure, Is.Null, "UnexpectedStoreFailure");
            Assert.That(sharedFailures, Is.Zero, "UnexpectedSharedCaptureFailure");
            return maximumLateness;
        }

        private static EvidenceStoreOptions Options(string profile)
        {
            // Reflection keeps this identical harness compilable against the frozen original build.
            var factory = typeof(EvidenceStoreOptions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(method => method.Name == "ForProfile");
            if (factory != null)
            {
                var type = factory.GetParameters()[0].ParameterType;
                return (EvidenceStoreOptions)factory.Invoke(null, new[] { Enum.Parse(type, profile, true), (object)true });
            }
            if (profile != "standard") throw new InvalidOperationException("Frozen baseline supports only standard; do not emulate a new runtime profile.");
            return new EvidenceStoreOptions { ObserveQueue = true, DeferObservationWindows = true };
        }

        private sealed class Prepared
        {
            public DiagnosticRecord record;
            public DiagnosticAdvance advance;
            public string kind;
            public JArray shared;
            public double due;
            public long fixtureCharge;
            public ulong sourceSequence;
            public DateTime sourceUtc;
        }
        private sealed class TemplateSet
        {
            public readonly Prepared[] items;
            public readonly object observation;
            public TemplateSet(Prepared[] items, object observation) { this.items = items; this.observation = observation; }
        }
        private static TemplateSet LoadTemplates(string fixture, JObject manifest, int maximumItems = 400000, long maximumChargeBytes = 4L << 30)
        {
            var clock = Stopwatch.StartNew();
            using var process = Process.GetCurrentProcess();
            long before = WorkingSet(process, out string memorySource), peak = before, charge = 0;
            int unavailable = before <= 0 ? 1 : 0;
            double nextSample = 1;
            var items = new List<Prepared>();
            foreach (var item in Read(fixture, manifest, (double)manifest["cycleSeconds"]))
            {
                if (items.Count >= maximumItems || item.fixtureCharge > maximumChargeBytes - charge)
                    throw new InvalidDataException("Fixture template exceeds its separate fixed item or charge limit; no records were skipped.");
                item.sourceSequence = ulong.Parse(item.record.recordSequence, CultureInfo.InvariantCulture);
                item.sourceUtc = DateTime.Parse(item.record.utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                items.Add(item); charge += item.fixtureCharge;
                if (clock.Elapsed.TotalSeconds >= nextSample)
                {
                    long current = WorkingSet(process, out _); if (current <= 0) unavailable++;
                    peak = Math.Max(peak, current); nextSample = clock.Elapsed.TotalSeconds + 1;
                }
            }
            long after = WorkingSet(process, out _); if (after <= 0) unavailable++;
            peak = Math.Max(peak, after);
            return new TemplateSet(items.ToArray(), new { sourceCycleSeconds = (double)manifest["cycleSeconds"], records = items.Count,
                chargeBytes = charge, maximumItems, maximumChargeBytes, loadSeconds = clock.Elapsed.TotalSeconds,
                processWorkingSetBeforeBytes = before, processWorkingSetAfterBytes = after, peakSampledProcessWorkingSetBytes = peak,
                processMemorySource = memorySource, processMemoryUnavailableSamples = unavailable,
                accounting = "One private source-cycle template shared read-only across repeats, outside Store and prefetch budgets. Fixed 400000-item / 4GiB source-size charge limits. Charge (2048 + 8*UTF8 bytes per original line) estimates retained data, not measured managed heap. Loading includes transient JSON graphs. Each queued non-shared payload and Advance boundary is independently cloned; production Store work is not pre-executed. Working set is the whole Editor, not template-only memory." });
        }
        private static IEnumerable<Prepared> ReadTemplates(TemplateSet templates, JObject manifest, double duration)
        {
            bool offsetClocks = ClockMapping(manifest) == "offset";
            double period = (double)manifest["cycleSeconds"];
            for (int cycle = 0; cycle * period < duration; cycle++)
            {
                string identity = EvidenceJson.Hash(Encoding.UTF8.GetBytes((string)manifest["fixtureId"] + ":" + cycle));
                string run = "mixed-" + identity.Substring(0, 24), capture = identity.Substring(24, 32);
                double offset = cycle * period;
                long tickOffset = (long)Math.Round(offset * 10000000);
                foreach (var source in templates.items)
                {
                    double due = source.due + offset;
                    if (due >= duration) yield break;
                    var record = source.record.Copy();
                    record.input = CloneFixtureValue(source.record.input);
                    record.before = CloneFixtureValue(source.record.before);
                    record.after = CloneFixtureValue(source.record.after);
                    if (source.record.completion != null)
                    {
                        var completion = source.record.completion;
                        record.completion = new DiagnosticCompletion { sequence = completion.sequence, utc = completion.utc,
                            monotonic = completion.monotonic, network = completion.network, frame = completion.frame,
                            fixedStep = completion.fixedStep, estimatedBytes = completion.estimatedBytes };
                    }
                    record.captureId = capture; record.runId = run;
                    record.recordSequence = (source.sourceSequence + (ulong)cycle * (ulong)manifest["sequenceSpan"]).ToString(CultureInfo.InvariantCulture);
                    if (offsetClocks)
                    {
                        record.monotonicTime += offset; record.networkTime += offset;
                        record.frame += cycle * (int)manifest["frameSpan"]; record.fixedStep += cycle * (int)manifest["fixedStepSpan"];
                        record.utc = source.sourceUtc.AddTicks(tickOffset).ToString("o", CultureInfo.InvariantCulture);
                    }
                    var advance = source.advance;
                    if (source.kind == "advance")
                    {
                        advance.sequence += (ulong)cycle * (ulong)manifest["sequenceSpan"];
                        if (offsetClocks)
                        {
                            advance.utcTicks += tickOffset; advance.monotonic += offset; advance.network += offset;
                            advance.frame += cycle * (int)manifest["frameSpan"]; advance.fixedStep += cycle * (int)manifest["fixedStepSpan"];
                        }
                        advance.boundary = CloneBoundary(source.advance.boundary);
                    }
                    yield return new Prepared { record = record, advance = advance, kind = source.kind, due = due,
                        shared = (JArray)source.shared.DeepClone(), fixtureCharge = source.fixtureCharge };
                }
            }
        }
        private static object CloneFixtureValue(object value)
        {
            if (value is JToken token) return token.DeepClone();
            if (value == null || value is string || value.GetType().IsValueType) return value;
            throw new InvalidDataException("Unsupported mutable template payload type: " + value.GetType().FullName);
        }
        private static StatusReplayBoundary CloneBoundary(StatusReplayBoundary value) => value == null ? null : new StatusReplayBoundary {
            ids = value.ids == null ? null : new EventSequenceState { slot = value.ids.slot, epoch = value.ids.epoch, next = value.ids.next },
            eventIds = value.eventIds, supported = value.supported, executeAll = value.executeAll,
            offline = value.offline, server = value.server, localPlayer = value.localPlayer, targetOwner = value.targetOwner };
        private static string ClockMapping(JObject manifest)
        {
            string mode = (string)manifest["recordClockMapping"] ?? "offset";
            if (mode != "offset" && mode != "preserve-source") throw new InvalidDataException("Unsupported recordClockMapping: " + mode);
            return mode;
        }
        private sealed class PreparedBuffer
        {
            private readonly Queue<Prepared> items = new();
            private readonly object gate = new();
            private readonly int limit;
            private readonly long byteLimit;
            private readonly ManualResetEventSlim ready;
            private readonly CancellationToken cancellation;
            private bool complete;
            private long bytes, peakBytes, fullWaits, fullTicks, maximumFullTicks, emptyWaits, emptyTicks, maximumEmptyTicks;
            private int peakCount;
            private double lastDue = -1;
            public PreparedBuffer(int limit, long byteLimit, ManualResetEventSlim ready, CancellationToken cancellation)
            { this.limit = limit; this.byteLimit = byteLimit; this.ready = ready; this.cancellation = cancellation; }
            public void Add(Prepared item)
            {
                if (item.fixtureCharge <= 0 || item.fixtureCharge > byteLimit)
                    throw new InvalidDataException("Fixture item exceeds the separate bounded prefetch charge.");
                lock (gate)
                {
                    long started = 0;
                    while (items.Count >= limit || bytes + item.fixtureCharge > byteLimit)
                    {
                        ready.Set();
                        if (started == 0) { started = Stopwatch.GetTimestamp(); fullWaits++; }
                        cancellation.ThrowIfCancellationRequested(); Monitor.Wait(gate, 100);
                    }
                    if (started != 0) { long ticks = Stopwatch.GetTimestamp() - started; fullTicks += ticks; maximumFullTicks = Math.Max(maximumFullTicks, ticks); }
                    cancellation.ThrowIfCancellationRequested();
                    if (complete) throw new InvalidOperationException("Fixture buffer was completed.");
                    items.Enqueue(item); bytes += item.fixtureCharge;
                    peakBytes = Math.Max(peakBytes, bytes); peakCount = Math.Max(peakCount, items.Count); lastDue = item.due;
                    Monitor.PulseAll(gate);
                }
            }
            public bool Take(out Prepared item, out double waitMilliseconds)
            {
                lock (gate)
                {
                    long started = 0;
                    while (items.Count == 0 && !complete)
                    {
                        if (started == 0) { started = Stopwatch.GetTimestamp(); emptyWaits++; }
                        cancellation.ThrowIfCancellationRequested(); Monitor.Wait(gate, 100);
                    }
                    long ticks = started == 0 ? 0 : Stopwatch.GetTimestamp() - started;
                    emptyTicks += ticks; maximumEmptyTicks = Math.Max(maximumEmptyTicks, ticks); waitMilliseconds = Milliseconds(ticks);
                    if (items.Count == 0) { item = null; return false; }
                    item = items.Dequeue(); bytes -= item.fixtureCharge; Monitor.PulseAll(gate); return true;
                }
            }
            public void Complete() { lock (gate) { complete = true; Monitor.PulseAll(gate); } }
            public void Wake() { lock (gate) Monitor.PulseAll(gate); }
            public object Snapshot()
            {
                lock (gate) return new { itemLimit = limit, chargeLimitBytes = byteLimit, count = items.Count, chargeBytes = bytes,
                    peakCount, peakChargeBytes = peakBytes, firstDueSeconds = items.Count == 0 ? (double?)null : items.Peek().due,
                    lastPreparedDueSeconds = lastDue, fullWaitCount = fullWaits, fullWaitMilliseconds = Milliseconds(fullTicks),
                    maximumFullWaitMilliseconds = Milliseconds(maximumFullTicks), emptyWaitCount = emptyWaits,
                    emptyWaitMilliseconds = Milliseconds(emptyTicks), maximumEmptyWaitMilliseconds = Milliseconds(maximumEmptyTicks) };
            }
        }
        private sealed class PacingWindow
        {
            public double startSeconds;
            public long planned, submitted, accepted, rejected;
            public double maximumLatenessSeconds, storeCallMilliseconds, readerWaitMilliseconds;
        }
        private static PacingWindow Window(SortedDictionary<int, PacingWindow> windows, double seconds)
        {
            int index = (int)Math.Floor(seconds * 10);
            if (!windows.TryGetValue(index, out var window)) windows.Add(index, window = new PacingWindow { startSeconds = index / 10d });
            return window;
        }
        private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
        private sealed class SharedLease { public SharedEvidencePayload payload; public int remaining; }
        private sealed class SharedBudgetException : Exception { }

        private static IEnumerable<Prepared> Read(string fixture, JObject manifest, double duration)
        {
            bool offsetClocks = ClockMapping(manifest) == "offset";
            using var locale = new ExactNumberReader.NumericLocale();
            double period = (double)manifest["cycleSeconds"], origin = (double)manifest["firstMonotonic"];
            for (int cycle = 0; cycle * period < duration; cycle++)
            {
                string identity = EvidenceJson.Hash(Encoding.UTF8.GetBytes((string)manifest["fixtureId"] + ":" + cycle));
                string run = "mixed-" + identity.Substring(0, 24), capture = identity.Substring(24, 32);
                double offset = cycle * period;
                using var raw = File.OpenRead(fixture);
                using var zipped = new GZipStream(raw, CompressionMode.Decompress);
                using var text = new StreamReader(zipped, Encoding.UTF8);
                string line;
                while ((line = text.ReadLine()) != null)
                {
                    JObject wrapper = Parse(line, locale), body = (JObject)wrapper["record"];
                    var record = EvidenceJson.Convert<DiagnosticRecord>(body);
                    // Newtonsoft's untyped-object materialization normalizes nested
                    // negative-zero doubles even from an exact JTokenReader. Restore
                    // these detached payloads from their original token values.
                    record.input = ReadFixtureValue(body["input"]);
                    record.before = ReadFixtureValue(body["before"]);
                    record.after = ReadFixtureValue(body["after"]);
                    double due = record.monotonicTime - origin + offset;
                    // Frozen source sequence order is monotonic. Once the requested
                    // interval ends, stop reading; draining the rest of the fixture
                    // would give the writer an unmeasured catch-up period.
                    if (due >= duration) yield break;
                    if (record.inputRef != null || record.checkpointRef != null || body.Descendants().OfType<JProperty>().Any(p => p.Name == "$evidenceRef"))
                        throw new InvalidDataException("Fixture must reconstruct dependencies; copying old file references is forbidden.");
                    record.captureId = capture; record.runId = run;
                    record.recordSequence = (ulong.Parse(record.recordSequence) + (ulong)cycle * (ulong)manifest["sequenceSpan"]).ToString(CultureInfo.InvariantCulture);
                    if (offsetClocks)
                    {
                        record.monotonicTime += offset; record.networkTime += offset;
                        record.frame += cycle * (int)manifest["frameSpan"]; record.fixedStep += cycle * (int)manifest["fixedStepSpan"];
                        record.utc = DateTime.Parse(record.utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                            .AddTicks((long)Math.Round(offset * 10000000)).ToString("o", CultureInfo.InvariantCulture);
                    }
                    string kind = (string)wrapper["kind"];
                    // Detach descriptors too: a child JToken retains its entire JSON
                    // wrapper through Parent, including the unused expanded Advance.
                    var item = new Prepared { record = record, kind = kind, shared = (JArray)wrapper["shared"].DeepClone(), due = due,
                        fixtureCharge = 2048L + 8L * Encoding.UTF8.GetByteCount(line) };
                    if (kind == "advance")
                    {
                        item.advance = Advance(record);
                        // These expanded fixture graphs are only needed to restore the
                        // native Advance. The actual Store call still occurs on schedule.
                        record.input = null; record.before = null; record.after = null;
                    }
                    else if (kind != "record") throw new InvalidDataException("Unknown fixture call kind.");
                    yield return item;
                }
            }
        }

        private static DiagnosticAdvance Advance(DiagnosticRecord record)
        {
            int phase = record.stage == "replay.input" ? 0 : record.stage == "replay.output" && record.outcome == "Completed" ? 1 :
                record.stage == "replay.output" && record.outcome == "Aborted" ? 2 : throw new InvalidDataException("Invalid Advance stage.");
            return new DiagnosticAdvance { role = record.role, engine = record.engine, operation = record.operation,
                sequence = ulong.Parse(record.recordSequence), utcTicks = DateTime.Parse(record.utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).Ticks,
                monotonic = record.monotonicTime, network = record.networkTime, frame = record.frame, fixedStep = record.fixedStep,
                phase = phase, delta = phase == 0 ? (float)((JArray)record.input)[0] : 0,
                boundary = record.before == null ? null : EvidenceJson.Convert<StatusReplayBoundary>((JToken)record.before) };
        }
        private static object ReadFixtureValue(JToken token) => token is JValue scalar ? scalar.Value : token?.DeepClone();

        private static void MaterializeShared(Prepared item, JObject manifest, DiagnosticMemoryBudget memory, Dictionary<string, SharedLease> leases)
        {
            if (item.kind != "record") throw new InvalidDataException("Unknown fixture call kind.");
            if (item.shared.Count == 0) return;
            if (item.shared.Count != 1) throw new InvalidDataException("Unsupported multiple shared input shape.");
            var descriptor = item.shared[0];
            if ((string)descriptor["field"] != "input" || (string)descriptor["kind"] != "canonical")
                throw new InvalidDataException("Unsupported shared type.");
            string group = (string)descriptor["group"];
            JToken payload;
            bool receive = item.record.stage == "network.canonical" && descriptor["path"].ToString(Formatting.None) == "[\"batch\"]";
            bool apply = item.record.stage == "replay.input" && item.record.operation == "Apply" && descriptor["path"].ToString(Formatting.None) == "[0]";
            if (!receive && !apply) throw new InvalidDataException("Unsupported shared location.");
            payload = receive ? ((JObject)item.record.input)["batch"] : ((JArray)item.record.input)[0];
            if (!leases.TryGetValue(group, out var lease))
            {
                if (!receive) throw new InvalidDataException("Missing receive for shared Apply.");
                if (!SharedEvidencePayload.TryCapture(EvidenceJson.Convert<CanonicalWorldBatch>(payload), memory, out var shared))
                    throw new SharedBudgetException();
                lease = new SharedLease { payload = shared, remaining = (int)manifest["sharedGroups"][group]["uses"] };
                leases.Add(group, lease);
            }
            if (receive) item.record.input = new CanonicalReceiveEvidence { incomingRound = (uint)((JObject)item.record.input)["incomingRound"], batch = lease.payload };
            else item.record.input = new object[] { lease.payload };
        }

        private static void ReleaseCreatorUses(Prepared item, Dictionary<string, SharedLease> leases)
        {
            foreach (var descriptor in item.shared)
            {
                string group = (string)descriptor["group"];
                if (leases.TryGetValue(group, out var lease) && --lease.remaining == 0)
                { lease.payload.Dispose(); leases.Remove(group); }
            }
        }

        [Test] public void AdvanceReconstructionKeepsNativePhaseBoundaryAndPrecision()
        {
            var original = new DiagnosticAdvance { role = "status", engine = "status-1", operation = "Advance", sequence = 7,
                utcTicks = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc).Ticks + 1234567,
                monotonic = 2.34567891, network = 1.23456789, frame = 144, fixedStep = 50, delta = .007f,
                boundary = new StatusReplayBoundary { supported = true, executeAll = true } };
            var expanded = original.Expand("capture", "run", 1);
            var detached = EvidenceJson.Convert<DiagnosticRecord>(Parse(EvidenceJson.Encode(expanded)));
            Assert.That(EvidenceJson.Encode(Advance(detached).Expand("capture", "run", 1)), Is.EqualTo(EvidenceJson.Encode(expanded)));
        }

        [Test] public void PreparedBufferByteBackpressurePreservesOrderAndReleasesCharge()
        {
            using var ready = new ManualResetEventSlim();
            using var added = new ManualResetEventSlim();
            using var cancellation = new CancellationTokenSource();
            var buffer = new PreparedBuffer(2, 10, ready, cancellation.Token);
            var first = new Prepared { due = 1, fixtureCharge = 6 };
            var second = new Prepared { due = 2, fixtureCharge = 6 };
            buffer.Add(first);
            Exception failure = null;
            var thread = new Thread(() => { try { buffer.Add(second); added.Set(); buffer.Complete(); } catch (Exception error) { failure = error; } });
            thread.Start();
            try
            {
                Assert.That(ready.Wait(5000), Is.True, "Byte pressure must release startup before waiting for a consumer.");
                Assert.That(added.IsSet, Is.False, "Two items fit the count limit but must not exceed the byte charge.");
                Assert.That(buffer.Take(out var actual, out _), Is.True); Assert.That(actual, Is.SameAs(first));
                Assert.That(added.Wait(5000), Is.True);
                Assert.That(buffer.Take(out actual, out _), Is.True); Assert.That(actual, Is.SameAs(second));
                Assert.That(buffer.Take(out _, out _), Is.False);
                var stats = Parse(EvidenceJson.Encode(buffer.Snapshot()));
                Assert.That((long)stats["chargeBytes"], Is.Zero);
                Assert.That((long)stats["peakChargeBytes"], Is.EqualTo(6));
                Assert.That((long)stats["fullWaitCount"], Is.EqualTo(1));
                Assert.That(failure, Is.Null);
            }
            finally { cancellation.Cancel(); buffer.Wake(); Assert.That(thread.Join(5000), Is.True); }
        }

        [Test] public void PreparedAdvanceRestoresBeforeScheduledSubmission()
        {
            string fixture = Path.Combine(Path.GetTempPath(), "evidence-mixed-prepared-" + Guid.NewGuid().ToString("N") + ".gz");
            var manifest = Parse("{\"fixtureId\":\"prepared-test\",\"cycleSeconds\":20,\"firstMonotonic\":226.8566091,\"sequenceSpan\":3,\"frameSpan\":2,\"fixedStepSpan\":2}");
            var original = new DiagnosticAdvance { role = "status", engine = "status-1", operation = "Advance", sequence = 3301,
                utcTicks = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc).Ticks + 1234567,
                monotonic = 226.8566091, network = 1.23456789, frame = 144, fixedStep = 50, delta = .007f,
                boundary = new StatusReplayBoundary { supported = true, executeAll = true } };
            try
            {
                using (var raw = File.Create(fixture))
                using (var zipped = new GZipStream(raw, CompressionMode.Compress))
                using (var text = new StreamWriter(zipped, new UTF8Encoding(false)))
                    text.WriteLine(EvidenceJson.Encode(new { kind = "advance", shared = new object[0], record = original.Expand("capture", "run", 1) }));
                var item = Read(fixture, manifest, 1).Single();
                Assert.That(item.record.input, Is.Null, "Discard the expanded fixture input only after native reconstruction.");
                Assert.That(item.record.before, Is.Null);
                Assert.That(EvidenceJson.Encode(item.advance.Expand(item.record.captureId, item.record.runId, 1)),
                    Is.EqualTo(EvidenceJson.Encode(original.Expand(item.record.captureId, item.record.runId, 1))));
                Assert.That(item.fixtureCharge, Is.GreaterThan(0));
            }
            finally { if (File.Exists(fixture)) File.Delete(fixture); }
        }

        [Test] public void ReusedNumericLocaleSurvivesIndividualReaderDisposal()
        {
            using var locale = new ExactNumberReader.NumericLocale();
            foreach (string json in new[] { "{\"v\":226.8566091}", "{\"escaped\":\"\\\"5e-324\\\"\",\"v\":226.8566091}" })
                Assert.That(BitConverter.DoubleToInt64Bits((double)Parse(json, locale)["v"]), Is.EqualTo(0x406c5b69577cbe97L));
            Assert.Throws<JsonReaderException>(() => Parse("{\"v\":broken}", locale));
            Assert.That(BitConverter.DoubleToInt64Bits((double)Parse("{\"v\":-0.0}", locale)["v"]), Is.EqualTo(long.MinValue));
        }

        [Test] public void TemplateMappingMatchesStreamingAndIsolatesPayloads()
        {
            string fixture = Path.Combine(Path.GetTempPath(), "evidence-template-map-" + Guid.NewGuid().ToString("N") + ".gz");
            var manifest = Parse("{\"fixtureId\":\"template-test\",\"cycleSeconds\":1,\"firstMonotonic\":10,\"sequenceSpan\":3,\"frameSpan\":7,\"fixedStepSpan\":5}");
            var normal = new DiagnosticRecord { recordSequence = "41", runId = "source", captureId = "source-capture", round = 7,
                role = "replica", stage = "replay.input", outcome = "Observed", reason = "reason", engine = "engine", operation = "Apply",
                eventId = "18446744073709551615", rootEventId = "root", parentEventId = "parent", statusInstanceId = "status", entityGeneration = "generation",
                source = 9, target = 8, connectionEpoch = 7, assignmentEpoch = 6, batchSequence = 5, serverSequence = 4,
                stateVersion = 3, applicationRevision = 2, tickIndex = 1, frame = 12, fixedStep = 13,
                monotonicTime = 10, networkTime = 1.23456789, utc = "2026-09-25T03:16:29.0928533Z", critical = true, estimatedBytes = 4096,
                input = Parse("{\"nested\":[{\"exact\":226.8566091,\"negativeZero\":-0.0,\"text\":\"\\u4e2d\\u6587\"}]}"),
                before = new JArray(1, 2), after = Parse("{\"state\":[3,4]}"),
                completion = new DiagnosticCompletion { sequence = "42", utc = "2026-09-25T03:16:29.0928534Z", monotonic = 10.01,
                    network = 1.25, frame = 13, fixedStep = 14, estimatedBytes = 512 } };
            // Preserve this test fixture's literal spelling across Mono's JSON writer,
            // which otherwise formats a negative-zero double as positive zero.
            ((JObject)normal.input)["nested"][0]["negativeZero"] = new JRaw("-0.0");
            var advance = new DiagnosticAdvance { role = "status", engine = "status", operation = "Advance", sequence = 42,
                utcTicks = new DateTime(2026, 9, 25, 3, 16, 29, DateTimeKind.Utc).Ticks + 2234567,
                monotonic = 10.1, network = 1.23456789, frame = 14, fixedStep = 15, delta = .007f,
                boundary = new StatusReplayBoundary { ids = new EventSequenceState { slot = 17, epoch = 18, next = uint.MaxValue },
                    eventIds = true, supported = true, executeAll = true, offline = true, server = true, localPlayer = 19, targetOwner = 20 } };
            try
            {
                WriteFixture(fixture, new { kind = "record", shared = new object[0], record = normal },
                    new { kind = "advance", shared = new object[0], record = advance.Expand("capture", "run", 7) });
                var templates = LoadTemplates(fixture, manifest);
                Assert.That(templates.items.All(item => item.shared.Parent == null), Is.True,
                    "A descriptor must not retain the entire original JSON wrapper through Parent.");
                var reference = Read(fixture, manifest, 2.5).ToArray();
                var actual = ReadTemplates(templates, manifest, 2.5).ToArray();
                Assert.That(actual.Length, Is.EqualTo(reference.Length));
                for (int i = 0; i < actual.Length; i++)
                {
                    Assert.That(EvidenceJson.Encode(Logical(actual[i])), Is.EqualTo(EvidenceJson.Encode(Logical(reference[i]))), "Entire logical record " + i);
                    Assert.That(BitConverter.DoubleToInt64Bits(actual[i].due), Is.EqualTo(BitConverter.DoubleToInt64Bits(reference[i].due)));
                }
                var first = (JObject)actual[0].record.input;
                Assert.That(BitConverter.DoubleToInt64Bits((double)first["nested"][0]["negativeZero"]),
                    Is.EqualTo(long.MinValue), "Explicit fixture -0.0 must survive Read, template preparation and clone.");
                Assert.That(BitConverter.DoubleToInt64Bits((double)((JObject)reference[0].record.input)["nested"][0]["negativeZero"]), Is.EqualTo(long.MinValue));
                var exactClone = (JObject)CloneFixtureValue(Parse("{\"v\":-0.0,\"n\":226.8566091}"));
                Assert.That(BitConverter.DoubleToInt64Bits((double)exactClone["v"]), Is.EqualTo(long.MinValue));
                Assert.That(BitConverter.DoubleToInt64Bits((double)exactClone["n"]), Is.EqualTo(0x406c5b69577cbe97L));
                first["nested"][0]["exact"] = -1d;
                ((JArray)actual[0].record.before)[0] = -1;
                ((JObject)actual[0].record.after)["state"][0] = -1;
                actual[0].record.completion.sequence = "mutated";
                actual[1].advance.boundary.ids.slot = 100;
                actual[1].advance.boundary.server = false;
                Assert.That(EvidenceJson.Encode(Logical(actual[2])), Is.EqualTo(EvidenceJson.Encode(Logical(reference[2]))), "Next cycle must own a detached graph.");
                Assert.That(EvidenceJson.Encode(Logical(actual[3])), Is.EqualTo(EvidenceJson.Encode(Logical(reference[3]))), "Advance boundary must also be detached.");
                var reread = ReadTemplates(templates, manifest, 2.5).ToArray();
                for (int i = 0; i < reread.Length; i++)
                    Assert.That(EvidenceJson.Encode(Logical(reread[i])), Is.EqualTo(EvidenceJson.Encode(Logical(reference[i]))), "Template must remain unchanged.");
            }
            finally { if (File.Exists(fixture)) File.Delete(fixture); }
        }

        [Test] public void TemplateCapacityFailureDoesNotSkipRecords()
        {
            string fixture = Path.Combine(Path.GetTempPath(), "evidence-template-limit-" + Guid.NewGuid().ToString("N") + ".gz");
            var manifest = Parse("{\"fixtureId\":\"limit-test\",\"cycleSeconds\":1,\"firstMonotonic\":10,\"sequenceSpan\":2,\"frameSpan\":1,\"fixedStepSpan\":1}");
            var row = new { kind = "record", shared = new object[0], record = new DiagnosticRecord {
                recordSequence = "1", monotonicTime = 10, utc = "2026-09-25T03:16:29.0928533Z" } };
            try
            {
                WriteFixture(fixture, row, row);
                Assert.Throws<InvalidDataException>(() => LoadTemplates(fixture, manifest, maximumItems: 1));
                Assert.Throws<InvalidDataException>(() => LoadTemplates(fixture, manifest, maximumChargeBytes: 1));
                Assert.That(LoadTemplates(fixture, manifest).items.Length, Is.EqualTo(2));
            }
            finally { if (File.Exists(fixture)) File.Delete(fixture); }
        }

        [Test] public void TemplatePreservesSourceClocksWithIndependentSchedule()
        {
            string fixture = Path.Combine(Path.GetTempPath(), "evidence-template-clock-" + Guid.NewGuid().ToString("N") + ".gz");
            var manifest = Parse("{\"fixtureId\":\"clock-test\",\"recordClockMapping\":\"preserve-source\",\"cycleSeconds\":1.25,\"firstMonotonic\":10.125,\"sequenceSpan\":2,\"frameSpan\":7,\"fixedStepSpan\":5}");
            try
            {
                using (var raw = File.Create(fixture))
                using (var zipped = new GZipStream(raw, CompressionMode.Compress))
                using (var text = new StreamWriter(zipped, new UTF8Encoding(false)))
                {
                    // Explicit text avoids the legacy Mono JSON writer normalizing
                    // negative zero before this end-to-end reader test begins.
                    text.WriteLine("{\"kind\":\"record\",\"shared\":[],\"record\":{\"recordSequence\":\"1\",\"round\":1,\"stage\":\"network.message\",\"monotonicTime\":10.125,\"networkTime\":-0.0,\"utc\":\"2026-09-25T03:16:29.0928533Z\",\"frame\":9,\"fixedStep\":10,\"input\":{\"v\":-0.0}}}");
                    text.WriteLine("{\"kind\":\"advance\",\"shared\":[],\"record\":{\"recordSequence\":\"2\",\"round\":1,\"role\":\"replica\",\"engine\":\"engine\",\"operation\":\"Advance\",\"stage\":\"replay.output\",\"outcome\":\"Completed\",\"monotonicTime\":10.25,\"networkTime\":-0.0,\"utc\":\"2026-09-25T03:16:29.0928534Z\",\"frame\":11,\"fixedStep\":12}}");
                }
                var templates = LoadTemplates(fixture, manifest);
                var reference = Read(fixture, manifest, 2).ToArray();
                var actual = ReadTemplates(templates, manifest, 2).ToArray();
                Assert.That(actual.Length, Is.EqualTo(4));
                for (int i = 0; i < actual.Length; i++)
                {
                    var record = Logical(actual[i]);
                    Assert.That(EvidenceJson.Encode(record), Is.EqualTo(EvidenceJson.Encode(Logical(reference[i]))));
                    Assert.That(BitConverter.DoubleToInt64Bits(record.networkTime), Is.EqualTo(long.MinValue), "Preserve means no addition, including +0.0.");
                    Assert.That(record.monotonicTime, Is.EqualTo(i % 2 == 0 ? 10.125 : 10.25));
                    Assert.That(record.frame, Is.EqualTo(i % 2 == 0 ? 9 : 11));
                    Assert.That(record.fixedStep, Is.EqualTo(i % 2 == 0 ? 10 : 12));
                    Assert.That(record.utc, Is.EqualTo(i % 2 == 0 ? "2026-09-25T03:16:29.0928533Z" : "2026-09-25T03:16:29.0928534Z"));
                    Assert.That(record.recordSequence, Is.EqualTo((i + 1).ToString(CultureInfo.InvariantCulture)));
                    Assert.That(actual[i].due, Is.EqualTo(i / 2 * 1.25 + (i % 2 == 0 ? 0 : .125)));
                }
                Assert.That(BitConverter.DoubleToInt64Bits((double)((JObject)actual[2].record.input)["v"]), Is.EqualTo(long.MinValue));
                Assert.That(actual[0].record.captureId, Is.Not.EqualTo(actual[2].record.captureId));
                manifest["recordClockMapping"] = "unknown";
                Assert.Throws<InvalidDataException>(() => Read(fixture, manifest, 2).ToArray());
                Assert.Throws<InvalidDataException>(() => ReadTemplates(templates, manifest, 2).ToArray());
            }
            finally { if (File.Exists(fixture)) File.Delete(fixture); }
        }

        [Test] public void TemplateClonesPreserveSharedPairAfterStoreSubmission()
        {
            string output = Path.Combine(Path.GetTempPath(), "evidence-template-shared-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            string fixture = Path.Combine(output, "fixture.gz");
            var manifest = Parse("{\"fixtureId\":\"shared-test\",\"cycleSeconds\":1,\"firstMonotonic\":10,\"sequenceSpan\":2,\"frameSpan\":1,\"fixedStepSpan\":1,\"sharedGroups\":{\"canonical-1\":{\"uses\":2}}}");
            var batch = Parse("{\"ServerSequence\":7,\"Entities\":[],\"Statuses\":[],\"ConfirmedKills\":[],\"EnemyHitPresentations\":[]}");
            var received = new DiagnosticRecord { runId = "run", captureId = "capture", round = 1, recordSequence = "1", stage = "network.canonical",
                monotonicTime = 10, utc = "2026-09-25T03:16:29.0928533Z", input = new JObject { ["incomingRound"] = 1, ["batch"] = batch.DeepClone() }, estimatedBytes = 2048 };
            var applied = received.Copy(); applied.recordSequence = "2"; applied.monotonicTime = 10.1; applied.stage = "replay.input";
            applied.operation = "Apply"; applied.engine = "replica-1"; applied.input = new JArray(batch.DeepClone());
            var memory = new DiagnosticMemoryBudget(); var leases = new Dictionary<string, SharedLease>();
            try
            {
                WriteFixture(fixture,
                    new { kind = "record", shared = new[] { new { field = "input", kind = "canonical", group = "canonical-1", path = new[] { "batch" } } }, record = received },
                    new { kind = "record", shared = new[] { new { field = "input", kind = "canonical", group = "canonical-1", path = new[] { 0 } } }, record = applied });
                var templates = LoadTemplates(fixture, manifest);
                string original = EvidenceJson.Encode(templates.items.Select(Logical).ToArray());
                using var store = new CombatEvidenceStore(Path.Combine(output, "capture"), new EvidenceStoreOptions { Memory = memory });
                SharedEvidencePayload current = null;
                foreach (var item in ReadTemplates(templates, manifest, 2))
                {
                    MaterializeShared(item, manifest, memory, leases);
                    if (item.record.stage == "network.canonical") current = (SharedEvidencePayload)((CanonicalReceiveEvidence)item.record.input).batch;
                    else Assert.That(((object[])item.record.input)[0], Is.SameAs(current));
                    Assert.That(store.TryWrite(item.record, true), Is.True);
                    ReleaseCreatorUses(item, leases);
                }
                store.RequestClose(); Assert.That(store.WaitForClose(), Is.True);
                Assert.That(leases, Is.Empty); Assert.That(memory.Used, Is.Zero);
                Assert.That(store.LastFailure, Is.Null); Assert.That(store.Dropped, Is.Zero);
                Assert.That(Directory.GetFiles(Path.Combine(output, "capture"), "*.json.gz", SearchOption.AllDirectories), Has.Length.EqualTo(2));
                Assert.That(EvidenceJson.Encode(templates.items.Select(Logical).ToArray()), Is.EqualTo(original));
            }
            finally
            {
                foreach (var lease in leases.Values) lease.payload.Dispose();
                if (Directory.Exists(output)) Directory.Delete(output, true);
            }
        }

        private static DiagnosticRecord Logical(Prepared item) => item.kind == "advance"
            ? item.advance.Expand(item.record.captureId, item.record.runId, item.record.round) : item.record;
        private static void WriteFixture(string path, params object[] rows)
        {
            using var raw = File.Create(path); using var zipped = new GZipStream(raw, CompressionMode.Compress);
            using var text = new StreamWriter(zipped, new UTF8Encoding(false));
            foreach (var row in rows) text.WriteLine(EvidenceJson.Encode(row));
        }

        [Test] public void FixtureParserDoesNotTruncateUtcTicksOrUnsignedIds()
        {
            JObject token = Parse("{\"utc\":\"2026-09-25T03:16:29.0928533Z\",\"id\":\"18446744073709551615\"}");
            Assert.That(token["utc"].Type, Is.EqualTo(JTokenType.String));
            Assert.That((string)token["utc"], Does.EndWith("0928533Z"));
            Assert.That((string)token["id"], Is.EqualTo(ulong.MaxValue.ToString(CultureInfo.InvariantCulture)));
        }

        [Test] public void FixtureParserPreservesExactFloatingBitsFromRealAdvanceRecord()
        {
            // Client source sequence 3301. Mono double.Parse rounds this decimal
            // one ULP high; the original Advance binary contains ...be97.
            var token = Parse("{\n  \"monotonicTime\":226.8566091,\"values\":[-226.8566091,5e-324,1.7976931348623157e308,-0.0,1.25e+3],\"text\":\"226.8566091\"\n}");
            Assert.That(BitConverter.DoubleToInt64Bits((double)token["monotonicTime"]), Is.EqualTo(0x406c5b69577cbe97L));
            Assert.That(BitConverter.DoubleToInt64Bits((double)token["values"][0]), Is.EqualTo(unchecked((long)0xc06c5b69577cbe97UL)));
            Assert.That(BitConverter.DoubleToInt64Bits((double)token["values"][1]), Is.EqualTo(1L));
            Assert.That(BitConverter.DoubleToInt64Bits((double)token["values"][2]), Is.EqualTo(0x7fefffffffffffffL));
            Assert.That(BitConverter.DoubleToInt64Bits((double)token["values"][3]), Is.EqualTo(long.MinValue));
            Assert.That((double)token["values"][4], Is.EqualTo(1250d));
            Assert.That((string)token["text"], Is.EqualTo("226.8566091"));
            string escaped = "line\n\"226.8566091\" \\ 5e-324";
            token = Parse("{\"text\":" + JsonConvert.SerializeObject(escaped) + ",\"value\":1.25e-3}");
            Assert.That((string)token["text"], Is.EqualTo(escaped));
            Assert.That(BitConverter.DoubleToInt64Bits((double)token["value"]), Is.EqualTo(BitConverter.DoubleToInt64Bits(.00125)));
        }

        [Test] public void RealAdvanceDoubleBitsSurviveMixedReaderAndStore()
        {
            string output = Path.Combine(Path.GetTempPath(), "evidence-mixed-exact-" + Guid.NewGuid().ToString("N"));
            var record = EvidenceJson.Convert<DiagnosticRecord>(Parse("{\"captureId\":\"capture\",\"runId\":\"run\",\"round\":1," +
                "\"recordSequence\":\"3301\",\"stage\":\"replay.output\",\"outcome\":\"Completed\",\"role\":\"replica\",\"engine\":\"replica-1\"," +
                "\"operation\":\"controller.6.Advance\",\"monotonicTime\":226.8566091,\"networkTime\":134.3755368275345," +
                "\"utc\":\"2026-09-25T03:16:33.0252674Z\",\"frame\":7837,\"fixedStep\":6657}"));
            try
            {
                using var store = new CombatEvidenceStore(output);
                Assert.That(store.TryWriteAdvance(record.captureId, record.runId, record.round, Advance(record)), Is.True);
                store.RequestClose(); Assert.That(store.WaitForClose(), Is.True);
                var actual = Directory.GetFiles(output, "events-*.jsonl", SearchOption.AllDirectories)
                    .SelectMany(File.ReadLines).SelectMany(EvidenceBlocks.Decode).Single();
                Assert.That(BitConverter.DoubleToInt64Bits(actual.monotonicTime), Is.EqualTo(0x406c5b69577cbe97L));
                Assert.That(actual.recordSequence, Is.EqualTo("3301"));
                Assert.That(actual.utc, Is.EqualTo("2026-09-25T03:16:33.0252674Z"));
                Assert.That(store.LastFailure, Is.Null);
            }
            finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
        }

        [Test] public void ProcessMemorySamplerReportsActualBytesOrExplicitUnavailable()
        {
            using var process = Process.GetCurrentProcess();
            long working = WorkingSet(process, out string source);
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Assert.That(source, Is.EqualTo("Win32GetProcessMemoryInfo"));
                Assert.That(working, Is.GreaterThan(0));
            }
            else Assert.That(working, Is.Not.EqualTo(0), "Unavailable memory must not be reported as a real zero-byte process.");
        }

        [Test] public void PartialIntervalStopsBeforeReadingUnscheduledFixtureTail()
        {
            string fixture = Path.Combine(Path.GetTempPath(), "evidence-mixed-cutoff-" + Guid.NewGuid().ToString("N") + ".gz");
            var manifest = Parse("{\"fixtureId\":\"cutoff-test\",\"cycleSeconds\":20,\"firstMonotonic\":10,\"sequenceSpan\":3,\"frameSpan\":2,\"fixedStepSpan\":2}");
            try
            {
                using (var raw = File.Create(fixture))
                using (var zipped = new GZipStream(raw, CompressionMode.Compress))
                using (var text = new StreamWriter(zipped, new UTF8Encoding(false)))
                {
                    foreach (int sequence in new[] { 1, 2 })
                        text.WriteLine(EvidenceJson.Encode(new { kind = "record", shared = new object[0], record = new DiagnosticRecord {
                            runId = "original", captureId = "capture", round = 1, recordSequence = sequence.ToString(), stage = "network.message",
                            monotonicTime = sequence == 1 ? 10 : 16, utc = "2026-09-25T03:16:29.0928533Z" } }));
                    text.WriteLine("This malformed tail is outside the requested interval and must never be parsed.");
                }
                var selected = Read(fixture, manifest, 5).ToArray();
                Assert.That(selected, Has.Length.EqualTo(1));
                Assert.That(selected[0].record.recordSequence, Is.EqualTo("1"));
                Assert.That(selected[0].due, Is.Zero);
            }
            finally { if (File.Exists(fixture)) File.Delete(fixture); }
        }

        [Test] public void KnownSharedPairUsesRealLeasesAndOneDurableDependency()
        {
            string output = Path.Combine(Path.GetTempPath(), "evidence-mixed-shared-" + Guid.NewGuid().ToString("N"));
            var memory = new DiagnosticMemoryBudget();
            var leases = new Dictionary<string, SharedLease>();
            var manifest = Parse("{\"sharedGroups\":{\"canonical-1\":{\"uses\":2}}}");
            var batch = Parse("{\"ServerSequence\":7,\"Entities\":[],\"Statuses\":[],\"ConfirmedKills\":[],\"EnemyHitPresentations\":[]}");
            var received = new Prepared { kind = "record", record = new DiagnosticRecord {
                captureId = "capture", runId = "run", round = 1, recordSequence = "1", stage = "network.canonical",
                input = new JObject { ["incomingRound"] = 1, ["batch"] = batch.DeepClone() }, estimatedBytes = 2048 },
                shared = (JArray)Parse("{\"items\":[{\"field\":\"input\",\"kind\":\"canonical\",\"group\":\"canonical-1\",\"path\":[\"batch\"]}]}")["items"] };
            var applied = new Prepared { kind = "record", record = new DiagnosticRecord {
                captureId = "capture", runId = "run", round = 1, recordSequence = "2", stage = "replay.input", operation = "Apply", engine = "replica-1",
                input = new JArray(batch.DeepClone()), estimatedBytes = 680 },
                shared = (JArray)Parse("{\"items\":[{\"field\":\"input\",\"kind\":\"canonical\",\"group\":\"canonical-1\",\"path\":[0]}]}")["items"] };
            try
            {
                using var store = new CombatEvidenceStore(output, new EvidenceStoreOptions { Memory = memory });
                MaterializeShared(received, manifest, memory, leases);
                var handle = ((CanonicalReceiveEvidence)received.record.input).batch;
                Assert.That(handle, Is.TypeOf<SharedEvidencePayload>());
                Assert.That(store.TryWrite(received.record), Is.True);
                ReleaseCreatorUses(received, leases);
                MaterializeShared(applied, manifest, memory, leases);
                Assert.That(((object[])applied.record.input)[0], Is.SameAs(handle));
                Assert.That(store.TryWrite(applied.record), Is.True);
                ReleaseCreatorUses(applied, leases);
                Assert.That(leases, Is.Empty);
                store.RequestClose(); Assert.That(store.WaitForClose(), Is.True);
                Assert.That(store.Dropped, Is.Zero); Assert.That(store.LastFailure, Is.Null);
                Assert.That(memory.Used, Is.Zero);
                Assert.That(Directory.GetFiles(output, "*.json.gz", SearchOption.AllDirectories), Has.Length.EqualTo(1));
            }
            finally
            {
                foreach (var lease in leases.Values) lease.payload.Dispose();
                if (Directory.Exists(output)) Directory.Delete(output, true);
            }
        }

        private static JObject Parse(string value)
        {
            using var reader = new ExactNumberReader(value) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }
        private static JObject Parse(string value, ExactNumberReader.NumericLocale locale)
        {
            using var reader = new ExactNumberReader(value, locale) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }
        // The Windows benchmark must restore the same IEEE bits as the frozen
        // Python/source decoder. Mono's decimal-to-double parser is not always
        // correctly rounded, including short literals such as 226.8566091.
        // Re-read every floating token's original lexeme using an explicit C
        // numeric locale; strings, IDs, integers and original files stay intact.
        private sealed class ExactNumberReader : JsonTextReader
        {
            private readonly string json;
            private readonly int[] lines;
            private NumericLocale locale;
            private readonly bool ownsLocale;
            public ExactNumberReader(string value, NumericLocale sharedLocale = null) : base(new StringReader(value))
            {
                locale = sharedLocale; ownsLocale = sharedLocale == null;
                json = value; CloseInput = true;
                var starts = new List<int> { 0 };
                for (int i = 0; i < value.Length; i++)
                    if (value[i] == '\n' || value[i] == '\r' && (i + 1 == value.Length || value[i + 1] != '\n')) starts.Add(i + 1);
                lines = starts.ToArray();
            }
            public override bool Read()
            {
                bool read = base.Read();
                if (read && TokenType == JsonToken.Float)
                {
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                        throw new PlatformNotSupportedException("Exact frozen mixed benchmark numeric parsing requires Windows UCRT.");
                    int end = lines[LineNumber - 1] + LinePosition, start = end;
                    while (start > 0 && IsNumber(json[start - 1])) start--;
                    if (start == end) throw new InvalidDataException("Cannot recover fixture numeric lexeme.");
                    locale ??= new NumericLocale();
                    double value = ParseDouble(json.Substring(start, end - start), out _, locale);
                    if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Non-finite fixture numeric literal.");
                    SetToken(JsonToken.Float, value);
                }
                return read;
            }
            public override void Close()
            {
                try { base.Close(); }
                finally { if (ownsLocale) locale?.Dispose(); locale = null; }
            }
            private static bool IsNumber(char value) => value >= '0' && value <= '9' || value == '.' || value == '-' || value == '+' || value == 'e' || value == 'E';
            public sealed class NumericLocale : SafeHandle
            {
                public NumericLocale() : base(IntPtr.Zero, true)
                {
                    // Windows SDK ucrt/locale.h defines LC_NUMERIC as 4.
                    SetHandle(CreateLocale(4, "C"));
                    if (IsInvalid) throw new InvalidOperationException("Cannot create exact fixture numeric locale.");
                }
                public override bool IsInvalid => handle == IntPtr.Zero;
                protected override bool ReleaseHandle() { FreeLocale(handle); return true; }
            }
            [DllImport("ucrtbase.dll", EntryPoint = "_create_locale", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            private static extern IntPtr CreateLocale(int category, string locale);
            [DllImport("ucrtbase.dll", EntryPoint = "_free_locale", CallingConvention = CallingConvention.Cdecl)]
            private static extern void FreeLocale(IntPtr locale);
            [DllImport("ucrtbase.dll", EntryPoint = "_strtod_l", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            private static extern double ParseDouble(string text, out IntPtr end, NumericLocale locale);
        }

        private static long WorkingSet(Process process, out string source)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                uint size = (uint)Marshal.SizeOf<ProcessMemory>();
                var memory = new ProcessMemory { size = size };
                if (GetProcessMemoryInfo(GetCurrentProcess(), ref memory, size) && memory.workingSet.ToUInt64() > 0)
                { source = "Win32GetProcessMemoryInfo"; return checked((long)memory.workingSet.ToUInt64()); }
            }
            try
            {
                process.Refresh(); long working = process.WorkingSet64;
                if (working > 0) { source = "Process.WorkingSet64"; return working; }
            }
            catch (Exception) { }
            source = "Unavailable"; return -1;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessMemory
        {
            public uint size, pageFaults;
            public UIntPtr peakWorkingSet, workingSet, peakPagedPool, pagedPool, peakNonPagedPool, nonPagedPool, pagefile, peakPagefile, privateUsage;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")][return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr processHandle, ref ProcessMemory memory, uint size);
        private static string FileHash(string path)
        {
            using var stream = File.OpenRead(path); using var hash = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing " + name);
        private static double Number(string name, double fallback, double min, double max) =>
            double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value >= min && value <= max ? value : throw new ArgumentOutOfRangeException(name) : fallback;
        private sealed class CountingStorage : IEvidenceStorage
        {
            private readonly FileEvidenceStorage real = new();
            public long opens, flushes, durableFlushes;
            public Stream OpenAppend(string path) { opens++; return real.OpenAppend(path); }
            public void Flush(Stream stream, bool durable) { flushes++; if (durable) durableFlushes++; real.Flush(stream, durable); }
        }
    }
}
