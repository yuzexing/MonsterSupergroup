using System;
using Com.LuisPedroFonseca.ProCamera2D;
using UnityEngine;

namespace MonsterSupergroup.NordicSample
{
    [DefaultExecutionOrder(100)]
    public sealed class NordicSamplePreview : MonoBehaviour
    {
        public SpriteRenderer ground;
        public ProCamera2D cameraRig;
        public Rigidbody2D character;
        public Transform characterVisual;
        public Animator characterAnimator;
        public float moveSpeed = 5;
        public bool acceptInput = true;
        public bool showHelp = true;
        private Vector2 input;
        private Vector2 startPosition;
        private ContactFilter2D obstacleFilter;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];
        private readonly Collider2D[] overlaps = new Collider2D[16];
        private const float Skin = 0.01f;
        public Bounds MapBounds => ground.bounds;
        public float FootRadius => character.GetComponent<CircleCollider2D>().radius * Mathf.Abs(character.transform.lossyScale.x);
        public Vector2 SpawnPosition => startPosition;

        private void Awake()
        {
            Application.runInBackground = true;
            if (ground == null || cameraRig == null || character == null || characterVisual == null || characterAnimator == null)
                throw new InvalidOperationException("Nordic preview requires Ground, camera and character references.");
            obstacleFilter = new ContactFilter2D { useTriggers = false };
            obstacleFilter.SetLayerMask(LayerMask.GetMask("Obstacles"));
            ApplyGroundBounds();
            cameraRig.RemoveAllCameraTargets();
            cameraRig.AddCameraTarget(character.transform);
            cameraRig.Reset();
            Teleport(character.position);
            startPosition = character.position;
        }

        public void ApplyGroundBounds()
        {
            Bounds bounds = ground.bounds;
            var limits = cameraRig.GetComponent<ProCamera2DNumericBoundaries>();
            limits.LeftBoundary = bounds.min.x;
            limits.RightBoundary = bounds.max.x;
            limits.BottomBoundary = bounds.min.y;
            limits.TopBoundary = bounds.max.y;
            limits.UseLeftBoundary = limits.UseRightBoundary = limits.UseTopBoundary = limits.UseBottomBoundary = true;
            limits.UseNumericBoundaries = true;
            limits.UseSoftBoundaries = false;
            // Numeric Boundaries also registers an automatic zoom-to-fit override.
            // Keep its position limits, but the sample must retain its saved lens.
            if (Application.isPlaying) cameraRig.RemoveSizeOverrider(limits);
        }

