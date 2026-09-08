#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class CardPickMenuTests
    {
        private const string Content = "Assets/_Project/Content/HellMaiden/NativeGAS/";
        private GameObject ui, poolObject, playerObject, databaseObject, ownerObject;
        private CardPickMenu menu;
        private LocalPlayerUIBinder binder;
        private ModifierSelectionController selection;
        private PlayerBuildRuntime build;
        private EquipmentDB equipmentDB;
        private Button[] buttons;
        private ulong submitted;
        private int requests;

        [SetUp]
        public void SetUp()
        {
            poolObject = new GameObject("Card menu test pool");
            poolObject.AddComponent<PoolManager>().Init();
            playerObject = new GameObject("Card menu test build");
            playerObject.SetActive(false);
            var player = playerObject.AddComponent<PlayerMovement>();
            var attacks = new GameObject("Attacks");
            attacks.transform.SetParent(playerObject.transform, false);
            player.AttacksParent = attacks.transform;
            build = playerObject.AddComponent<PlayerBuildRuntime>();
            build.Initialize(player, new FixedRandom());
            databaseObject = new GameObject("Card menu test database");
            var database = databaseObject.AddComponent<RuntimeDB>();
            database.ConfigureWeaponDatabase(Asset<WeaponDB>(Content + "NativeGasWeaponDB.asset"));
            equipmentDB = Asset<EquipmentDB>(Content + "NativeGasEquipmentDB.asset");
            typeof(RuntimeDB).GetField("_equipmentDB", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(database, equipmentDB);
            build.StartInitialBuild(database);
            ownerObject = new GameObject("Card menu owner facade");
            ownerObject.AddComponent<NetworkIdentity>();
            selection = ownerObject.AddComponent<ModifierSelectionController>();
            selection.Bind(build);
            ui = Object.Instantiate(Asset<GameObject>("Assets/_Project/UI/CombatUI/CombatUI.prefab"));
            menu = ui.GetComponentInChildren<CardPickMenu>();
            binder = ui.GetComponent<LocalPlayerUIBinder>();
            binder.enabled = false;
            buttons = menu.GetComponentsInChildren<Button>();
            submitted = 0;
            requests = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(ownerObject);
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(databaseObject);
            Object.DestroyImmediate(poolObject);
            PoolManager.Instance = null;
        }

        private void Present(ulong firstId = 100, int count = 3)
        {
            selection.ReceiveOffers(equipmentDB.Equipments.Take(count)
                .Select((card, i) => new ModifierOffer(firstId + (ulong)i, card, 0)).ToArray(),
                id => { submitted = id; requests++; return true; });
        }

        [Test]
        public void AvailabilityFollowsMenuBindingRatherThanBuildBinding()
        {
            Assert.That(selection.IsPresentationReady, Is.False);
            menu.Bind(selection);
            Assert.That(selection.IsPresentationReady, Is.True);
            selection.Unbind();
            selection.Bind(build);
            Assert.That(selection.IsPresentationReady, Is.True);
            menu.enabled = false;
            Assert.That(selection.IsPresentationReady, Is.False);
            selection.Unbind();
            selection.Bind(build);
            Assert.That(selection.IsPresentationReady, Is.False,
                "Rebinding gameplay cannot make an unavailable view ready.");
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ReducedOfferHidesUnusedButtonsAndSubmitsOnlyAnIssuedOption(int count)
        {
            menu.Bind(selection);
            Present(count: count);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(buttons.Count(button => button.gameObject.activeSelf), Is.EqualTo(count));
            Assert.That(selection.Select(count).Succeeded, Is.False);
            buttons[count].onClick.Invoke();
            Assert.That(requests, Is.Zero);
            buttons[count - 1].onClick.Invoke();
            Assert.That(submitted, Is.EqualTo(100ul + (ulong)count - 1));
            Present(200);
            Assert.That(buttons.All(button => button.gameObject.activeSelf && button.interactable), Is.True,
                "A later full offer must restore all three options.");
        }

        [Test]
        public void Prefab_HasThreeBasicOptionsAndPreservesHealthPresentation()
        {
            Assert.That(buttons.Length, Is.EqualTo(3));
            Assert.That(menu.GetComponentsInChildren<TMP_Text>().Length, Is.EqualTo(3));
            Assert.That(ui.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            foreach (Button button in buttons)
            {
                Assert.That(button.GetComponent<Image>(), Is.Not.Null);
                Assert.That(button.GetComponentInChildren<TMP_Text>().font, Is.Not.Null);
            }
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(menu.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            Assert.That(ui.GetComponentInChildren<PlayerHealthHUD>(), Is.Not.Null);
            Assert.That(ui.GetComponentsInChildren<OverflowBar>().Length, Is.EqualTo(1));
            Assert.That(ui.GetComponentsInChildren<Transform>(true)
                .Count(child => child.name == "HealthBottomBar"), Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Button_SubmitsDisplayedIdentityAndWaitsForServer(int index)
        {
            menu.Bind(selection);
            Present();
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(selection.Offers.All(offer => !string.IsNullOrWhiteSpace(offer.DisplayName)), Is.True,
                "The minimal scene must display authored titles even when localization is not loaded.");
            for (int i = 0; i < 3; i++)
                Assert.That(buttons[i].GetComponentInChildren<TMP_Text>().text,
                    Is.EqualTo(selection.Offers[i].DisplayName));
            buttons[index].onClick.Invoke();
            Assert.That(submitted, Is.EqualTo(100ul + (ulong)index));
            Assert.That(menu.IsOpen, Is.True, "A request must not optimistically close the menu.");
            Assert.That(buttons.All(button => !button.interactable), Is.True);
            buttons[index].onClick.Invoke();
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(build.EquipmentCount, Is.Zero, "Presentation cannot apply gameplay state.");

            selection.CompleteRequest("Server rejected the request");
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(buttons.All(button => button.interactable), Is.True);
            buttons[index].onClick.Invoke();
            Assert.That(requests, Is.EqualTo(2));
            selection.ClearOffers();
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(menu.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
        }

        [Test]
        public void BindingAfterOffer_AdvancingAndUnbindingKeepOnlyCurrentPresentation()
        {
            Present();
            menu.Bind(selection);
            Assert.That(menu.IsOpen, Is.True);
            Present(200);
            buttons[1].onClick.Invoke();
            Assert.That(submitted, Is.EqualTo(201ul));
            menu.Unbind();
            Assert.That(menu.IsOpen, Is.False);
            Present(300);
            Assert.That(menu.IsOpen, Is.False);
            buttons[2].onClick.Invoke();
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(SubscriberCount(), Is.Zero);
            menu.Bind(selection);
            Assert.That(SubscriberCount(), Is.EqualTo(1));
            int cancellations = 0;
            selection.CancelRequested += () => cancellations++;
            menu.enabled = false;
            Assert.That(cancellations, Is.EqualTo(1));
            Assert.That(SubscriberCount(), Is.Zero);
            Assert.That(menu.IsOpen, Is.False);
        }

        [Test]
        public void Binder_RequiresLocalOwnershipAndDoesNotDependOnHealthHUD()
        {
            Assert.That(NetworkClient.active, Is.False);
            FieldInfo state = typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic);
            object previousState = state.GetValue(null);
            PropertyInfo localPlayer = typeof(NetworkClient).GetProperty("localPlayer");
            object previousLocalPlayer = localPlayer.GetValue(null);
            NetworkIdentity identity = ownerObject.GetComponent<NetworkIdentity>();
            PropertyInfo owned = typeof(NetworkIdentity).GetProperty("isOwned");
            try
            {
                Present();
                ui.GetComponentInChildren<CombatHUDController>().enabled = false;
                state.SetValue(null, Enum.Parse(state.FieldType, "Connected"));
                localPlayer.SetValue(null, identity);
                binder.enabled = true;
                binder.SendMessage("LateUpdate");
                Assert.That(menu.BoundSelection, Is.Null);
                owned.SetValue(identity, true);
                binder.SendMessage("LateUpdate");
                Assert.That(menu.BoundSelection, Is.SameAs(selection));
                Assert.That(menu.IsOpen, Is.True);
                owned.SetValue(identity, false);
                binder.SendMessage("LateUpdate");
                Assert.That(menu.IsOpen, Is.False);
                Assert.That(SubscriberCount(), Is.Zero);
                owned.SetValue(identity, true);
                binder.SendMessage("LateUpdate");
                Assert.That(menu.IsOpen, Is.True);
                state.SetValue(null, Enum.Parse(state.FieldType, "Disconnected"));
                binder.SendMessage("LateUpdate");
                Assert.That(menu.IsOpen, Is.False);
                Assert.That(SubscriberCount(), Is.Zero);
            }
            finally
            {
                binder.enabled = false;
                localPlayer.SetValue(null, previousLocalPlayer);
                state.SetValue(null, previousState);
                owned.SetValue(identity, false);
            }
        }

        private int SubscriberCount() => ((Delegate)typeof(ModifierSelectionController)
            .GetField("OffersChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(selection))?.GetInvocationList().Length ?? 0;

        private static T Asset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
        }

        private sealed class FixedRandom : IRandomSource
        {
            public float Next01() => 0.99f;
        }
    }
}
#endif
