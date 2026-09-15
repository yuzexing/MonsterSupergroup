using UnityEngine;
using System.Collections.Generic;
using Com.LuisPedroFonseca.ProCamera2D;

namespace MonsterSupergroup.Gameplay.Combat
{
    public static class GameplayCameraGeometry
    {
        private static readonly Plane GroundPlane = new Plane(Vector3.forward, Vector3.zero);
        public static Vector3 OnGround(Ray ray) => GroundPlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : ray.origin;
        public static Bounds ViewBounds(Camera camera)
        {
            Vector3 a = OnGround(camera.ViewportPointToRay(Vector3.zero));
            var bounds = new Bounds(a, Vector3.zero);
            bounds.Encapsulate(OnGround(camera.ViewportPointToRay(Vector3.right)));
            bounds.Encapsulate(OnGround(camera.ViewportPointToRay(Vector3.up)));
            bounds.Encapsulate(OnGround(camera.ViewportPointToRay(Vector3.one)));
            return bounds;
        }
        public static Bounds ClampView(Bounds view, Bounds map)
        {
            var center = view.center;
            center.x = Mathf.Clamp(center.x, map.min.x + view.extents.x, map.max.x - view.extents.x);
            center.y = Mathf.Clamp(center.y, map.min.y + view.extents.y, map.max.y - view.extents.y);
            view.center = center;
            return view;
        }

        // Zero means at least one participant still sees the enemy. No views means no decision.
        public static float MinimumOutsideDistance(Vector2 position, IReadOnlyList<Bounds> views)
        {
            float result = float.PositiveInfinity;
            foreach (var view in views)
            {
                var outside = new Vector2(Mathf.Max(0, Mathf.Abs(position.x - view.center.x) - view.extents.x),
                    Mathf.Max(0, Mathf.Abs(position.y - view.center.y) - view.extents.y));
                result = Mathf.Min(result, outside.magnitude);
            }
            return result;
        }

        public static void ConstrainNordic(Camera camera, ProCamera2D rig, Bounds map, float distance = 10)
        {
            camera.orthographic = false; camera.fieldOfView = 80;
            var position = camera.transform.position; position.z = -distance;
            camera.transform.SetPositionAndRotation(position, Quaternion.identity);
            float halfHeight = distance * Mathf.Tan(40 * Mathf.Deg2Rad);
            int height = camera.targetTexture != null ? camera.targetTexture.height : Screen.height;
            float displayAspect = camera.targetTexture != null ? (float)camera.targetTexture.width / height : (float)Screen.width / Mathf.Max(1, height);
            float pixel = 2 * halfHeight / Mathf.Max(1, height);
            float aspect = Mathf.Min(displayAspect, (map.size.x - 2 * pixel) / (2 * halfHeight));
            float width = aspect / displayAspect;
            camera.rect = new Rect((1 - width) * .5f, 0, width, 1); camera.aspect = aspect;
            if (Application.isPlaying && rig.GameCamera != null && Mathf.Abs(rig.ScreenSizeInWorldCoordinates.x - 2 * halfHeight * aspect) > .001f)
            { rig.CalculateScreenSize(); camera.aspect = aspect; }
            Bounds view = ViewBounds(camera);
            var clamped = ClampView(view, map);
            position.x += clamped.center.x - view.center.x;
            position.y += clamped.center.y - view.center.y;
            camera.transform.position = position;
        }
    }
}
