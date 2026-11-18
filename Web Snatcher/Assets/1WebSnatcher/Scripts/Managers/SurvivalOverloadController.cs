using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Charged, skill-based web shot. Fires along a fixed heading: NO homing and misses are possible.
    /// Public interface preserved for SurvivalGameManager and SurvivalStatusHUD (V17).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurvivalOverloadController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SurvivalGameManager game;
        [SerializeField] private FistGestureInput rightFist;
        [SerializeField] private GazeTargeting gaze;
        [SerializeField] private Transform head;
        [SerializeField] private Transform rightHand;

        [Header("Charge and firing")]
        [SerializeField, Min(1)] private int catchesPerCharge = 5;
        [Tooltip("Allow firing if the Striker is within this angle of the center-eye direction. Does NOT steer the projectile.")]
        [SerializeField, Range(2f, 45f)] private float aimAngle = 19f;
        [SerializeField, Min(0.5f)] private float maximumAimDistance = 7f;
        [SerializeField, Min(2f)] private float projectileSpeed = 11f;
        [SerializeField, Min(0.02f)] private float projectileRadius = 0.15f;
        [SerializeField, Min(0f)] private float targetExtraRadius = 0.06f;
        [SerializeField, Min(1f)] private float maxFlightDistance = 9f;
        [Tooltip("If true, uses the Striker's root collider geometry when possible; a fallback sphere is used otherwise.")]
        [SerializeField] private bool useColliderBounds = true;

        [Header("Appearance")]
        [SerializeField] private Color webColor = new Color(0.97f, 0.88f, 0.73f, 1f);
        [SerializeField] private Color impactColor = new Color(1f, 0.38f, 0.18f, 1f);
        [SerializeField, Min(0.01f)] private float orbDiameter = 0.11f;
        [SerializeField, Min(0.1f)] private float impactSeconds = 0.38f;
        [SerializeField] private bool logShots = true;

        public int Progress { get; private set; }
        public int RequiredCatches => catchesPerCharge;
        public bool IsCharged => Progress >= catchesPerCharge;
        public bool IsFiring => firing;

        public float AimAngle => aimAngle;
        public float MaximumAimDistance => maximumAimDistance;
        public bool IsScoutTargeting =>
            gaze != null && gaze.CurrentTarget != null;
            
        public event Action ChargeChanged;

        public event Action<Vector3, Vector3> ShotLaunched;
        public event Action<Vector3> ShotHit;
        public event Action<Vector3> ShotMissed;

        public Vector3 CurrentShotPosition => shotPosition;

        private bool firing, bursting;
        private float impactAt, flightDistance;
        private GameObject shotTarget;
        private Collider targetCollider;
        private Vector3 shotDirection, shotPosition, impactPosition;
        private Transform orb;
        private LineRenderer trail, ring;
        private Material silk, impact;
        private const int Segments = 32;

        public void Initialize(SurvivalGameManager owner) => game = owner;

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<SurvivalGameManager>();
            if (rightFist == null) rightFist = FindFirstObjectByType<FistGestureInput>();
            if (gaze == null) gaze = FindFirstObjectByType<GazeTargeting>();
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (head == null) head = rig.centerEyeAnchor;
                if (rightHand == null) rightHand = rig.rightHandAnchor;
            }
            if (head == null && Camera.main != null) head = Camera.main.transform;
            BuildVisuals();
        }

        private void OnEnable()
        {
            if (rightFist != null) rightFist.FistClosed += HandleFist;
        }

        private void OnDisable()
        {
            if (rightFist != null) rightFist.FistClosed -= HandleFist;
            firing = false;
            bursting = false;
            HideVisuals();
        }

        public void AddCatch()
        {
            if (game == null || !game.IsRunning || IsCharged) return;
            Progress = Mathf.Min(catchesPerCharge, Progress + 1);
            ChargeChanged?.Invoke();
            if (IsCharged) Debug.Log("[WS Overload] CHARGED - face Striker and close fist", this);
        }

        private void HandleFist()
        {
            if (game == null || !game.IsRunning || !IsCharged || firing || head == null) return;
            // Scouts have priority so the player can still catch normally while charged.
            if (gaze != null && gaze.CurrentTarget != null) return;
            GameObject striker = game.ActiveStriker;
            if (striker == null || !striker.activeInHierarchy) return;
            Vector3 toTarget = striker.transform.position - head.position;
            float distance = toTarget.magnitude;
            if (distance < 0.1f || distance > maximumAimDistance) return;
            if (Vector3.Angle(head.forward, toTarget) > aimAngle) return;

            shotTarget = striker;
            targetCollider = striker.GetComponent<Collider>();
            if (targetCollider == null) targetCollider = striker.GetComponentInChildren<Collider>();
            // IMPORTANT: head aiming determines trajectory; NOT hand forward, NOT target's changing position.
            shotDirection = head.forward.normalized;
            // Begin in front of the eyes to avoid intersecting the player, while preserving gaze direction.
            shotPosition = head.position + shotDirection * 0.38f;
            flightDistance = 0f;
            firing = true;
            bursting = false;
            Progress = 0;
            ChargeChanged?.Invoke();
            orb.position = shotPosition;
            orb.gameObject.SetActive(true);
            trail.enabled = true;
            trail.SetPosition(0, shotPosition);
            trail.SetPosition(1, shotPosition);

            ShotLaunched?.Invoke(shotPosition, shotDirection);
            if (logShots) Debug.Log("[WS Overload] FIRED (unguided)", this);
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame &&
                game != null && game.IsRunning)
            {
                Progress = catchesPerCharge;
                ChargeChanged?.Invoke();
            }
