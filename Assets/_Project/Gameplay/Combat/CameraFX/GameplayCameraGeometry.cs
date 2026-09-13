using UnityEngine;
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
        public static void ConstrainNordic(Camera camera, ProCamera2D rig, Bounds map)
        {
            camera.orthographic = false; camera.fieldOfView = 80;
            var position = camera.transform.position; position.z = -10;
            camera.transform.SetPositionAndRotation(position, Quaternion.identity);
            float halfHeight = 10 * Mathf.Tan(40 * Mathf.Deg2Rad);
            int height = camera.targetTexture != null ? camera.targetTexture.height : Screen.height;
            float displayAspect = camera.targetTexture != null ? (float)camera.targetTexture.width / height : (float)Screen.width / Mathf.Max(1, height);
            float pixel = 2 * halfHeight / Mathf.Max(1, height);
            float aspect = Mathf.Min(displayAspect, (map.size.x - 2 * pixel) / (2 * halfHeight));
            float width = aspect / displayAspect;
            camera.rect = new Rect((1 - width) * .5f, 0, width, 1); camera.aspect = aspect;
            if (Application.isPlaying && rig.GameCamera != null && Mathf.Abs(rig.ScreenSizeInWorldCoordinates.x - 2 * halfHeight * aspect) > .001f)
            { rig.CalculateScreenSize(); camera.aspect = aspect; }
            Bounds view = ViewBounds(camera);
            position.x += Mathf.Clamp(view.center.x, map.min.x + view.extents.x, map.max.x - view.extents.x) - view.center.x;
            position.y += Mathf.Clamp(view.center.y, map.min.y + view.extents.y, map.max.y - view.extents.y) - view.center.y;
            camera.transform.position = position;
        }
    }
}
