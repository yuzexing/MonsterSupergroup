using System.Collections;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class EnemyHandoffGameplayTests
    {
        [UnityTest]
        public IEnumerator BrotchiDashTravelsConfiguredDistanceWithLocalPhysicsInterpolation()
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Content/NetworkCombat/Limbo/Dash/ReferenceBrotchiDash.prefab");
            var agent = Spawn(prefab);
            yield return WaitFor(() => agent.ProductEnemyInitialized && agent.Authority.RunsCombatDecisions, "Dash ready");
            var enemy = agent.GetComponent<EnemyController>();
            var dash = agent.GetComponent<EnemyAttackDash>();
            enemy.attackDistance = 0;
            enemy.Attack();
            yield return WaitFor(() => enemy.CurrentAttackPresentationPhase == EnemyAttackPresentationPhase.Active, "Dash active");
            var action = enemy.CaptureSimulationAction(EnemySimulationClock.CombatNow);
            Assert.That(enemy.rigidBody.interpolation, Is.EqualTo(RigidbodyInterpolation2D.Interpolate));
            float maximumDistance = 0;
            float previousDistance = 0;
            float deadline = Time.realtimeSinceStartup + 3;
            while (enemy.CurrentAttackPresentationPhase == EnemyAttackPresentationPhase.Active && Time.realtimeSinceStartup < deadline)
            {
                float distance = Vector2.Distance(enemy.rigidBody.position, action.DashStart);
                Assert.That(distance, Is.GreaterThanOrEqualTo(previousDistance - .02f), "Render interpolation must not rewind physical dash progress.");
                previousDistance = distance;
                maximumDistance = Mathf.Max(maximumDistance, distance);
                yield return null;
            }
            maximumDistance = Mathf.Max(maximumDistance, Vector2.Distance(enemy.rigidBody.position, action.DashStart));
            TestContext.WriteLine($"Dash configured={dash.distance}, actual={maximumDistance:F4}");
            // The source prefab keeps damping=10; retain its physical travel rather than
            // changing the authored distance or requiring a teleport to the curve endpoint.
            Assert.That(maximumDistance, Is.GreaterThan(dash.distance * .75f), "An unobstructed dash must not lose most of its physical travel.");
#endif
            yield break;
        }
    }
}
