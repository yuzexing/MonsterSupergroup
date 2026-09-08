using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerBuildRestorationTests
    {
        private const string Content = "Assets/_Project/Content/HellMaiden/NativeGAS/";
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private RuntimeDB database;
        private EquipmentData damage;
        private PerkData speedPerk;
        private PlayerBuildRuntime source;
        private PlayerBuildRuntime restored;

        [SetUp]
        public void SetUp()
        {
            CreateObject("Restore Test Pool").AddComponent<PoolManager>().Init();
            database = CreateObject("Restore Test Definitions").AddComponent<RuntimeDB>();
            database.ConfigureWeaponDatabase(Asset<WeaponDB>(Content + "NativeGasWeaponDB.asset"));
            SetField(database, "_equipmentDB", Asset<EquipmentDB>(Content + "NativeGasEquipmentDB.asset"));
            damage = Asset<EquipmentData>("Assets/MonoBehaviour/StatRaise_DamageRaiseEquipment.asset");
            speedPerk = Asset<PerkData>("Assets/MonoBehaviour/AttackSpeedPerk.asset");
            var perks = ScriptableObject.CreateInstance<PerkDB>();
            objects.Add(perks);
            perks.Perks = new[] { speedPerk };
            SetField(database, "_perkDB", perks);
            source = CreateBuild("Original Participant");
            restored = CreateBuild("Restored Participant");
            source.StartInitialBuild(database);
            restored.StartInitialBuild(database);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
            PoolManager.Instance = null;
        }

        [Test]
        public void DetachedSnapshotRestoresMultipleSlotsEquipmentAndPerkRarityThroughNativeGas()
        {
            source.EquipWeaponAtSlot(2, source.InitialWeapon.WeaponData);
            source.AddEquipment(0, damage, 1);
            source.AddEquipment(2, damage, 0);
            source.AddPerk(speedPerk, PerkRarity.Silver);
            int firstDamage = source.InitialWeapon.DamageValue;
            float speed = source.InitialWeapon.SpeedValue;
            PlayerBuildSnapshot snapshot = source.CaptureState();
            source.ClearBuild();

            restored.RestoreState(database, snapshot);

            Assert.That(restored.WeaponCount, Is.EqualTo(2));
            Assert.That(restored.GetWeaponAtSlot(1), Is.Null);
            Assert.That(restored.GetWeaponAtSlot(2).DamageValue, Is.EqualTo(20));
            Assert.That(restored.InitialWeapon.DamageValue, Is.EqualTo(firstDamage));
            Assert.That(restored.InitialWeapon.SpeedValue, Is.EqualTo(speed));
            Assert.That(restored.EquipmentCount, Is.EqualTo(2));
            Assert.That(restored.PerkCount, Is.EqualTo(1));
            Assert.That(restored.CaptureState().Perks[0].Rarity, Is.EqualTo(PerkRarity.Silver));
            using (AttackSnapshot attack = restored.InitialWeapon.NativeRuntime.BeginAttack(
                restored.InitialWeapon.WeaponData.AttackTags))
                Assert.That(attack.Stats.Damage, Is.EqualTo(firstDamage));
            Assert.That(source.WeaponCount, Is.Zero);

            // A later mutation of the transport/save DTO cannot mutate live equipment.
            snapshot.Equipment[0].LevelIndex = 0;
            Assert.That(restored.GetEquipmentStates()[0].LevelIndex, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedOwnerBaselinePreservesWeaponsAndAppliesDuplicateCardsExactlyOncePerOccurrence()
        {
            source.AddEquipment(0, damage, 0);
            source.AddEquipment(0, damage, 0);
            source.AddPerk(speedPerk, PerkRarity.Bronze);
            source.AddPerk(speedPerk, PerkRarity.Bronze);
            PlayerBuildSnapshot snapshot = source.CaptureState();
            var initial = restored.InitialWeapon;
            using (AttackSnapshot inFlight = initial.NativeRuntime.BeginAttack(initial.WeaponData.AttackTags))
            {
                restored.ReconcileState(database, snapshot);
                var handles = restored.GetEquipmentStates().Select(item => item.Handle).ToArray();
                restored.ReconcileState(database, snapshot);
                Assert.That(restored.InitialWeapon, Is.SameAs(initial));
                Assert.That(restored.EquipmentCount, Is.EqualTo(2));
                Assert.That(restored.PerkCount, Is.EqualTo(2));
                Assert.That(restored.GetEquipmentStates().Select(item => item.Handle), Is.EqualTo(handles));
                Assert.That(initial.DamageValue, Is.EqualTo(source.InitialWeapon.DamageValue));
                Assert.That(initial.SpeedValue, Is.EqualTo(source.InitialWeapon.SpeedValue));
                Assert.That(inFlight.Stats.Damage, Is.EqualTo(15));
            }
        }

        [Test]
        public void ReconcileCanRemoveOneIdenticalCardAndPerkWithoutRemovingTheOther()
        {
            source.AddEquipment(0, damage, 0);
            source.AddPerk(speedPerk, PerkRarity.Bronze);
            restored.AddEquipment(0, damage, 0);
            restored.AddEquipment(0, damage, 0);
            restored.AddPerk(speedPerk, PerkRarity.Bronze);
            restored.AddPerk(speedPerk, PerkRarity.Bronze);

            restored.ReconcileState(database, source.CaptureState());

            Assert.That(restored.EquipmentCount, Is.EqualTo(1));
            Assert.That(restored.PerkCount, Is.EqualTo(1));
            Assert.That(restored.InitialWeapon.DamageValue, Is.EqualTo(source.InitialWeapon.DamageValue));
            Assert.That(restored.InitialWeapon.SpeedValue, Is.EqualTo(source.InitialWeapon.SpeedValue));
        }

        [Test]
        public void InvalidDefinitionOrRepeatedSlotFailsBeforeDestroyingTheExistingBuild()
        {
            var initial = restored.InitialWeapon;
            PlayerBuildSnapshot snapshot = source.CaptureState();
            snapshot.Equipment = new[] { new PlayerBuildEquipmentSnapshot
                { SlotIndex = 0, EquipmentId = uint.MaxValue, LevelIndex = 0 } };
            Assert.Throws<InvalidOperationException>(() => restored.RestoreState(database, snapshot));
            Assert.That(restored.InitialWeapon, Is.SameAs(initial));
            snapshot = source.CaptureState();
            snapshot.Weapons = new[] { snapshot.Weapons[0], snapshot.Weapons[0] };
            Assert.Throws<ArgumentException>(() => restored.RestoreState(database, snapshot));
            Assert.That(restored.InitialWeapon, Is.SameAs(initial));
        }

        [Test]
        public void SavedUpgradeResolvesTheRestoredEquipmentHandleWithoutDrawingAgain()
        {
            var oldHandle = source.AddEquipment(0, damage, 0);
            PlayerBuildSnapshot snapshot = source.CaptureState();
            var authority = source.gameObject.AddComponent<NetworkModifierSelection>();
            SetField(authority, "build", source);
            SetField(authority, "level", 4);
            SetField(authority, "experience", 1f);
            SetField(authority, "<PendingUpgradeCount>k__BackingField", 2);
            SetField(authority, "buildRevision", 8u);
            SetField(authority, "serverOffers", Array.AsReadOnly(new[]
            {
                new ModifierOffer(123ul, damage, 1, 0, oldHandle)
            }));
            PlayerProgressionSnapshot progression = authority.CaptureProgression();
            source.ClearBuild();
            restored.RestoreState(database, snapshot);
            var replacement = restored.gameObject.AddComponent<NetworkModifierSelection>();
            SetField(replacement, "build", restored);
            SetField(replacement, "provider", new EquipmentModifierOfferProvider(new NoDrawRandom()));
            SetField(replacement, "restoredOffers", progression.Offers);

            var offers = (IReadOnlyList<ModifierOffer>)typeof(NetworkModifierSelection)
                .GetMethod("ResolveRestoredOffers", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(replacement, null);

            Assert.That(progression.Level, Is.EqualTo(4));
            Assert.That(progression.Experience, Is.EqualTo(1f));
            Assert.That(progression.PendingUpgradeCount, Is.EqualTo(2));
            Assert.That(progression.BuildRevision, Is.EqualTo(8u));
            Assert.That(progression.Offers[0].PreviousOfferId, Is.EqualTo(123ul));
            Assert.That(offers.Count, Is.EqualTo(1));
            Assert.That(offers[0].EquipmentId, Is.EqualTo(damage.ID));
            Assert.That(offers[0].LevelIndex, Is.EqualTo(1));
            Assert.That(offers[0].ExistingEquipmentHandle,
                Is.EqualTo(restored.GetEquipmentStates()[0].Handle));
            restored.UpgradeEquipment(offers[0].ExistingEquipmentHandle, offers[0].LevelIndex);
            Assert.That(restored.EquipmentCount, Is.EqualTo(1));
            Assert.That(restored.GetEquipmentStates()[0].LevelIndex, Is.EqualTo(1));
        }

        private PlayerBuildRuntime CreateBuild(string name)
        {
            GameObject go = CreateObject(name);
            go.SetActive(false);
            PlayerMovement player = go.AddComponent<PlayerMovement>();
            var attacks = new GameObject("Attacks");
            attacks.transform.SetParent(go.transform, false);
            player.AttacksParent = attacks.transform;
            PlayerBuildRuntime result = go.AddComponent<PlayerBuildRuntime>();
            result.Initialize(player, new NoDrawRandom());
            return result;
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            objects.Add(result);
            return result;
        }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static T Asset<T>(string path) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            T asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
#else
            throw new NotSupportedException("Definition integration tests require the Editor.");
#endif
        }

        private sealed class NoDrawRandom : IRandomSource
        {
            public float Next01() => throw new InvalidOperationException("Restoring a saved offer must not draw RNG.");
        }
    }
}