        private void Update()
        {
            // Prepare viewport/cache before the plugin processes a resized window.
            ConstrainCameraView();
            if (acceptInput)
            {
                float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0)
                          - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
                float y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0)
                          - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
                input = Vector2.ClampMagnitude(new Vector2(x, y), 1);
                if (Input.GetKeyDown(KeyCode.R)) Teleport(startPosition);
            }
            if (input.x != 0)
            {
                Vector3 scale = characterVisual.localScale;
                // The imported Axeldor parts already face left at positive scale.
                scale.x = Mathf.Abs(scale.x) * (input.x < 0 ? 1 : -1);
                characterVisual.localScale = scale;
            }
            bool walking = input.sqrMagnitude > 0.01f;
            characterAnimator.SetBool("Walking", walking);
            // This preview uses casts against static scenery, not force-driven physics.
            // Advance once per rendered frame so LateUpdate follows a fresh position.
            Move(input * (moveSpeed * Time.deltaTime));
        }

        private void LateUpdate() => ConstrainCameraView();

        public void ConstrainCameraView()
        {
            // Reset/teleport and render-target changes can bypass the plugin's movement delta.
            // Constrain the actual view after the plugin, using the same Ground bounds.
            Camera camera = cameraRig.GetComponent<Camera>();
            Bounds bounds = MapBounds;
            float halfHeight = -camera.transform.position.z * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            float displayAspect = camera.targetTexture != null ? (float)camera.targetTexture.width / camera.targetTexture.height :
                (float)Screen.width / Mathf.Max(1, Screen.height);
            int pixelHeight = camera.targetTexture != null ? camera.targetTexture.height : Screen.height;
            // Fractional viewport edges are rounded to pixels by Unity. Reserve one
            // pixel per side when fitting the whole map, then clamp the actual rays.
            float worldPixel = halfHeight * 2 / Mathf.Max(1, pixelHeight);
            float effectiveAspect = Mathf.Min(displayAspect, (bounds.size.x - worldPixel * 2) / (halfHeight * 2));
            float viewportWidth = effectiveAspect / displayAspect;
            camera.rect = new Rect((1 - viewportWidth) * .5f, 0, viewportWidth, 1);
            camera.aspect = effectiveAspect;
            if (Application.isPlaying && cameraRig.GameCamera != null &&
                Mathf.Abs(cameraRig.ScreenSizeInWorldCoordinates.x - halfHeight * 2 * effectiveAspect) > .001f)
            {
                cameraRig.CalculateScreenSize();
                camera.aspect = effectiveAspect;
            }
            Vector3 position = camera.transform.position;
            Ray bottomRay = camera.ViewportPointToRay(Vector3.zero);
            Ray topRay = camera.ViewportPointToRay(Vector3.one);
            Vector3 minOffset = bottomRay.origin + bottomRay.direction * (-bottomRay.origin.z / bottomRay.direction.z) - position;
            Vector3 maxOffset = topRay.origin + topRay.direction * (-topRay.origin.z / topRay.direction.z) - position;
            position.x = Mathf.Clamp(position.x, bounds.min.x - minOffset.x, bounds.max.x - maxOffset.x);
            position.y = Mathf.Clamp(position.y, bounds.min.y - minOffset.y, bounds.max.y - maxOffset.y);
            camera.transform.position = position;
        }

        public void SetPreviewInput(Vector2 direction) => input = Vector2.ClampMagnitude(direction, 1);

        public void Move(Vector2 delta)
        {
            Vector2 position = character.position;
            Vector2 originalPosition = position;
            Physics2D.SyncTransforms();
            float distance = delta.magnitude;
            if (distance > 0)
            {
                Vector2 direction = delta / distance;
                int count = character.Cast(direction, obstacleFilter, hits, distance + Skin);
                float allowed = distance;
                Vector2 normal = Vector2.zero;
                for (int i = 0; i < count; i++)
                {
                    if (hits[i].distance - Skin < allowed)
                    {
                        allowed = Mathf.Max(0, hits[i].distance - Skin);
                        normal = hits[i].normal;
                    }
                }
                position += direction * allowed;
                // A small second cast permits sliding along roots instead of sticking to corners.
                if (allowed < distance && normal != Vector2.zero)
                {
                    Vector2 remaining = delta - direction * allowed;
                    Vector2 slide = remaining - Vector2.Dot(remaining, normal) * normal;
                    PublishPosition(position);
                    Physics2D.SyncTransforms();
                    float slideDistance = slide.magnitude;
                    if (slideDistance > Skin)
                    {
                        int slides = character.Cast(slide / slideDistance, obstacleFilter, hits, slideDistance + Skin);
                        for (int i = 0; i < slides; i++) slideDistance = Mathf.Min(slideDistance, Mathf.Max(0, hits[i].distance - Skin));
                        position += slide.normalized * slideDistance;
                    }
                }
            }
            position = ClampFoot(position);
            PublishPosition(position);
            // Reject numerical penetration at convex corners after the sliding cast.
            if (Physics2D.OverlapCircle(position, FootRadius, obstacleFilter, overlaps) > 0)
            {
                character.position = originalPosition;
                Physics2D.SyncTransforms();
            }
            // Rigidbody2D.position alone can leave the rendered Transform at the
            // preceding physics step. Publish this kinematic pose before camera follow.
            Vector2 finalPosition = character.position;
            character.transform.position = new Vector3(finalPosition.x, finalPosition.y, 0);
            Physics2D.SyncTransforms();
        }

        public void Teleport(Vector2 position)
        {
            obstacleFilter = new ContactFilter2D { useTriggers = false };
            obstacleFilter.SetLayerMask(LayerMask.GetMask("Obstacles"));
            Physics2D.SyncTransforms();
            PublishPosition(FindFreePosition(ClampFoot(position)));
            if (Application.isPlaying) cameraRig.Reset();
            else cameraRig.transform.position = new Vector3(character.position.x, character.position.y, -10);
            ConstrainCameraView();
        }

        private void PublishPosition(Vector2 position)
        {
            character.position = position;
            character.transform.position = new Vector3(position.x, position.y, 0);
            Physics2D.SyncTransforms();
        }

        private Vector2 ClampFoot(Vector2 position)
        {
            Bounds b = MapBounds;
            float margin = FootRadius + Skin;
            return new Vector2(Mathf.Clamp(position.x, b.min.x + margin, b.max.x - margin),
                Mathf.Clamp(position.y, b.min.y + margin, b.max.y - margin));
        }

        public bool IsFree(Vector2 point)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(LayerMask.GetMask("Obstacles"));
            return (ClampFoot(point) - point).sqrMagnitude < .000001f &&
                Physics2D.OverlapCircle(point, FootRadius + Skin, filter, overlaps) == 0;
        }

        private Vector2 FindFreePosition(Vector2 origin)
        {
            if (IsFree(origin)) return origin;
            // Nearest valid point on a 0.25-unit grid; no scenery is removed.
            Vector2 best = default;
            float bestDistance = float.PositiveInfinity;
            int limit = Mathf.CeilToInt(MapBounds.size.magnitude * 4);
            for (int ring = 1; ring <= limit; ring++)
            {
                for (int i = -ring; i <= ring; i++)
                {
                    Consider(new Vector2(i, -ring)); Consider(new Vector2(i, ring));
                    if (Mathf.Abs(i) != ring) { Consider(new Vector2(-ring, i)); Consider(new Vector2(ring, i)); }
                }
                if ((ring + 1) * .25f > bestDistance) return best;
            }
            throw new InvalidOperationException("No unblocked spawn position within Ground.");
            void Consider(Vector2 offset)
            {
                Vector2 p = origin + offset * .25f;
                float distance = Vector2.Distance(p, origin);
                if (distance < bestDistance && IsFree(p)) { best = p; bestDistance = distance; }
            }
        }

        private void OnGUI()
        {
            if (!showHelp) return;
            GUI.Box(new Rect(16, 16, 430, 60), "MIDGARD / STATIC SAMPLE\nWASD / Arrows: move     R: reset\n4 x 4 views / 119.34 x 67.13    Perspective 80");
        }
    }
}
