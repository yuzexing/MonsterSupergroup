using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SteamDiagnosticValidityTests
    {
        private static JObject Sample(long raw, string result = "k_EResultOK")
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Mirror.FizzySteam.SteamTransportDiagnostics")).First(t => t != null);
            var method = type.GetMethod("DescribeSample"); var parameters = method.GetParameters();
            var status = Activator.CreateInstance(parameters[3].ParameterType);
            var field = parameters[3].ParameterType.GetField("m_usecQueueTime");
            field.SetValue(status, Activator.CreateInstance(field.FieldType, raw));
            return JObject.FromObject(method.Invoke(null, new[] { (object)7u, 2, Enum.Parse(parameters[2].ParameterType, result), status }));
        }
        [Test] public void FailedStatusReadDoesNotBecomeZeroPendingOrAbsentPeer()
        {
            var sample = Sample(0, "k_EResultFail");
            Assert.That((bool)sample["readSucceeded"], Is.False);
            Assert.That((int)sample["pendingReliableBytes"], Is.EqualTo(-1));
            Assert.That((string)sample["queueValidity"], Is.EqualTo("ReadFailed"));
            Assert.That((string)sample["steamConnection"], Is.EqualTo("7"));
        }
        [TestCase(long.MaxValue, "AboveOneHourUnverified")]
        [TestCase(-1L, "Negative")]
        public void InvalidDurationRetainsExactRawValueAndCannotTriggerLegacyQueueAlarm(long raw, string expected)
        {
            var sample = Sample(raw);
            Assert.That((string)sample["queueMicrosecondsRaw"], Is.EqualTo(raw.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That((bool)sample["queueValid"], Is.False);
            Assert.That((double)sample["queueMilliseconds"], Is.EqualTo(-1));
            Assert.That((string)sample["queueValidity"], Is.EqualTo(expected));
        }
        [Test] public void ValidCongestionDurationKeepsMillisecondsAndValidity()
        {
            var sample = Sample(1234567);
            Assert.That((bool)sample["queueValid"], Is.True);
            Assert.That((double)sample["queueMilliseconds"], Is.EqualTo(1234.567));
        }
        [TestCase("k_EResultOK", true)]
        [TestCase("k_EResultFail", false)]
        public void LegacySamplingKeepsItsOriginalValuesAndOmitsInvestigationMetadata(string result, bool expected)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Mirror.FizzySteam.SteamTransportDiagnostics")).First(t => t != null);
            var method = type.GetMethod("TryDescribeLegacySample"); var parameters = method.GetParameters();
            var status = Activator.CreateInstance(parameters[2].ParameterType);
            var field = parameters[2].ParameterType.GetField("m_usecQueueTime");
            field.SetValue(status, Activator.CreateInstance(field.FieldType, 1234567L));
            var args = new[] { (object)2, Enum.Parse(parameters[1].ParameterType, result), status, null };
            Assert.That(method.Invoke(null, args), Is.EqualTo(expected));
            var sample = JObject.Parse(UnityEngine.JsonUtility.ToJson(args[3]));
            Assert.That((double)sample["queueMilliseconds"], Is.EqualTo(expected ? 1234.567 : 0));
            foreach (string name in new[] { "steamConnection", "queueMicrosecondsRaw", "queueValidity", "readSucceeded", "queueValid" })
                Assert.That(sample[name], Is.Null, name);
        }
    }
}
