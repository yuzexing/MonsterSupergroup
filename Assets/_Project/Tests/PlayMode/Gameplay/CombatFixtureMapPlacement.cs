using System.Collections;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    internal static class CombatFixtureMapPlacement
    {
        public static IEnumerator PlacePlayer(PlayerMovement player, float clearance)
        {
            GameplayMapContext map = GameplayMapContext.For(player.gameObject);
            Assert.That(map != null && map.IsReady, Is.True, "The real map must be initialized.");
            Vector2 position = map.FindSpawn(map.Bounds.center, clearance);
            Assert.That(map.IsFree(position, clearance), Is.True, "The whole test motion footprint must be clear.");
            player.SetDirection(Vector2.zero);
            map.Place(player.body, player.GetComponent<CircleCollider2D>(), position);
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(Vector2.Distance(player.body.position, position), Is.LessThan(.05f),
                "Generate requests only after physical placement is stable.");
        }
    }
}