#endif
            if (!firing) return;
            if (bursting) { AnimateImpact(); return; }
            if (shotTarget == null || !shotTarget.activeInHierarchy)
            {
                ShotMissed?.Invoke(shotPosition);
                Finish();
                return;
            }

            float step = projectileSpeed * Mathf.Min(Time.deltaTime, 0.06f);
            Vector3 previous = shotPosition;
            shotPosition += shotDirection * step;
            flightDistance += step;
            orb.position = shotPosition;
            trail.SetPosition(0, shotPosition - shotDirection * Mathf.Min(0.45f, flightDistance));
            trail.SetPosition(1, shotPosition);

            // Swept collision against a MOVING Striker. A small sphere tolerance is used
            // so the mobile VR shot feels fair, while the actual shot can still miss.
            Vector3 center = shotTarget.transform.position;
            float targetRadius = targetExtraRadius + 0.24f;
            if (useColliderBounds && targetCollider != null && targetCollider.enabled)
            {
                Bounds bounds = targetCollider.bounds;
                center = bounds.center;
                // Clamp large wing/collider bounds so a broad wingspan is not an automatic hit.
                targetRadius = Mathf.Clamp(Mathf.Max(bounds.extents.x,
                    Mathf.Max(bounds.extents.y, bounds.extents.z)), 0.08f, 0.32f)
                    + targetExtraRadius;
            }
            if (SegmentPassesSphere(previous, shotPosition, center, targetRadius + projectileRadius))
            {
                impactPosition = center;
                orb.gameObject.SetActive(false);
                trail.enabled = false;
                bursting = true;
                impactAt = Time.time;
                ring.enabled = true;
                ShotHit?.Invoke(impactPosition);
                game.DefeatStriker(shotTarget);
                if (logShots) Debug.Log("[WS Overload] HIT", this);
                return;
            }
            if (flightDistance >= maxFlightDistance)
            {
                ShotMissed?.Invoke(shotPosition);

                if (logShots)
                    Debug.Log("[WS Overload] MISSED - charge consumed", this);

                Finish();
            }
        }

        private static bool SegmentPassesSphere(Vector3 a, Vector3 b, Vector3 center, float radius)
        {
            Vector3 ab = b - a;
            float magnitude2 = ab.sqrMagnitude;
            float t = magnitude2 > 0.0000001f ?
                Mathf.Clamp01(Vector3.Dot(center - a, ab) / magnitude2) : 0f;
            Vector3 nearest = a + ab * t;
            return (center - nearest).sqrMagnitude <= radius * radius;
        }

        private void AnimateImpact()
        {
            float t = Mathf.Clamp01((Time.time - impactAt) / impactSeconds);
            Vector3 facing = head != null ? head.position - impactPosition : Vector3.back;
            if (facing.sqrMagnitude < 0.001f) facing = Vector3.back;
            Quaternion rotation = Quaternion.LookRotation(facing.normalized);
            float radius = Mathf.Lerp(0.04f, 0.65f, t);
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / Segments);
                ring.SetPosition(i, impactPosition + rotation *
                    new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
            if (t >= 1f) Finish();
        }

        private void Finish()
        {
            firing = false;
            bursting = false;
            shotTarget = null;
            targetCollider = null;
            HideVisuals();
        }

        private void HideVisuals()
        {
            if (orb != null) orb.gameObject.SetActive(false);
            if (trail != null) trail.enabled = false;
            if (ring != null) ring.enabled = false;
        }

        private static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            Material material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            return material;
        }

        private void BuildVisuals()
        {
            silk = MakeMaterial("WS_Survival_Silk", webColor);
            impact = MakeMaterial("WS_Survival_Impact", impactColor);
            GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "WS_Survival_Overload_Orb";
            projectile.transform.SetParent(transform, false);
            projectile.transform.localScale = Vector3.one * orbDiameter;
            Collider collider = projectile.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            projectile.GetComponent<Renderer>().sharedMaterial = silk;
            orb = projectile.transform;
            trail = MakeLine("WS_Survival_Overload_Trail", silk, 2, 0.009f);
            ring = MakeLine("WS_Survival_Overload_Burst", impact, Segments + 1, 0.008f);
            HideVisuals();
        }

        private LineRenderer MakeLine(string name, Material material, int vertices, float width)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            LineRenderer lr = obj.AddComponent<LineRenderer>();
            lr.sharedMaterial = material;
            lr.positionCount = vertices;
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void OnDestroy()
        {
            if (silk != null) Destroy(silk);
            if (impact != null) Destroy(impact);
        }
    }
}
