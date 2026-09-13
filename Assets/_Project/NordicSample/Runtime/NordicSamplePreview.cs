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

        private void Awake()
        {
            Application.runInBackground = true;
            if (ground == null || cameraRig == null || character == null || characterVisual == null || characterAnimator == null)
                throw new InvalidOperationException("Nordic preview requires Ground, camera and character references.");
            startPosition = character.position;
            obstacleFilter = new ContactFilter2D { useTriggers = false };
            obstacleFilter.SetLayerMask(LayerMask.GetMask("Obstacles"));
            ApplyGroundBounds();
            cameraRig.RemoveAllCameraTargets();
            cameraRig.AddCameraTarget(character.transform);
            cameraRig.Reset();
            character.GetComponent<NordicSortAnchor>().RefreshOrder();
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
        }

        private void Update()
        {
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

        private void ConstrainCameraView()
        {
            // Reset/teleport and render-target changes can bypass the plugin's movement delta.
            // Constrain the actual view after the plugin, using the same Ground bounds.
            Camera camera = cameraRig.GameCamera;
            Bounds bounds = MapBounds;
            float halfHeight = -camera.transform.position.z * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * camera.aspect;
            Vector3 position = camera.transform.position;
            position.x = Mathf.Clamp(position.x, bounds.min.x + halfWidth, bounds.max.x - halfWidth);
            position.y = Mathf.Clamp(position.y, bounds.min.y + halfHeight, bounds.max.y - halfHeight);
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
                    character.position = position;
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
            Bounds bounds = MapBounds;
            position.x = Mathf.Clamp(position.x, bounds.min.x + .2f, bounds.max.x - .2f);
            position.y = Mathf.Clamp(position.y, bounds.min.y + .2f, bounds.max.y - .2f);
            character.position = position;
            Physics2D.SyncTransforms();
            // Reject numerical penetration at convex corners after the sliding cast.
            if (Physics2D.OverlapCircle(position, .13f, obstacleFilter, overlaps) > 0)
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
            character.position = position;
            character.transform.position = new Vector3(position.x, position.y, 0);
            Physics2D.SyncTransforms();
            character.GetComponent<NordicSortAnchor>().RefreshOrder();
            cameraRig.Reset();
            ConstrainCameraView();
        }

        private void OnGUI()
        {
            if (!showHelp) return;
            GUI.Box(new Rect(16, 16, 380, 60), "MIDGARD / STATIC SAMPLE\nWASD / Arrows: move     R: reset\n64 x 40    Perspective 80    URP 2D / Linear");
        }
    }
}
