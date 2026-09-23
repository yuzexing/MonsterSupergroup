using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class CombatDamageEvidenceTests
    {
        [TearDown] public void ResetSink() => CombatEvidence.Sink = null;

        [TestCase(0.249999f, true, 15)]
        [TestCase(0.25f, false, 10)]
        public void DamageReplayRecomputesCriticalBoundaryAndRounding(float roll, bool critical, int damage)
        {
            var stats = new WeaponBehaviourStats(new AttackStats { damage = 10, critRate = .25f, critMultiplier = 1.5f }).CreateSnapshot();
            var input = new DamageCalculationInput { stats = stats, targetMultipliers = new AttackStatsMultipliers(), criticalRoll = roll, modifiers = Array.Empty<ModifierEvidence>() };
            var adapter = new CombatReplayAdapter("damage");
            adapter.RestoreReplayState(JObject.Parse("{\"version\":1}"));
            var actual = adapter.Execute("Calculate", new JArray(CombatReplayAdapter.Token(input)), null);
            Assert.That(actual["requestedDamage"].Value<int>(), Is.EqualTo(damage));
            Assert.That(actual["isCritical"].Value<bool>(), Is.EqualTo(critical));
        }

        [Test] public void UnsupportedTargetModifierCannotBeReplayedUsingItsLoggedResult()
        {
            var input = new DamageCalculationInput {
                stats = new WeaponBehaviourStats(new AttackStats { damage = 10 }).CreateSnapshot(),
                targetMultipliers = new AttackStatsMultipliers { damage = 99 }, criticalRoll = -1,
                modifiers = new[] { new ModifierEvidence { id = 999, type = "UnregisteredConditionalModifier", parameters = new[] { 99f } } }
            };
            var adapter = new CombatReplayAdapter("damage"); adapter.RestoreReplayState(JObject.Parse("{\"version\":1}"));
            Assert.Throws<InvalidOperationException>(() => adapter.Execute("Calculate", new JArray(CombatReplayAdapter.Token(input)), null));
        }

        [Test] public void StatReplayExecutesActualRegisteredModifiersAndGlobalLayer()
        {
            var input = new AttackStatsEvidenceInput {
                baseStats = new AttackStats { damage = 10, critRate = .1f, critMultiplier = 1.5f },
                globalMultipliers = new AttackStatsMultipliers { damage = .2f },
                staticModifiers = new[] { ModifierEvidence.Capture(new DamageStatModifier(new DamageStatModifierParameters(.5f))) },
                dynamicModifiers = Array.Empty<ModifierEvidence>(), remaps = Array.Empty<StatRemapEvidence>()
            };
            var adapter = new CombatReplayAdapter("weapon_stats"); adapter.RestoreReplayState(JObject.Parse("{\"version\":1}"));
            var result = adapter.Execute("Rebuild", new JArray(CombatReplayAdapter.Token(input)), null);
            Assert.That(result["Damage"].Value<int>(), Is.EqualTo(18));
        }

        [Test] public void EachTargetCalculationIsLinkedToItsOwnDamageEvent()
        {
            var sink = new Sink(); CombatEvidence.Sink = sink;
            var pipeline = new CombatPipeline(new RuntimeEquipmentModifiers(), new FixedRandom());
            using var attack = pipeline.BeginAttack(new Weapon());
            var first = pipeline.ResolveHitDetailed(attack, new Target(101));
            var second = pipeline.ResolveHitDetailed(attack, new Target(102));
            var calculations = sink.records.Where(r => r.stage == "owner.damage_calculation").ToArray();
            Assert.That(calculations.Length, Is.EqualTo(2));
            Assert.That(calculations[0].eventId, Is.EqualTo(first.DamageContext.EventId.Value.ToString()));
            Assert.That(calculations[1].eventId, Is.EqualTo(second.DamageContext.EventId.Value.ToString()));
            Assert.That(calculations[0].eventId, Is.Not.EqualTo(calculations[1].eventId));
            Assert.That(calculations[0].rootEventId, Is.EqualTo(attack.Context.RootEventId.Value.ToString()));
        }

        [Test] public void OutputFixtureDetectsARepeatedStatisticWithoutConflatingDamageMetrics()
        {
            var initial = new OutputStatisticsState { totalDamage = 10, criticalDamage = 0 };
            var input = new OutputStatisticInput { weaponId = 1, value = 7, critical = true, metric = "ComputedDamage" };
            var expected = OutputStatistics.ApplyDamage(initial, input);
            var fixture = new ReplayFixture { complete = true, domain = "output_stats", checkpoint = CombatReplayAdapter.Token(initial), steps = new[] {
                new ReplayStep { record = "one", operation = "ApplyDamage", arguments = new JArray(CombatReplayAdapter.Token(input)), expected = CombatReplayAdapter.Token(expected) },
                new ReplayStep { record = "duplicate", operation = "ApplyDamage", arguments = new JArray(CombatReplayAdapter.Token(input)), expected = CombatReplayAdapter.Token(expected) }
            } };
            var report = CombatReplay.Run(fixture);
            Assert.That(report.reliable, Is.True); Assert.That(report.passed, Is.False);
            Assert.That(report.record, Is.EqualTo("duplicate")); Assert.That(report.firstDivergence, Is.EqualTo(1));
            SaveFixture("output-statistics-duplicate", fixture);
        }

        [Test] public void MultiplePlayersDotRestoresWithoutRepeatingExecutedTicksAndKeepsCausalIdentity()
        {
            var sink = new Sink(); CombatEvidence.Sink = sink;
            var ticks = new List<StatusTick>(); var controller = new StatusController(ticks.Add);
            var definition = new StatusDefinition(EnemyStatusID.Poison, StatusStackMode.Add, 4);
            for (uint player = 1; player <= 2; player++)
            {
                var context = CombatContext.CreateRoot(CombatEventId.Compose((ushort)player, 1, 1), player, player, 20, CombatTags.Attack);
                controller.Apply(new StatusApplication(definition, (int)player * 3, 3, .25f, 1, sourcePlayerId: player,
                    sourceEntityId: player, targetEntityId: 100, sourceContext: context));
            }
            controller.Advance(.5f);
            Assert.That(ticks.Count, Is.EqualTo(4));
            var checkpoint = CombatReplayAdapter.Token(controller.CaptureReplayState());
            var resumedTicks = new List<StatusTick>();
            var restored = StatusController.RestoreReplayState(EvidenceJson.Convert<StatusControllerReplayState>(checkpoint), resumedTicks.Add);
            restored.Advance(.25f);
            Assert.That(resumedTicks.Select(t => t.TickIndex), Is.All.EqualTo(3));
            Assert.That(resumedTicks.Select(t => t.Instance.SourcePlayerId).OrderBy(p => p), Is.EqualTo(new uint[] { 1, 2 }));
            var records = sink.records.Where(r => r.stage == "status.tick" && r.outcome == "Executed").ToArray();
            Assert.That(records, Has.Length.EqualTo(6));
            Assert.That(records.All(r => r.target == 100 && r.statusInstanceId != null && r.rootEventId != null && r.applicationRevision != 0), Is.True);
            var fixture = new ReplayFixture { complete = true, domain = "status", checkpoint = checkpoint,
                steps = new[] { new ReplayStep { record = "resume-third-tick", operation = "Advance", arguments = new JArray(.25f),
                    expected = JValue.CreateNull(), expectedTicks = (JArray)CombatReplayAdapter.Token(resumedTicks),
                    expectedState = CombatReplayAdapter.Token(restored.CaptureReplayState()) } } };
            var report = CombatReplay.Run(fixture);
            Assert.That(report.passed, Is.True, EvidenceJson.Encode(report));
            SaveFixture("multiplayer-dot-resume", fixture);
        }

        [Test] public void DiagnosticsDoNotChangeDamageOrConsumeExtraRandomSamples()
        {
            var off = RunCombat(false);
            var on = RunCombat(true);
            Assert.That(EvidenceJson.Encode(on), Is.EqualTo(EvidenceJson.Encode(off)));
        }

        [Test] public void WrongDamageAssertionIsMinimizedToTheExactEventAndFormulaStep()
        {
            var input = new DamageCalculationInput { stats = new WeaponBehaviourStats(new AttackStats { damage = 7 }).CreateSnapshot(),
                targetMultipliers = new AttackStatsMultipliers(), criticalRoll = -1, modifiers = Array.Empty<ModifierEvidence>() };
            var correct = CombatReplayAdapter.Token(DamageCalculation.Replay(input));
            var wrong = correct.DeepClone(); wrong["requestedDamage"] = 99;
            var fixture = new ReplayFixture { complete = true, domain = "damage", checkpoint = JObject.Parse("{\"version\":1}"), steps = new[] {
                new ReplayStep { record = "earlier", operation = "Calculate", arguments = new JArray(CombatReplayAdapter.Token(input)), expected = correct },
                new ReplayStep { record = "damage-2", operation = "Calculate", arguments = new JArray(CombatReplayAdapter.Token(input)), expected = wrong }
            } };
            var report = CombatReplay.Run(fixture);
            Assert.That(report.differencePath, Is.EqualTo("$/requestedDamage"));
            var minimized = CombatReplay.Minimize(fixture);
            Assert.That(minimized.steps, Has.Length.EqualTo(1)); Assert.That(minimized.steps[0].record, Is.EqualTo("damage-2"));
            SaveFixture("damage-formula-divergence", minimized);
            minimized.steps[0].expected = correct;
            Assert.That(CombatReplay.Run(minimized).passed, Is.True);
            SaveFixture("damage-formula-matched", minimized);
        }

        [Test] public void PlayerTotalStatisticRetainsDamageEventAndActualIncrement()
        {
            var sink = new Sink(); CombatEvidence.Sink = sink;
            var context = CombatContext.CreateRoot(CombatEventId.Compose(2, 4, 8), 2, 2, 30, CombatTags.Damage);
            var stats = new AstralShift.HellMaiden.GameStats.PlayerStatsEntry();
            using (CombatOutputEvidence.Enter(context)) stats.RegisterDamageDealt(12);
            var record = sink.records.Single(r => r.stage == "stats.damage");
            Assert.That(record.eventId, Is.EqualTo(context.EventId.Value.ToString()));
            Assert.That(((OutputStatisticInput)record.input).metric, Is.EqualTo("PlayerComputedDamage"));
            Assert.That(((OutputStatisticsState)record.before).totalDamage, Is.Zero);
            Assert.That(((OutputStatisticsState)record.after).totalDamage, Is.EqualTo(12));
        }

        [Test] public void AFailingOptionalHealthCaptureCannotPreventDamage()
        {
            var sink = new Sink(); CombatEvidence.Sink = sink;
            var pipeline = new CombatPipeline(new RuntimeEquipmentModifiers(), new FixedRandom());
            using var attack = pipeline.BeginAttack(new Weapon());
            var result = pipeline.ResolveHitDetailed(attack, new FailingHealthTarget());
            Assert.That(result.PredictedAppliedDamage.Value, Is.EqualTo(10));
            Assert.That(sink.records.Any(r => r.stage == "evidence.gap" && r.reason == "owner.health:InvalidOperationException"
                && r.eventId == result.DamageContext.EventId.Value.ToString()), Is.True);
        }

        private static object RunCombat(bool enabled)
        {
            CombatEvidence.Sink = enabled ? new Sink() : null;
            var modifiers = new RuntimeEquipmentModifiers();
            modifiers.Add(new DamageStatModifier(new DamageStatModifierParameters(.13f)));
            modifiers.Add(new OnHitBurnModifier(new OnHitBurnModifierParameters(.5f, .3f, 3, .25f)));
            var random = new FixedRandom(); var pipeline = new CombatPipeline(modifiers, random);
            using var attack = pipeline.BeginAttack(new Weapon());
            var one = pipeline.ResolveHitDetailed(attack, new Target(101));
            var two = pipeline.ResolveHitDetailed(attack, new Target(102));
            return new { one, two, random.calls };
        }

        private static void SaveFixture(string name, ReplayFixture fixture)
        {
            string directory = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../Logs/CombatEvidenceBusinessFixtures"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, name + ".json");
            File.WriteAllText(file, EvidenceJson.Encode(fixture));
            TestContext.Progress.WriteLine("Combat evidence fixture: " + file);
        }

        private sealed class Sink : IDiagnosticSink
        {
            public readonly List<DiagnosticRecord> records = new();
            public bool TryWrite(DiagnosticRecord record) { records.Add(record); return true; }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture) => domain;
        }
        private sealed class FixedRandom : IRandomSource { public int calls; public float Next01() { calls++; return .5f; } }
        private sealed class Weapon : IWeaponRuntime, ICombatContextSource
        {
            public uint CombatId => 8; public uint SourcePlayerId => 1; public uint SourceEntityId => 1;
            public WeaponBehaviourStats Stats { get; } = new(new AttackStats { damage = 10 });
        }
        private sealed class Target : ICombatTarget, ICombatStateIdentity
        {
            public Target(uint id) { EntityId = id; }
            public uint EntityId { get; } public uint StateVersion => 1; public bool IsAlive => true;
            public DamageInfo ReceiveDamage(DamageInfo damage) => damage;
            public StatusApplicationResult ApplyStatus(StatusApplication application) => default;
        }
        private sealed class FailingHealthTarget : ICombatTarget, ICombatHealthEvidence
        {
            public bool IsAlive => true;
            public int DiagnosticHealth => throw new InvalidOperationException("capture failed");
            public bool DiagnosticInvulnerable => throw new InvalidOperationException("capture failed");
            public DamageInfo ReceiveDamage(DamageInfo damage) => damage;
            public StatusApplicationResult ApplyStatus(StatusApplication application) => default;
        }
    }
}
