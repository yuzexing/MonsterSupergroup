using System;
using System.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.GAS.Tests
{
    public sealed class AssemblyFenceTests
    {
        private static readonly string[] ForbiddenAssemblyFragments =
        {
            "UnityEngine",
            "UnityEditor",
            "Unity.InputSystem",
            "UnityEngine.UI",
            "FMOD",
            "Rewired",
            "Mirror"
        };

        [Test]
        public void CoreAssembly_HasNoUnityUiInputAudioOrRewiredReferences()
        {
            string[] references = typeof(EquipmentModifierID).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            foreach (string forbidden in ForbiddenAssemblyFragments)
            {
                Assert.That(
                    references.Any(reference => reference.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    $"Core assembly unexpectedly references {forbidden}: {string.Join(", ", references)}");
            }
        }

        [Test]
        public void CoreAssembly_ContainsNoLegacyGlobalServiceTypes()
        {
            string[] forbiddenTypeNames =
            {
                "GameDirector",
                "PlayerHand",
                "BaseEnemyController",
                "SceneManager"
            };

            string[] typeNames = typeof(EquipmentModifierID).Assembly
                .GetTypes()
                .Select(type => type.Name)
                .ToArray();

            foreach (string forbidden in forbiddenTypeNames)
            {
                Assert.That(typeNames, Does.Not.Contain(forbidden));
            }
        }

        [Test]
        public void GameplayCombatRuntime_DoesNotReferenceLegacyPluginsOrMirror()
        {
            string[] references = typeof(MonsterSupergroup.Gameplay.Combat.CombatantBehaviour).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            string[] forbiddenReferences =
            {
                "FMOD",
                "Rewired",
                "Mirror"
            };

            foreach (string forbidden in forbiddenReferences)
            {
                Assert.That(
                    references.Any(reference => reference.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    $"Combat runtime unexpectedly references {forbidden}: {string.Join(", ", references)}");
            }

            Assert.That(references, Does.Not.Contain("MonsterSupergroup.Gameplay.Combat"));
        }
    }
}
