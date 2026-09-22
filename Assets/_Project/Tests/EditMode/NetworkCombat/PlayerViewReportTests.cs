using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PlayerViewReportTests
    {
        private static PlayerViewReport Report => new PlayerViewReport {
            Round = 4, Sequence = 1, SampledAt = 10, Center = new Vector2(2, 3), Size = new Vector2(30, 20)
        };

        [Test]
        public void OnlyFreshCurrentRoundGeometryCanAuthorizeScreenTransfer()
        {
            var report = Report;
            Assert.That(report.IsValid(4, 10.5), Is.True);
            Assert.That(report.IsValid(4, 10.5001), Is.False);
            Assert.That(report.IsValid(5, 10.1), Is.False);
            Assert.That(report.IsValid(4, 9.74), Is.False);
            report.Sequence = 0;
            Assert.That(report.IsValid(4, 10), Is.False);
        }

        [TestCase(float.NaN, 20)]
        [TestCase(float.PositiveInfinity, 20)]
        [TestCase(0, 20)]
        [TestCase(-1, 20)]
        [TestCase(1001, 20)]
        [TestCase(30, float.NaN)]
        public void MalformedFootprintsCannotReplaceAValidReport(float width, float height)
        {
            var report = Report; report.Size = new Vector2(width, height);
            Assert.That(report.IsValid(4, 10), Is.False);
        }

        [Test]
        public void ScreenIntersectionUsesBodyEdgesAndIgnoresPresentationDepth()
        {
            var view = new Bounds(Vector3.zero, new Vector3(20, 10, 0));
            Assert.That(PlayerViewReport.Intersects(view, new Bounds(new Vector3(10.5f, 0, 2), Vector3.one)), Is.True);
            Assert.That(PlayerViewReport.Intersects(view, new Bounds(new Vector3(10.51f, 0, 0), Vector3.one)), Is.False);
        }
    }
}
