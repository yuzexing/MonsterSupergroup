using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    /// <summary>Opt-in only after the sink supports shared dependency materialization and queue leases.</summary>
    public interface IDiagnosticSharedPayloadSink
    {
        DiagnosticMemoryBudget Memory { get; }
    }

    /// <summary>
    /// One explicitly captured semantic version, shared by multiple diagnostic records.
    /// Never look up this handle by an unversioned mutable gameplay-array identity.
    /// The creator owns Dispose; each queued record owns an independent AcquireLease result.
    /// </summary>
    [JsonConverter(typeof(SharedEvidencePayloadReferenceConverter))]
    public sealed class SharedEvidencePayload : IDisposable
    {
        public const int MaximumRetainedBytes = 4 << 20;
        private readonly DiagnosticMemoryBudget memory;
        private object value;
        private string referenceSource, relativeReference;
        private int references = 1, creatorReleased;
        public long RetainedBytes { get; }
        internal object ValueForWriter => value ?? throw new ObjectDisposedException(nameof(SharedEvidencePayload));

        private SharedEvidencePayload(object value, long bytes, DiagnosticMemoryBudget memory)
        { this.value = value; this.memory = memory; RetainedBytes = bytes; }

        public static bool TryCapture(CombatSubmissionBatch batch, DiagnosticMemoryBudget memory, out SharedEvidencePayload payload)
        {
            long count = (long)(batch.Results?.Length ?? 0) + (batch.StatusMutations?.Length ?? 0) +
                (batch.PlayerHealthReports?.Length ?? 0) + (batch.EnemyDeathReports?.Length ?? 0);
            return TryCapture(512 + count * 1024, memory, () => DiagnosticPayload.Freeze(batch), out payload);
        }
        public static bool TryCapture(CanonicalWorldBatch batch, DiagnosticMemoryBudget memory, out SharedEvidencePayload payload)
        {
            long count = (long)(batch.Entities?.Length ?? 0) + (batch.Statuses?.Length ?? 0) +
                (batch.ConfirmedKills?.Length ?? 0) + (batch.EnemyHitPresentations?.Length ?? 0);
            return TryCapture(512 + count * 1024, memory, () => DiagnosticPayload.Freeze(batch), out payload);
        }
        private static bool TryCapture(long bytes, DiagnosticMemoryBudget memory, Func<object> capture, out SharedEvidencePayload payload)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            payload = null;
            if (bytes > MaximumRetainedBytes || !memory.TryReserve(bytes)) return false;
            try { payload = new SharedEvidencePayload(capture(), bytes, memory); return true; }
            catch { memory.Release(bytes); throw; }
        }
        public IDisposable AcquireLease()
        {
            while (true)
            {
                int count = Volatile.Read(ref references);
                if (count == 0) throw new ObjectDisposedException(nameof(SharedEvidencePayload));
                if (count == int.MaxValue) throw new InvalidOperationException("SharedEvidenceReferenceLimit");
                if (Interlocked.CompareExchange(ref references, count + 1, count) == count) return new Lease(this);
            }
        }
#if UNITY_EDITOR
        // Tests alone may allocate an unbudgeted inspection copy; production only receives writer leases.
        internal object CopyValueForTests()
        {
            using var lease = AcquireLease(); return DiagnosticPayload.Freeze(ValueForWriter);
        }
#endif
        // Storage-worker access only. The reference is published only after its dependency is durable.
        internal bool TryGetReference(string sourceDirectory, out string reference)
        {
            reference = string.Equals(referenceSource, sourceDirectory, StringComparison.Ordinal) ? relativeReference : null;
            return reference != null;
        }
        internal void SetReference(string sourceDirectory, string reference)
        {
            if (string.IsNullOrEmpty(sourceDirectory)) throw new ArgumentException("MissingSharedEvidenceSource", nameof(sourceDirectory));
            if (string.IsNullOrEmpty(reference)) throw new ArgumentException("MissingSharedEvidenceReference", nameof(reference));
            referenceSource = sourceDirectory; relativeReference = reference;
        }
        /// <summary>Acquire leases for the explicitly supported diagnostic graph, rolling back this call if traversal fails.</summary>
        public static void CollectLeases(object value, List<IDisposable> leases)
        {
            if (leases == null) throw new ArgumentNullException(nameof(leases));
            int start = leases.Count, remaining = 4096;
            try { CollectLeases(value, leases, 0, ref remaining); }
            catch
            {
                for (int i = leases.Count - 1; i >= start; i--) { leases[i].Dispose(); leases.RemoveAt(i); }
                throw;
            }
        }
        private static void CollectLeases(object value, List<IDisposable> leases, int depth, ref int remaining)
        {
            if (depth > 32 || --remaining < 0) throw new InvalidOperationException("SharedEvidenceGraphLimit");
            switch (value)
            {
                case SharedEvidencePayload shared: leases.Add(shared.AcquireLease()); break;
                case object[] array:
                    foreach (var item in array) CollectLeases(item, leases, depth + 1, ref remaining);
                    break;
                case ReplayOutResult output:
                    CollectLeases(output.result, leases, depth + 1, ref remaining);
                    CollectLeases(output.outValues, leases, depth + 1, ref remaining);
                    break;
                case CanonicalReceiveEvidence received:
                    CollectLeases(received.batch, leases, depth + 1, ref remaining);
                    break;
            }
        }
        public void Dispose() { if (Interlocked.Exchange(ref creatorReleased, 1) == 0) Release(); }
        private void Release()
        {
            if (Interlocked.Decrement(ref references) != 0) return;
            value = null; referenceSource = null; relativeReference = null; memory.Release(RetainedBytes);
        }
        private sealed class Lease : IDisposable
        {
            private SharedEvidencePayload owner;
            public Lease(SharedEvidencePayload owner) { this.owner = owner; }
            public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
        }
    }

    // The storage worker must persist/flush the dependency and replace the handle with $evidenceRef first.
    // A missed materialization must become an explicit capture failure, not a plausible empty object.
    public sealed class SharedEvidencePayloadReferenceConverter : JsonConverter
    {
        [ThreadStatic] private static Func<SharedEvidencePayload, string> resolver;
        internal static IDisposable BeginResolution(Func<SharedEvidencePayload, string> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var scope = new ResolutionScope(resolver); resolver = value; return scope;
        }
        public override bool CanConvert(Type type) => type == typeof(SharedEvidencePayload);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (resolver == null) throw new JsonSerializationException("SharedEvidenceReferenceNotMaterialized");
            string reference = resolver((SharedEvidencePayload)value);
            if (string.IsNullOrEmpty(reference)) throw new JsonSerializationException("SharedEvidenceReferenceNotMaterialized");
            writer.WriteStartObject(); writer.WritePropertyName("$evidenceRef"); writer.WriteValue(reference); writer.WriteEndObject();
        }
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
            throw new JsonSerializationException("SharedEvidenceHandleIsNotAFileFormat");
        private sealed class ResolutionScope : IDisposable
        {
            private readonly Func<SharedEvidencePayload, string> previous;
            private bool disposed;
            public ResolutionScope(Func<SharedEvidencePayload, string> previous) { this.previous = previous; }
            public void Dispose() { if (disposed) return; disposed = true; resolver = previous; }
        }
    }
}
