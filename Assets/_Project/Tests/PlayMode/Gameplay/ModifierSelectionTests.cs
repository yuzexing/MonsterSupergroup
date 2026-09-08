using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class ModifierSelectionTests
    {
        private const string Content = "Assets/_Project/Content/HellMaiden/NativeGAS/";
        private GameObject poolObject, playerObject, databaseObject, selectionObject;
        private PlayerBuildRuntime build;
        private RuntimeDB database;
        private EquipmentDB equipmentDB;
        private ModifierSelectionController selection;
        private ulong submitted;
        private int requests;

        [SetUp]
        public void SetUp()
        {
            poolObject = new GameObject("Selection Test Pool");
            poolObject.AddComponent<PoolManager>().Init();
            playerObject = new GameObject("Selection Test Player");
            playerObject.SetActive(false);
            PlayerMovement player = playerObject.AddComponent<PlayerMovement>();
            var attacks = new GameObject("Attacks");
            attacks.transform.SetParent(playerObject.transform, false);
            player.AttacksParent = attacks.transform;
            build = playerObject.AddComponent<PlayerBuildRuntime>();
            build.Initialize(player, new FixedRandom());
            equipmentDB = Asset<EquipmentDB>(Content + "NativeGasEquipmentDB.asset");
            databaseObject = new GameObject("Selection Test Database");
            database = databaseObject.AddComponent<RuntimeDB>();
            database.ConfigureWeaponDatabase(Asset<WeaponDB>(Content + "NativeGasWeaponDB.asset"));
            typeof(RuntimeDB).GetField("_equipmentDB", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(database, equipmentDB);
            build.StartInitialBuild(database);
            selectionObject = new GameObject("Selection Test Controller");
            selection = selectionObject.AddComponent<ModifierSelectionController>();
            submitted = 0;
            requests = 0;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(selectionObject);
            UnityEngine.Object.DestroyImmediate(playerObject);
            UnityEngine.Object.DestroyImmediate(databaseObject);
            UnityEngine.Object.DestroyImmediate(poolObject);
            PoolManager.Instance = null;
        }

        private void Present(ulong firstId = 100)
        {
            selection.Bind(build);
            var offers = equipmentDB.Equipments.Take(3).Select((card, i) =>
                new ModifierOffer(firstId + (ulong)i, card, 0)).ToArray();
            selection.ReceiveOffers(offers, id => { submitted = id; requests++; return true; });
        }

        [Test]
        public void BindingAloneNeverGeneratesOffersOrMutatesBuild()
        {
            selection.Bind(build);
            Assert.That(selection.Offers, Is.Empty);
            Assert.That(build.EquipmentCount, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SelectIndex_SubmitsIntentWithoutApplyingAndWaitsForAcknowledgement(int index)
        {
            Present();
            Assert.That(selection.Select(index).Succeeded, Is.True);
            Assert.That(submitted, Is.EqualTo(100ul + (ulong)index));
            Assert.That(selection.IsRequestPending, Is.True);
            Assert.That(selection.Offers.Count, Is.EqualTo(3));
            Assert.That(build.EquipmentCount, Is.Zero);
            Assert.That(selection.Select(index).Succeeded, Is.False);
            Assert.That(requests, Is.EqualTo(1));
            selection.ClearOffers();
            Assert.That(selection.IsRequestPending, Is.False);
            Assert.That(selection.Offers, Is.Empty);
        }

        [Test]
        public void StaleInvalidAndForeignOptionIdsNeverSubmit()
        {
            Present();
            Assert.That(selection.Select(-1).Succeeded, Is.False);
            Assert.That(selection.Select(3).Succeeded, Is.False);
            Assert.That(selection.SelectOffer(999999).Succeeded, Is.False);
            Present(200);
            Assert.That(selection.SelectOffer(100).Succeeded, Is.False);
            Assert.That(requests, Is.Zero);
            Assert.That(selection.SelectOffer(201).Succeeded, Is.True);
            Assert.That(submitted, Is.EqualTo(201));
        }

        [Test]
        public void RejectionRetainsOfferAndAllowsRetry()
        {
            Present();
            selection.Select(0);
            selection.CompleteRequest("Rejected by server");
            Assert.That(selection.Offers.Count, Is.EqualTo(3));
            Assert.That(selection.IsRequestPending, Is.False);
            Assert.That(selection.LastError, Is.EqualTo("Rejected by server"));
            Assert.That(selection.Select(1).Succeeded, Is.True);
            Assert.That(requests, Is.EqualTo(2));
        }

        [Test]
        public void PresentationRejectsEmptyOrOversizedOffers()
        {
            selection.Bind(build);
            Assert.Throws<ArgumentException>(() => selection.ReceiveOffers(Array.Empty<ModifierOffer>(), _ => true));
            var oversized = equipmentDB.Equipments.Take(4).Select((card, i) =>
                new ModifierOffer((ulong)i + 1, card, 0)).ToArray();
            Assert.Throws<ArgumentException>(() => selection.ReceiveOffers(oversized, _ => true));
        }

        [UnityTest]
        public IEnumerator RebuildUnbindDisableAndDestroyExpireOffers()
        {
            Present();
            build.StartInitialBuild(database);
            Assert.That(selection.SelectOffer(100).Succeeded, Is.False);
            yield return null;
            Assert.That(selection.Offers, Is.Empty);
            Present(200);
            selection.Unbind();
            Assert.That(selection.Offers, Is.Empty);
            Assert.That(selection.BoundBuild, Is.Null);
            Present(300);
            selection.enabled = false;
            Assert.That(selection.Offers, Is.Empty);
            selection.enabled = true;
            Present(400);
            UnityEngine.Object.DestroyImmediate(playerObject);
            yield return null;
            Assert.That(selection.Select(0).Succeeded, Is.False);
            Assert.That(requests, Is.Zero);
        }

        private static T Asset<T>(string path) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            T asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
#else
            throw new NotSupportedException("Asset integration tests require the Editor.");
#endif
        }

        private sealed class FixedRandom : IRandomSource
        {
            public float Next01() => 0.99f;
        }
    }
}
