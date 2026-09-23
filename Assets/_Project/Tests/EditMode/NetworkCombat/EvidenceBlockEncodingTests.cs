using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EvidenceBlockEncodingTests
    {
        [Test] public void StorageHashesMatchIndependentSha256AcrossBoundarySizesAndThreads()
        {
            Assert.That(EvidenceJson.Hash(Array.Empty<byte>()), Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
            Assert.That(EvidenceJson.Hash(Encoding.ASCII.GetBytes("abc")), Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            Parallel.ForEach(new[] { 1, 55, 56, 63, 64, 65, 128, 6500, 65536, 1048576 }, length => {
                var bytes = new byte[length]; new Random(length).NextBytes(bytes);
                using var reference = new SHA256Managed();
                string expected = BitConverter.ToString(reference.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                for (int repeat = 0; repeat < 3; repeat++) Assert.That(EvidenceJson.Hash(bytes), Is.EqualTo(expected));
            });
        }

        [Test] public void BinaryV1GoldenBytesPreserveAllBoundaryFlagsAndExceptionalNumberBits()
        {
            var rows = GoldenEntries();
            string encoded = EvidenceBlocks.EncodeAdvances("capture\"\n中文", "run\\end", uint.MaxValue, rows, rows.Length);
            var header = JObject.Parse(encoded);
            // Captured from the preceding BinaryWriter implementation, before the workspace/JSON-envelope optimization.
            Assert.That((string)header["hash"], Is.EqualTo("771326d3ecdab30ef6d8146824e42d6ba32e45185eeac0a6b8ed5d0c6a308adf"));
            Assert.That((string)header["encoding"], Is.EqualTo("advance-binary-v1")); Assert.That((int)header["schemaVersion"], Is.EqualTo(2));
            Assert.That((string)header["captureId"], Is.EqualTo("capture\"\n中文")); Assert.That((string)header["runId"], Is.EqualTo("run\\end"));
            var decoded = EvidenceBlocks.Decode(encoded).ToArray(); Assert.That(decoded.Length, Is.EqualTo(128));
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; var record = decoded[i];
                Assert.That(record.recordSequence, Is.EqualTo(row.sequence.ToString())); Assert.That(record.round, Is.EqualTo(uint.MaxValue));
                Assert.That(record.role, Is.EqualTo(row.role)); Assert.That(record.engine, Is.EqualTo(row.engine)); Assert.That(record.operation, Is.EqualTo(row.operation));
                Assert.That(record.frame, Is.EqualTo(row.frame)); Assert.That(record.fixedStep, Is.EqualTo(row.fixedStep));
                Assert.That(BitConverter.DoubleToInt64Bits(record.monotonicTime), Is.EqualTo(BitConverter.DoubleToInt64Bits(row.monotonic)));
                Assert.That(BitConverter.DoubleToInt64Bits(record.networkTime), Is.EqualTo(BitConverter.DoubleToInt64Bits(row.network)));
                Assert.That(record.utc, Is.EqualTo(new DateTime(row.utcTicks, DateTimeKind.Utc).ToString("o")));
                if (row.phase == 0)
                {
                    Assert.That(BitConverter.SingleToInt32Bits((float)((object[])record.input)[0]), Is.EqualTo(BitConverter.SingleToInt32Bits(row.delta)));
                    Assert.That(EvidenceJson.Encode(record.before), Is.EqualTo(EvidenceJson.Encode(row.boundary)));
                }
                else { Assert.That(record.before, Is.Null); Assert.That(record.input, Is.Null); }
            }
        }

        [Test] public void WorkspaceReuseDoesNotCarryDictionariesOrTrailingBytesAcrossIndependentBlocks()
        {
            var small = new[] { new DiagnosticAdvance { sequence = 1, utcTicks = DateTime.UnixEpoch.Ticks,
                role = null, engine = "new-engine", operation = "Advance", delta = .125f } };
            string expected = EvidenceBlocks.EncodeAdvances(null, null, 0, small, 1);
            EvidenceBlocks.EncodeAdvances("large", "first", 19, GoldenEntries(), 128);
            var record = new DiagnosticRecord { recordSequence = "2", stage = "plain", input = "second" };
            string plain = EvidenceBlocks.Encode(Encoding.UTF8.GetBytes(EvidenceJson.Encode(record) + "\n"), "2", "2", 1);
            Assert.That(EvidenceBlocks.Decode(plain).Single().stage, Is.EqualTo("plain"));
            string actual = EvidenceBlocks.EncodeAdvances(null, null, 0, small, 1);
            Assert.That((string)JObject.Parse(actual)["hash"], Is.EqualTo((string)JObject.Parse(expected)["hash"]));
            var restored = EvidenceBlocks.Decode(actual).Single(); Assert.That(restored.engine, Is.EqualTo("new-engine")); Assert.That(restored.before, Is.Null);
            Assert.That(JObject.Parse(actual)["captureId"], Is.Null); Assert.That(JObject.Parse(actual)["runId"], Is.Null);
        }

        [Test] public void FailedAndOversizedEncodingLeaveTheNextBlockUsable()
        {
            var rows = GoldenEntries();
            rows[63].engine = new string('x', 257);
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.EncodeAdvances("capture", "run", 1, rows, rows.Length));
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.EncodeAdvances("capture", "run", 1, null, 1));
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.EncodeAdvances("capture", "run", 1, new DiagnosticAdvance[129], 129));
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.EncodeAdvances("capture", "run", 1, rows, 0));
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.Encode(new byte[EvidenceBlocks.MaximumDecodedBytes + 1], "1", "1", 1));
            var valid = GoldenEntries();
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.EncodeAdvances(new string('x', 4097), "run", 1, valid, 128));
            string encoded = EvidenceBlocks.EncodeAdvances("capture", "run", 1, valid, 128);
            Assert.That((string)JObject.Parse(encoded)["hash"], Is.EqualTo("771326d3ecdab30ef6d8146824e42d6ba32e45185eeac0a6b8ed5d0c6a308adf"));
            Assert.That(EvidenceBlocks.Decode(encoded).Count(), Is.EqualTo(128));
        }

        [Test] public void MaximumIdentityAndAbortedStepRemainReadableWhileInvalidOrderAndBoundaryAreRejected()
        {
            var rows = new[] {
                new DiagnosticAdvance { sequence = 1, utcTicks = DateTime.UnixEpoch.Ticks, engine = new string('界', 256), phase = 0, delta = -.5f, boundary = new StatusReplayBoundary() },
                new DiagnosticAdvance { sequence = 2, utcTicks = DateTime.UnixEpoch.Ticks + 1, engine = new string('界', 256), phase = 2 } };
            var decoded = EvidenceBlocks.Decode(EvidenceBlocks.EncodeAdvances("c", "r", 0, rows, 2)).ToArray();
            Assert.That(decoded[0].engine.Length, Is.EqualTo(256)); Assert.That(decoded[1].outcome, Is.EqualTo("Aborted"));
            rows[1].sequence = 1;
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.Decode(EvidenceBlocks.EncodeAdvances("c", "r", 0, rows, 2)).ToArray());
            rows[1].sequence = 2; rows[1].boundary = new StatusReplayBoundary();
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.Decode(EvidenceBlocks.EncodeAdvances("c", "r", 0, rows, 2)).ToArray());
            rows[1].boundary = null; rows[1].phase = 3;
            Assert.Throws<InvalidDataException>(() => EvidenceBlocks.Decode(EvidenceBlocks.EncodeAdvances("c", "r", 0, rows, 2)).ToArray());
        }

        [Test] public void ParallelWritersKeepIndependentWorkspaces()
        {
            var tasks = Enumerable.Range(0, 2).Select(worker => Task.Run(() =>
            {
                var rows = GoldenEntries();
                for (int i = 0; i < rows.Length; i++) rows[i].engine = "worker-" + worker;
                for (int block = 0; block < 32; block++)
                {
                    var decoded = EvidenceBlocks.Decode(EvidenceBlocks.EncodeAdvances("capture-" + worker, "run", (uint)worker, rows, 128)).ToArray();
                    Assert.That(decoded.All(record => record.engine == "worker-" + worker && record.captureId == "capture-" + worker), Is.True);
                }
            })).ToArray();
            Assert.That(Task.WaitAll(tasks, 5000), Is.True);
        }

        [TestCase("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
        [TestCase("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
        public void HashRetainsStandardLowercaseSha256(string value, string expected) => Assert.That(EvidenceJson.Hash(Encoding.UTF8.GetBytes(value)), Is.EqualTo(expected));

        private static DiagnosticAdvance[] GoldenEntries()
        {
            var rows = new DiagnosticAdvance[128];
            int[] floatBits = { unchecked((int)0x80000000), 1, 0x7f800000, unchecked((int)0xff800000), 0x7fc01234, 0x3c888889 };
            long[] doubleBits = { unchecked((long)0x8000000000000000UL), 1, 0x7ff0000000000000L, unchecked((long)0xfff0000000000000UL), 0x7ff8000000001234L, 0x3f91111111111111L };
            for (int i = 0; i < rows.Length; i++)
            {
                int flags = i / 2;
                rows[i] = new DiagnosticAdvance { sequence = ulong.MaxValue - 127 + (ulong)i, utcTicks = 638940000000000000L + i,
                    role = flags % 3 == 0 ? null : "角色", engine = flags % 3 == 1 ? "" : "engine-" + flags, operation = "Advance\"\\\n",
                    phase = i % 2, frame = -i, fixedStep = int.MaxValue - i, delta = BitConverter.Int32BitsToSingle(floatBits[flags % floatBits.Length]),
                    monotonic = BitConverter.Int64BitsToDouble(doubleBits[i % doubleBits.Length]), network = BitConverter.Int64BitsToDouble(doubleBits[(i + 1) % doubleBits.Length]),
                    boundary = i % 2 == 1 ? null : new StatusReplayBoundary { eventIds = (flags & 1) != 0, supported = (flags & 2) != 0, executeAll = (flags & 4) != 0,
                        offline = (flags & 8) != 0, server = (flags & 16) != 0, localPlayer = (uint)flags, targetOwner = uint.MaxValue - (uint)flags,
                        ids = (flags & 32) == 0 ? null : new EventSequenceState { slot = ushort.MaxValue, epoch = (ushort)flags, next = uint.MaxValue - (uint)flags } } };
            }
            return rows;
        }
    }
}
