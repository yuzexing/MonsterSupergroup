using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonsterSupergroup.GAS;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MonsterSupergroup.NetworkCombat.Diagnostics
{
    public interface IReplayAdapter
    {
        string Domain { get; }
        void RestoreReplayState(JToken checkpoint);
        JToken Execute(string operation, JArray arguments, JToken boundary);
        JToken CaptureReplayState();
    }

    /// <summary>Only registered, instrumented business methods can execute; log content never names arbitrary CLR types.</summary>
    public sealed class CombatReplayAdapter : IReplayAdapter
    {
        public string Domain { get; }
        public object Engine { get; private set; }
        public readonly List<StatusTick> Ticks = new();
        public CombatReplayAdapter(string domain) { Domain = domain; }
        public void RestoreReplayState(JToken state)
        {
            using var suppressed = CombatEvidence.Suppress();
            if (Domain == "damage" || Domain == "weapon_stats" || Domain == "output_stats")
            {
                var calculation = new CalculationReplayAdapter(Domain); calculation.RestoreReplayState(state); Engine = calculation; return;
            }
            Engine = Domain switch {
                "gateway" => ServerCombatGateway.RestoreReplayState(EvidenceJson.Convert<GatewayReplayState>(state)),
                "ledger" => CombatLedger.RestoreReplayState(EvidenceJson.Convert<LedgerReplayState>(state)),
                "replica" => CanonicalWorldReplica.RestoreReplayState(EvidenceJson.Convert<ReplicaReplayState>(state), Ticks.Add),
                "authority" => ServerEnemySimulationRegistry.RestoreReplayState(EvidenceJson.Convert<AuthorityReplayState>(state)),
                "status" => StatusController.RestoreReplayState(EvidenceJson.Convert<StatusControllerReplayState>(state), Ticks.Add),
                "attacks" => ServerAttackRegistry.RestoreReplayState(EvidenceJson.Convert<AttackRegistryReplayState>(state)),
                "admissions" => ServerStatusDamageAdmissions.RestoreReplayState(EvidenceJson.Convert<StatusAdmissionReplayState[]>(state)),
                _ => throw new InvalidOperationException("Unsupported replay domain: " + Domain)
            };
        }
        public void SetExternalFacts(JArray facts)
        {
            if (Engine is not ServerCombatGateway gateway) return;
            var inputs = new Queue<JToken>(facts ?? new JArray());
            gateway.ValidatePickupReceipt = (sender, report) => {
                if (inputs.Count == 0) throw new InvalidOperationException("Missing external pickup receipt input.");
                var input = inputs.Dequeue();
                if (input["sender"].Value<uint>() != sender || !JToken.DeepEquals(input["report"], Token(report)))
                    throw new InvalidOperationException("External pickup receipt input does not match the requested operation.");
                return input["accepted"].Value<bool>();
            };
        }
        public JToken CaptureReplayState() => Token(Engine switch {
            ServerCombatGateway e => e.CaptureReplayState(), CombatLedger e => e.CaptureReplayState(),
            CanonicalWorldReplica e => e.CaptureReplayState(), ServerEnemySimulationRegistry e => e.CaptureReplayState(),
            StatusController e => e.CaptureReplayState(), ServerAttackRegistry e => e.CaptureReplayState(),
            ServerStatusDamageAdmissions e => e.CaptureReplayState(),
            IReplayAdapter e => e.CaptureReplayState(),
            _ => throw new InvalidOperationException("Missing replay engine.")
        });
        public JToken Execute(string operation, JArray args, JToken boundary)
        {
            using var suppressed = CombatEvidence.Suppress();
            if (Engine is IReplayAdapter calculation) return calculation.Execute(operation, args, boundary);
            object target = Engine;
            if (target is ServerCombatGateway gateway)
            {
                int dot = operation.IndexOf('.');
                if (dot >= 0)
                {
                    target = operation.Substring(0, dot) switch { "ledger" => gateway.Ledger, "statuses" => gateway.Statuses,
                        "attacks" => gateway.Attacks, "admissions" => gateway.StatusDamageAdmissions,
                        _ => throw new InvalidOperationException("Unknown gateway child.") };
                    operation = operation.Substring(dot + 1);
                }
                else if (operation == "SetRound") { gateway.Round = args[0].Value<uint>(); return JValue.CreateNull(); }
                else if (operation == "ProcessBatch")
                {
                    var batch = gateway.ProcessBatch(args[0].Value<uint>(), EvidenceJson.Convert<CombatSubmissionBatch>(args[1]), args[2].Value<double>(), out var receipts);
                    return Token(new GatewayReplayOutput { batch = batch, receipts = receipts });
                }
            }
            if (target is CanonicalWorldReplica replica)
            {
                if (operation.StartsWith("controller.", StringComparison.Ordinal))
                {
                    var parts = operation.Split('.'); if (parts.Length != 3) throw new InvalidOperationException("Invalid controller operation.");
                    target = replica.GetReplayController(uint.Parse(parts[1])); operation = parts[2];
                }
                else if (operation == "RegisterStatusController")
                { replica.RegisterStatusController(args[0].Value<uint>(), StatusController.RestoreReplayState(EvidenceJson.Convert<StatusControllerReplayState>(args[1]), Ticks.Add)); return JValue.CreateNull(); }
                else if (operation == "UnregisterStatusController")
                { uint id = args[0].Value<uint>(); return Token(replica.UnregisterStatusController(id, args[1].Value<bool>() ? replica.GetReplayController(id) : null)); }
            }
            if (target is StatusController status && boundary != null && boundary.Type != JTokenType.Null)
                status.RestoreReplayBoundary(EvidenceJson.Convert<StatusReplayBoundary>(boundary));
            if (target is StatusController transferring && operation == "TransferTo")
            {
                uint id = args[0].Value<uint>();
                var destination = (Engine as CanonicalWorldReplica)?.GetReplayController(id) ?? StatusController.RestoreReplayState(EvidenceJson.Convert<StatusControllerReplayState>(args[1]), Ticks.Add);
                int result = transferring.TransferTo(destination, id);
                return Token(new { result, targetState = destination.CaptureReplayState() });
            }
            // An EvidenceCore companion is the allowlist marker. Never invoke a name supplied by logs without it.
            var allowed = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "EvidenceCore_" + operation).ToArray();
            foreach (var marker in allowed)
            {
                var parameters = marker.GetParameters();
                if (parameters.Count(p => !p.IsOut) != args.Count || parameters.Any(p => p.ParameterType.IsByRef && !p.IsOut)) continue;
                var method = target.GetType().GetMethod(operation, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, parameters.Select(p => p.ParameterType).ToArray(), null);
                if (method == null) continue;
                var values = new object[parameters.Length];
                int argument = 0;
                for (int i = 0; i < values.Length; i++) if (!parameters[i].IsOut) values[i] = args[argument++].ToObject(parameters[i].ParameterType, JsonSerializer.Create(EvidenceJson.Settings));
                try {
                    var result = method.Invoke(target, values);
                    if (parameters.Any(p => p.IsOut)) return Token(new { result, outValues = parameters.Select((p, i) => (p, i)).Where(v => v.p.IsOut).Select(v => values[v.i]).ToArray() });
                    return Token(result);
                }
                catch (TargetInvocationException error) { throw error.InnerException ?? error; }
            }
            throw new InvalidOperationException("Unsupported replay operation: " + Domain + "." + operation);
        }
        public static JToken Token(object value) => value == null ? JValue.CreateNull() : JToken.Parse(EvidenceJson.Encode(value));
    }
    [Serializable] public sealed class ReplayFixture
    {
        public int version = 2;
        public string domain, build, source, engine;
        public bool complete;
        public string[] gaps;
        public JToken checkpoint;
        public ReplayStep[] steps;
    }
    [Serializable] public sealed class ReplayStep
    {
        public string record, operation;
        public JArray arguments, external, expectedTicks;
        public JToken boundary, expected, expectedState;
    }
    [Serializable] public sealed class ReplayReport
    {
        public bool reliable, passed;
        public int executed, firstDivergence = -1;
        public string reason, record, operation, differencePath;
        public JToken expected, actual, finalState;
    }
    public static class CombatReplay
    {
        public static ReplayReport Run(ReplayFixture fixture)
        {
            var report = new ReplayReport();
            if ((fixture.version != 1 && fixture.version != 2) || !fixture.complete || (fixture.gaps?.Length ?? 0) != 0)
            { report.reason = "IncompleteOrUnsupportedEvidence"; return report; }
            try
            {
                var adapter = new CombatReplayAdapter(fixture.domain); adapter.RestoreReplayState(fixture.checkpoint);
                report.reliable = true;
                for (int i = 0; i < fixture.steps.Length; i++)
                {
                    var step = fixture.steps[i]; report.record = step.record; report.operation = step.operation;
                    adapter.SetExternalFacts(step.external); adapter.Ticks.Clear();
                    var actual = adapter.Execute(step.operation, step.arguments, step.boundary); report.executed++;
                    JToken expected = step.expected ?? JValue.CreateNull();
                    bool outputMatches = JToken.DeepEquals(expected, actual);
                    if (step.expectedTicks != null && !JToken.DeepEquals(step.expectedTicks, CombatReplayAdapter.Token(adapter.Ticks)))
                    { expected = step.expectedTicks; actual = CombatReplayAdapter.Token(adapter.Ticks); outputMatches = false; }
                    var state = step.expectedState == null ? null : adapter.CaptureReplayState();
                    if (!outputMatches || (state != null && !JToken.DeepEquals(step.expectedState, state)))
                    {
                        report.firstDivergence = i; report.reason = outputMatches ? "StateDivergence" : "OutputDivergence";
                        report.expected = outputMatches ? step.expectedState : expected; report.actual = outputMatches ? state : actual;
                        report.differencePath = FirstDifference(report.expected, report.actual);
                        report.finalState = adapter.CaptureReplayState(); return report;
                    }
                }
                report.finalState = adapter.CaptureReplayState(); report.passed = true; report.reason = "Matched";
            }
            catch (Exception error) { report.reliable = false; report.reason = "CannotReliablyReplay: " + error.Message; }
            return report;
        }
        private static string FirstDifference(JToken expected, JToken actual, string path = "$")
        {
            if (JToken.DeepEquals(expected, actual)) return null;
            if (expected is JObject left && actual is JObject right)
            {
                foreach (string name in left.Properties().Select(p => p.Name).Concat(right.Properties().Select(p => p.Name)).Distinct())
                {
                    string found = FirstDifference(left[name], right[name], path + "/" + name.Replace("~", "~0").Replace("/", "~1"));
                    if (found != null) return found;
                }
            }
            if (expected is JArray a && actual is JArray b)
            {
                if (a.Count != b.Count) return path + "/#length";
                for (int i = 0; i < a.Count; i++)
                {
                    string found = FirstDifference(a[i], b[i], path + "/" + i);
                    if (found != null) return found;
                }
            }
            return path;
        }
        public static ReplayFixture Minimize(ReplayFixture fixture)
        {
            var original = Run(fixture);
            if (!original.reliable || original.passed) throw new InvalidOperationException("A reproducible divergence is required.");
            var steps = fixture.steps.ToList();
            // Preserve the exact failing assertion and operation while removing unrelated earlier operations.
            string record = original.record, operation = original.operation, reason = original.reason;
            for (int size = Math.Max(1, steps.Count / 2); size >= 1; size /= 2)
            {
                for (int start = 0; start + size <= steps.Count;)
                {
                    var candidate = steps.Take(start).Concat(steps.Skip(start + size)).ToArray(); fixture.steps = candidate;
                    var result = Run(fixture);
                    if (result.reliable && !result.passed && result.record == record && result.operation == operation && result.reason == reason && result.differencePath == original.differencePath)
                        steps = candidate.ToList();
                    else start += size;
                }
                if (size == 1) break;
            }
            fixture.steps = steps.ToArray(); return fixture;
        }
    }
}
