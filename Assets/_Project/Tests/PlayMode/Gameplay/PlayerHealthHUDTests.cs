#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AstralShift.HellMaiden.UI;
using kcp2k;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerHealthHUDTests
    {
        private const string PrefabPath = "Assets/_Project/UI/CombatUI/CombatUI.prefab";
        private readonly List<GameObject> objects = new List<GameObject>();
        private GameObject root;
        private CombatHUDController controller;
        private PlayerHealthHUD healthHUD;
        private LocalPlayerUIBinder binder;
        private CanvasGroup group;
        private Image fill;
        private Image bottomFill;
        private OverflowBar bar;
        private TMP_Text currentText;
        private TMP_Text maximumText;

        [SetUp]
        public void SetUp()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            root = Object.Instantiate(prefab);
            objects.Add(root);
            controller = root.GetComponentInChildren<CombatHUDController>();
            healthHUD = root.GetComponentInChildren<PlayerHealthHUD>();
            binder = root.GetComponent<LocalPlayerUIBinder>();
            group = controller.GetComponent<CanvasGroup>();
            bar = GetField<OverflowBar>(healthHUD, "healthBar");
            fill = GetField<Image>(bar, "topBar");
            bottomFill = GetField<Image>(bar, "bottomBar");
            currentText = GetField<TMP_Text>(healthHUD, "currentHealthText");
            maximumText = GetField<TMP_Text>(healthHUD, "maxHealthText");
            binder.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }
            objects.Clear();
        }

        [Test]
        public void Prefab_RetainsOriginalPresentation_AndRuntimeStartsHidden()
        {
            Assert.That(root.GetComponent<SceneUIManager>(), Is.TypeOf<GameplayUIRoot>());
            Assert.That(root.GetComponentInChildren<CombatUIManager>(true), Is.Null);
            Assert.That(root.GetComponentInChildren<OverflowBar>(true), Is.SameAs(bar));
            Assert.That(bottomFill.name, Is.EqualTo("HealthBottomBar"));
            Assert.That(bar.overflowImage, Is.Not.Null);
            Assert.That(GetField<Image>(bar, "blinkBar"), Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<NetworkBehaviour>(true), Is.Empty);
            Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
            Assert.That(fill.sprite, Is.Not.Null);
            Assert.That(group.alpha, Is.Zero);
            Assert.That(currentText.text, Is.Empty);
            Assert.That(maximumText.text, Is.Empty);
            Assert.That(fill.fillAmount, Is.Zero);

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(asset.GetComponentInChildren<CanvasGroup>().alpha, Is.EqualTo(1));
            var assetHUD = asset.GetComponentInChildren<PlayerHealthHUD>();
            Assert.That(GetField<TMP_Text>(assetHUD, "currentHealthText").text, Is.EqualTo("100"));
            Assert.That(GetField<TMP_Text>(assetHUD, "maxHealthText").text, Is.EqualTo("100"));
            Assert.That(GetField<Image>(GetField<OverflowBar>(assetHUD, "healthBar"), "topBar")
                .fillAmount, Is.EqualTo(1));
        }

        [Test]
        public void Bind_ReadsExistingDamage_ThenTracksDamageHealMaxHealthAndCanonicalState()
        {
            CombatantBehaviour player = Player(100);
            player.ReceiveDamage(new DamageInfo(1, 25, false));
            controller.Bind(player);
            AssertDisplay(75, 100);
            player.ReceiveDamage(new DamageInfo(2, 10, false));
            AssertDisplay(65, 100);
            player.RestoreHealth(5);
            AssertDisplay(70, 100);
            player.SetMaximumHealthPreservingMissingHealth(150);
            AssertDisplay(120, 150);
            player.ApplyCanonicalHealth(120, 200, 1);
            AssertDisplay(120, 200); // MaxHP-only change still refreshes the ratio.
            player.ApplyCanonicalHealth(0, 200, 2);
            AssertDisplay(0, 200);
            player.Initialize(80);
            AssertDisplay(80, 80);
        }

        [Test]
        public void RebindAndUnbind_RemoveFormerPlayersSubscription()
        {
            CombatantBehaviour first = Player(100);
            CombatantBehaviour second = Player(200);
            controller.Bind(first);
            controller.Bind(first);
            Assert.That(SubscriberCount(first), Is.EqualTo(1));
            controller.Bind(second);
            Assert.That(SubscriberCount(first), Is.Zero);
            Assert.That(SubscriberCount(second), Is.EqualTo(1));
            first.ReceiveDamage(new DamageInfo(1, 20, false));
            AssertDisplay(200, 200);
            controller.Unbind();
            controller.Unbind();
            Assert.That(SubscriberCount(second), Is.Zero);
            second.ReceiveDamage(new DamageInfo(2, 30, false));
            Assert.That(group.alpha, Is.Zero);
            Assert.That(currentText.text, Is.Empty);
            Assert.That(maximumText.text, Is.Empty);
            Assert.That(fill.fillAmount, Is.Zero);
        }

        [Test]
        public void HideKeepsBinding_ModuleDisableUnsubscribes_EnableReadsLatestState()
        {
            CombatantBehaviour player = Player(100);
            controller.Bind(player);
            controller.Hide();
            player.ReceiveDamage(new DamageInfo(1, 20, false));
            AssertDisplay(80, 100);
            Assert.That(group.alpha, Is.Zero);
            controller.Show();
            Assert.That(group.alpha, Is.EqualTo(1));
            healthHUD.enabled = false;
            Assert.That(SubscriberCount(player), Is.Zero);
            player.ReceiveDamage(new DamageInfo(2, 15, false));
            healthHUD.enabled = true;
            Assert.That(SubscriberCount(player), Is.EqualTo(1));
            AssertDisplay(65, 100);
            root.SetActive(false);
            Assert.That(SubscriberCount(player), Is.Zero);
            Assert.That(controller.BoundCombatant, Is.Null);
        }

        [Test]
        public void DestroyHUD_RemovesSubscriptionFromSurvivingPlayer()
        {
            CombatantBehaviour player = Player(100);
            controller.Bind(player);
            Object.DestroyImmediate(root);
            Assert.That(SubscriberCount(player), Is.Zero);
            player.ReceiveDamage(new DamageInfo(1, 20, false));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Rebind_CancelsPendingBottomBarAndOverflowAnimations()
        {
            SetField(bar, "animateFall", true);
            SetField(bar, "animateRise", true);
            bottomFill.gameObject.SetActive(true);
            bar.overflowBars = new List<OverflowBar.OverflowTresholds>
            {
                new OverflowBar.OverflowTresholds { overflowTreshold = 0, bar = fill.sprite },
                new OverflowBar.OverflowTresholds { overflowTreshold = 150, bar = fill.sprite }
            };
            CombatantBehaviour first = Player(200);
            controller.Bind(first);
            first.ReceiveDamage(new DamageInfo(1, 120, false));
            Assert.That(bottomFill.fillAmount, Is.GreaterThan(fill.fillAmount));
            Assert.That(bar.overflowImage.gameObject.activeSelf, Is.True);
            CombatantBehaviour next = Player(100);
            next.ReceiveDamage(new DamageInfo(2, 40, false));
            controller.Bind(next);
            AssertDisplay(60, 100);
            Assert.That(bar.overflowImage.gameObject.activeSelf, Is.False);
            yield return new WaitForSeconds(1.2f);
            AssertDisplay(60, 100);
            next.ReceiveDamage(new DamageInfo(3, 20, false));
            controller.Unbind();
            yield return new WaitForSeconds(0.6f);
            Assert.That(fill.fillAmount, Is.Zero);
            Assert.That(bottomFill.fillAmount, Is.Zero);
            Assert.That(bar.overflowImage.gameObject.activeSelf, Is.False);
            Assert.That(SubscriberCount(next), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Host_BindsOnlyLocalPlayer_ReplacesPlayer_AndUnbindsOnDisconnect()
        {
            GameObject network = new GameObject("Health HUD Host Test");
            objects.Add(network);
            network.SetActive(false);
            var transport = network.AddComponent<KcpTransport>();
            transport.Port = 0;
            var manager = network.AddComponent<NetworkManager>();
            manager.transport = transport;
            manager.autoCreatePlayer = false;
            manager.dontDestroyOnLoad = false;
            network.SetActive(true);
            try
            {
                binder.enabled = true; // UI before player.
                manager.StartHost();
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!NetworkClient.ready && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                Assert.That(NetworkClient.ready, Is.True);
                CombatantBehaviour local = Player(100, true);
                Assert.That(NetworkServer.AddPlayerForConnection(
                    NetworkServer.localConnection, local.gameObject), Is.True);
                CombatantBehaviour remote = Player(250, true);
                NetworkServer.Spawn(remote.gameObject);
                yield return null;
                Assert.That(controller.BoundCombatant, Is.SameAs(local));
                AssertDisplay(100, 100);
                remote.ReceiveDamage(new DamageInfo(1, 70, false));
                AssertDisplay(100, 100);
                local.ReceiveDamage(new DamageInfo(2, 30, false));
                AssertDisplay(70, 100);

                root.SetActive(false);
                Assert.That(SubscriberCount(local), Is.Zero);
                local.RestoreHealth(10);
                root.SetActive(true); // Player before UI.
                yield return null;
                AssertDisplay(80, 100);

                CombatantBehaviour replacement = Player(180, true);
                Assert.That(NetworkServer.ReplacePlayerForConnection(
                    NetworkServer.localConnection, replacement.gameObject,
                    ReplacePlayerOptions.KeepAuthority), Is.True);
                yield return null;
                Assert.That(local.GetComponent<NetworkIdentity>().isOwned, Is.True,
                    "An owned former player must not be selected over localPlayer.");
                Assert.That(controller.BoundCombatant, Is.SameAs(replacement));
                Assert.That(SubscriberCount(local), Is.Zero);
                local.ReceiveDamage(new DamageInfo(3, 10, false));
                AssertDisplay(180, 180);

                manager.StopHost();
                yield return null;
                Assert.That(controller.BoundCombatant, Is.Null);
                Assert.That(group.alpha, Is.Zero);
                Assert.That(currentText.text, Is.Empty);
            }
            finally
            {
                if (NetworkServer.active || NetworkClient.active)
                {
                    manager.StopHost();
                }
                NetworkClient.Shutdown();
                NetworkServer.Shutdown();
                NetworkManager.ResetStatics();
                Transport.active = null;
            }
        }

        [Test]
        public void ClientBinding_HandlesOwnershipLossRuntimeDestructionAndReconnect()
        {
            // Isolate client-side lifecycle from transport and gameplay bootstrap.
            // The separate Host test above exercises real Mirror spawn/replacement.
            Assert.That(NetworkServer.active, Is.False);
            Assert.That(NetworkClient.active, Is.False);
            FieldInfo state = typeof(NetworkClient).GetField(
                "connectState", BindingFlags.Static | BindingFlags.NonPublic);
            object previousState = state.GetValue(null);
            PropertyInfo localPlayer = typeof(NetworkClient).GetProperty("localPlayer");
            try
            {
                state.SetValue(null, Enum.Parse(state.FieldType, "Connected"));
                CombatantBehaviour local = Player(120, true);
                NetworkIdentity identity = local.GetComponent<NetworkIdentity>();
                typeof(NetworkIdentity).GetProperty("isOwned").SetValue(identity, true);
                localPlayer.SetValue(null, identity);
                binder.enabled = true;
                binder.SendMessage("LateUpdate");
                AssertDisplay(120, 120);
                local.ApplyCanonicalHealth(75, 150, 1);
                AssertDisplay(75, 150);
                typeof(NetworkIdentity).GetProperty("isOwned").SetValue(identity, false);
                binder.SendMessage("LateUpdate");
                Assert.That(controller.BoundCombatant, Is.Null);
                Assert.That(SubscriberCount(local), Is.Zero);
                typeof(NetworkIdentity).GetProperty("isOwned").SetValue(identity, true);
                binder.SendMessage("LateUpdate");
                AssertDisplay(75, 150);
                Object.DestroyImmediate(local);
                binder.SendMessage("LateUpdate");
                Assert.That(group.alpha, Is.Zero);
                CombatantBehaviour rebuilt = identity.gameObject.AddComponent<CombatantBehaviour>();
                rebuilt.Initialize(90);
                binder.SendMessage("LateUpdate");
                AssertDisplay(90, 90);
                state.SetValue(null, Enum.Parse(state.FieldType, "Disconnected"));
                binder.SendMessage("LateUpdate");
                Assert.That(SubscriberCount(rebuilt), Is.Zero);
                Assert.That(group.alpha, Is.Zero);
                state.SetValue(null, Enum.Parse(state.FieldType, "Connected"));
                binder.SendMessage("LateUpdate");
                AssertDisplay(90, 90);
                binder.enabled = false;
                Assert.That(SubscriberCount(rebuilt), Is.Zero);
                Assert.That(group.alpha, Is.Zero);
            }
            finally
            {
                binder.enabled = false;
                localPlayer.SetValue(null, null);
                state.SetValue(null, previousState);
            }
        }

        private CombatantBehaviour Player(int maximum, bool networked = false)
        {
            var player = new GameObject("Health HUD Player Test");
            objects.Add(player);
            if (networked)
            {
                player.AddComponent<NetworkIdentity>();
            }
            var combatant = player.AddComponent<CombatantBehaviour>();
            combatant.Initialize(maximum);
            return combatant;
        }

        private void AssertDisplay(int current, int maximum)
        {
            Assert.That(currentText.text, Is.EqualTo(current.ToString()));
            Assert.That(maximumText.text, Is.EqualTo(maximum.ToString()));
            Assert.That(fill.fillAmount, Is.EqualTo((float)current / maximum).Within(0.0001f));
            Assert.That(bottomFill.fillAmount, Is.EqualTo((float)current / maximum).Within(0.0001f));
        }

        private static int SubscriberCount(CombatantBehaviour combatant)
        {
            var subscribers = GetField<Delegate>(combatant, "HealthChanged");
            return subscribers?.GetInvocationList().Length ?? 0;
        }

        private static T GetField<T>(object instance, string name)
        {
            return (T)FindField(instance.GetType(), name).GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            FindField(instance.GetType(), name).SetValue(instance, value);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) return field;
                type = type.BaseType;
            }
            throw new MissingFieldException(name);
        }
    }
}
#endif
