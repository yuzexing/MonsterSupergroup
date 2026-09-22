using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using LegacyDamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class CombatContactEvidenceTests
    {
        private Scene scene;
        private Sink sink;

        [SetUp] public void SetUp()
        {
            scene = SceneManager.CreateScene("combat-contact-evidence-" + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            sink = new Sink(); CombatEvidence.Sink = sink;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            CombatEvidence.Sink = null;
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest] public IEnumerator PhysicalReentryIsRecordedAsDuplicateAndOnlyInvokesDamageOnce()
        {
            int hits = 0;
            var box = CreateBox(_ => hits++);
            var target = CreateTarget();
            var physics = scene.GetPhysicsScene2D();
            Physics2D.SyncTransforms(); physics.Simulate(.02f);
            Assert.That(hits, Is.EqualTo(1));
            target.position = Vector2.right * 10; physics.Simulate(.02f);
            target.position = Vector2.zero; physics.Simulate(.02f);
            Assert.That(hits, Is.EqualTo(1));
            var contacts = sink.records.Where(r => r.stage == "owner.contact").ToArray();
            Assert.That(contacts.Any(r => r.reason == "FirstContact" && r.rootEventId == "1"), Is.True);
            Assert.That(contacts.Any(r => r.reason == "DuplicateContact" && r.rootEventId == "1"), Is.True);
            Assert.That(box.collider.enabled, Is.True);
            yield return null;
        }

        [UnityTest] public IEnumerator ClosedColliderLeavesWindowEvidenceWithoutInventingAContact()
        {
            int hits = 0; var box = CreateBox(_ => hits++); box.Toggle(false);
            CreateTarget(); Physics2D.SyncTransforms(); scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(hits, Is.Zero);
            Assert.That(sink.records.Any(r => r.stage == "owner.attack_window" && r.reason == "AttackWindowBound"), Is.True);
            Assert.That(sink.records.Any(r => r.outcome == "Closed" && r.reason == "HitboxToggle"), Is.True);
            Assert.That(sink.records.Any(r => r.reason == "FirstContact" || r.reason == "DuplicateContact"), Is.False);
            yield return null;
        }

        private PlayerAttackHitBox CreateBox(Action<IDamageable> hit)
        {
            var actor = new GameObject("diagnostic attack"); SceneManager.MoveGameObjectToScene(actor, scene);
            actor.AddComponent<BoxCollider2D>();
            var box = actor.AddComponent<PlayerAttackHitBox>(); box.Init(hit);
            box.BindDiagnosticAttack(CombatContext.CreateRoot(new CombatEventId(1), 1, 1, 5, CombatTags.Attack));
            return box;
        }
        private Rigidbody2D CreateTarget()
        {
            var actor = new GameObject("diagnostic target"); SceneManager.MoveGameObjectToScene(actor, scene);
            actor.AddComponent<BoxCollider2D>(); actor.AddComponent<CombatContactEvidenceTarget>();
            var body = actor.AddComponent<Rigidbody2D>(); body.gravityScale = 0; return body;
        }
        private sealed class Sink : IDiagnosticSink
        {
            public readonly List<DiagnosticRecord> records = new();
            public bool TryWrite(DiagnosticRecord record) { records.Add(record); return true; }
            public string RegisterEngine(object engine, string domain, Func<object, object> capture) => domain;
        }
    }
    public sealed class CombatContactEvidenceTarget : MonoBehaviour, IDamageable
    {
        public int GetID() => GetInstanceID();
        public Vector2 GetPosition() => transform.position;
        public bool IsActive() => isActiveAndEnabled;
        public void Damage(Vector2 position, WeaponBehaviour weapon, LegacyDamageType type) { }
        public void Damage(int value, LegacyDamageType type) { }
    }
}
