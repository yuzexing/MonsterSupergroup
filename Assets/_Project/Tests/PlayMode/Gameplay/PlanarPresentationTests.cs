#if UNITY_EDITOR
using System.Collections;
using System.IO;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlanarPresentationTests
    {
        [Test]
        public void ProjectionPreservesWorldXYAndDoesNotChangeColliderTransform()
        {
            var root = new GameObject("tilted effect");
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(2, -3, 0), Quaternion.Euler(45, 0, 0));
                var collider = root.AddComponent<PolygonCollider2D>();
                collider.points = new[] { new Vector2(-1, 0), Vector2.up, Vector2.right };
                var rotation = root.transform.rotation;
                var point = root.transform.TransformPoint(new Vector3(0, -20, 0));
                Assert.That(point.z, Is.LessThan(-10));
                var projected = GameplayPlanarEffect.Project(point);
                Assert.That((Vector2)projected, Is.EqualTo((Vector2)point));
                Assert.That(projected.z, Is.Zero);
                root.AddComponent<GameplayPlanarEffect>();
                Assert.That(root.transform.rotation, Is.EqualTo(rotation));
                Assert.That(collider.points, Is.EqualTo(new[] { new Vector2(-1, 0), Vector2.up, Vector2.right }));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator PerspectiveCameraRendersOppositeDepthsAtTheSamePlanarSize()
        {
            var cameraObject = new GameObject("production perspective");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var camera = cameraObject.AddComponent<Camera>();
            var texture = new RenderTexture(512, 512, 24);
            var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
            Material material = null;
            try
            {
                camera.orthographic = false; camera.fieldOfView = 80;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << 30; camera.targetTexture = texture;
                quad.layer = 30;
                var renderer = quad.GetComponent<MeshRenderer>();
                foreach (string shader in new[] { "MonsterSupergroup/PlanarSprite", "MonsterSupergroup/PlanarVfx" })
                {
                material = new Material(Shader.Find(shader));
                if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                renderer.sharedMaterial = material;
                var projection = quad.AddComponent<GameplayPlanarEffect>();
                int[] widths = new int[2];
                Directory.CreateDirectory("Logs/ManualPlayFix/projection");
                for (int i = 0; i < 2; i++)
                {
                    // Identical XY quads at near/far depths reproduce the imported tilt's
                    // magnification without relying on randomized particle shape or animation.
                    quad.transform.position = new Vector3(0, 0, i == 0 ? -7 : 7);
                    projection.RefreshBounds();
                    yield return null;
                    camera.Render();
                    RenderTexture.active = texture;
                    image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply();
                    int min = 512, max = -1;
                    for (int x = 0; x < 512; x++) if (image.GetPixel(x, 256).maxColorComponent > .1f) { min = Mathf.Min(min, x); max = x; }
                    widths[i] = max - min + 1;
                    File.WriteAllBytes("Logs/ManualPlayFix/projection/" + shader.Split('/')[1] + "-depth-" + i + ".png", image.EncodeToPNG());
                }
                Assert.That(widths[0], Is.GreaterThan(20), "Effect must render using the actual perspective camera.");
                Assert.That(widths[0], Is.EqualTo(widths[1]).Within(1), "Depth alone must not change effect size.");
                Assert.That(camera.orthographic, Is.False);
                Object.DestroyImmediate(projection); Object.DestroyImmediate(material); material = null;
                }
            }
            finally
            {
                RenderTexture.active = null; camera.targetTexture = null;
                Object.DestroyImmediate(material); Object.DestroyImmediate(image); Object.DestroyImmediate(texture);
                Object.DestroyImmediate(quad); Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
#endif
