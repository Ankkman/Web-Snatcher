
using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Visual-only enhancement for the existing skill-based
    /// SurvivalOverloadController.
    ///
    /// Does not move the projectile, adjust aim,
    /// apply damage or determine hits.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OverloadCombatVFX : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private SurvivalOverloadController overload;

        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform head;

        [Header("Web Appearance")]
        [SerializeField] private Color silkColor =
            new Color(1f, 0.76f, 0.43f, 1f);

        [SerializeField] private Color brightColor =
            new Color(1f, 0.92f, 0.72f, 1f);

        [SerializeField, Range(0.001f, 0.012f)]
        private float strandWidth = 0.004f;

        [SerializeField, Range(0.1f, 0.9f)]
        private float trailLength = 0.48f;

        [SerializeField, Range(0.005f, 0.04f)]
        private float braidRadius = 0.016f;

        [SerializeField, Range(1f, 8f)]
        private float braidTwists = 3f;

        [Header("Right Hand Launch")]
        [SerializeField, Range(0.05f, 0.3f)]
        private float launchDuration = 0.13f;

        [Header("Impact / Dissolve")]
        [SerializeField, Range(0.1f, 0.6f)]
        private float hitDuration = 0.32f;

        [SerializeField, Range(0.08f, 0.5f)]
        private float hitMaxRadius = 0.28f;

        [SerializeField, Range(0.08f, 0.4f)]
        private float missDuration = 0.18f;

        [SerializeField, Range(0.03f, 0.2f)]
        private float missMaxRadius = 0.085f;

        private const int StrandPoints = 13;
        private const int RingPoints = 33;

        private readonly LineRenderer[] strands =
            new LineRenderer[3];

        private LineRenderer launchLine;
        private LineRenderer burstRing;

        private Material silkMaterial;
        private Material brightMaterial;

        private bool subscribed;
        private bool shotVisible;
        private bool burstVisible;
        private bool isHitBurst;
        private bool built;

        private float shotStartedAt;
        private float burstStartedAt;

        private Vector3 launchPoint;
        private Vector3 direction;
        private Vector3 burstPosition;

        private void Awake()
        {
            ResolveReferences();
            BuildVisuals();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        private void Start()
        {
            ResolveReferences();
            Subscribe();
        }

        private void ResolveReferences()
        {
            if (overload == null)
            {
                overload =
                    GetComponent<SurvivalOverloadController>();

                if (overload == null)
                {
                    overload =
                        FindFirstObjectByType<
                            SurvivalOverloadController>();
                }
            }

            OVRCameraRig rig =
                FindFirstObjectByType<OVRCameraRig>();

            if (rig != null)
            {
                if (rightHand == null)
                    rightHand = rig.rightHandAnchor;

                if (head == null)
                    head = rig.centerEyeAnchor;
            }

            if (head == null && Camera.main != null)
                head = Camera.main.transform;
        }

        private void Subscribe()
        {
            if (overload == null || subscribed)
                return;

            overload.ShotLaunched += OnShotLaunched;
            overload.ShotHit += OnShotHit;
            overload.ShotMissed += OnShotMissed;

            subscribed = true;
        }

        private void BuildVisuals()
        {
            if (built)
                return;

            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogError(
                    "[WS Overload VFX] Unlit shader unavailable.",
                    this);
                return;
            }

            silkMaterial =
                MakeMaterial(shader, "WS_BraidedSilk", silkColor);

            brightMaterial =
                MakeMaterial(shader, "WS_BurstAmber", brightColor);

            for (int i = 0; i < strands.Length; i++)
            {
                strands[i] = MakeLine(
                    "BraidedWeb_" + i,
                    StrandPoints,
                    strandWidth,
                    silkMaterial);
            }

            launchLine = MakeLine(
                "HandLaunch",
                2,
                strandWidth * 2.3f,
                brightMaterial);

            burstRing = MakeLine(
                "OverloadBurst",
                RingPoints,
                strandWidth * 2.5f,
                brightMaterial);

            HideShot();
            burstRing.enabled = false;

            built = true;
        }

        private static Material MakeMaterial(
            Shader shader,
            string materialName,
            Color color)
        {
            Material result = new Material(shader);
            result.name = materialName;

            if (result.HasProperty("_BaseColor"))
                result.SetColor("_BaseColor", color);
            else if (result.HasProperty("_Color"))
                result.SetColor("_Color", color);

            return result;
        }

        private LineRenderer MakeLine(
            string name,
            int pointCount,
            float width,
            Material material)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(transform, false);

            LineRenderer line =
                obj.AddComponent<LineRenderer>();

            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = pointCount;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            return line;
        }

        private void OnShotLaunched(
            Vector3 origin,
            Vector3 shotDirection)
        {
            if (!built)
                return;

            launchPoint = rightHand != null
                ? rightHand.position
                : origin;

            direction = shotDirection.normalized;

            shotStartedAt = Time.time;
            shotVisible = true;
            burstVisible = false;

            foreach (LineRenderer strand in strands)
                strand.enabled = true;

            launchLine.enabled = true;
            burstRing.enabled = false;
        }

        private void OnShotHit(Vector3 position)
        {
            StartBurst(position, true);
        }

        private void OnShotMissed(Vector3 position)
        {
            StartBurst(position, false);
        }

        private void StartBurst(
            Vector3 position,
            bool hit)
        {
            if (!built)
                return;

            shotVisible = false;
            HideShot();

            burstPosition = position;
            burstStartedAt = Time.time;
            isHitBurst = hit;
            burstVisible = true;

            burstRing.enabled = true;
        }

        private void LateUpdate()
        {
            if (!built)
                return;

            if (shotVisible)
            {
                if (overload == null || !overload.IsFiring)
                {
                    HideShot();
                    shotVisible = false;
                }
                else
                {
                    DrawBraidedWeb();
                    UpdateLaunch();
                }
            }

            if (burstVisible)
                UpdateBurst();
        }

        private void DrawBraidedWeb()
        {
            Vector3 tip =
                overload.CurrentShotPosition;

            Vector3 forward = direction;

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            forward.Normalize();

            // Basis vectors perpendicular to travel.
            Vector3 reference =
                Mathf.Abs(Vector3.Dot(forward, Vector3.up))
                > 0.94f
                ? Vector3.right
                : Vector3.up;

            Vector3 side =
                Vector3.Cross(forward, reference).normalized;

            Vector3 up =
                Vector3.Cross(side, forward).normalized;

            float age = Time.time - shotStartedAt;

            // Avoid drawing a long trail at the
            // first instant of launch.
            float length = Mathf.Min(
                trailLength,
                Mathf.Max(0.01f, age * 7f));

            for (int strandIndex = 0;
                 strandIndex < strands.Length;
                 strandIndex++)
            {
                LineRenderer line = strands[strandIndex];

                float phase =
                    strandIndex * Mathf.PI * 2f / 3f;

                for (int pointIndex = 0;
                     pointIndex < StrandPoints;
                     pointIndex++)
                {
                    float t =
                        pointIndex / (float)(StrandPoints - 1);

                    float angle =
                        t * Mathf.PI * 2f * braidTwists
                        + phase
                        + age * 12f;

                    // Reduce web thickness near
                    // the tail of the trail.
                    float radius =
                        braidRadius * Mathf.SmoothStep(
                            0.15f, 1f, t);

                    Vector3 offset =
                        (side * Mathf.Cos(angle) +
                         up * Mathf.Sin(angle))
                        * radius;

                    Vector3 position =
                        tip -
                        forward * length * (1f - t)
                        + offset;

                    line.SetPosition(pointIndex, position);
                }
            }
        }

        private void UpdateLaunch()
        {
            float elapsed =
                Time.time - shotStartedAt;

            if (elapsed >= launchDuration)
            {
                launchLine.enabled = false;
                return;
            }

            if (rightHand != null)
                launchPoint = rightHand.position;

            Vector3 tip =
                overload.CurrentShotPosition;

            // Pure visual bridge between the fist
            // and the gaze-aligned projectile.
            launchLine.SetPosition(0, launchPoint);
            launchLine.SetPosition(1, tip);

            launchLine.widthMultiplier =
                strandWidth * 2.3f *
                (1f - elapsed / launchDuration);

            launchLine.enabled = true;
        }

        private void UpdateBurst()
        {
            float duration =
                isHitBurst
                    ? hitDuration
                    : missDuration;

            float t = Mathf.Clamp01(
                (Time.time - burstStartedAt) /
                Mathf.Max(0.01f, duration));

            if (t >= 1f)
            {
                burstVisible = false;
                burstRing.enabled = false;
                return;
            }

            float maxRadius =
                isHitBurst
                    ? hitMaxRadius
                    : missMaxRadius;

            float radius =
                Mathf.Lerp(0.015f, maxRadius, t);

            Vector3 normal =
                head != null
                    ? head.position - burstPosition
                    : -direction;

            if (normal.sqrMagnitude < 0.0001f)
                normal = Vector3.back;

            Quaternion rotation =
                Quaternion.LookRotation(normal.normalized);

            burstRing.widthMultiplier =
                strandWidth *
                (isHitBurst ? 3f : 1.5f) *
                (1f - t);

            for (int i = 0; i < RingPoints; i++)
            {
                float angle =
                    i * Mathf.PI * 2f /
                    (RingPoints - 1);

                Vector3 offset = rotation *
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f);

                burstRing.SetPosition(
                    i, burstPosition + offset);
            }
        }

        private void HideShot()
        {
            foreach (LineRenderer strand in strands)
            {
                if (strand != null)
                    strand.enabled = false;
            }

            if (launchLine != null)
                launchLine.enabled = false;
        }

        private void OnDisable()
        {
            if (subscribed && overload != null)
            {
                overload.ShotLaunched -= OnShotLaunched;
                overload.ShotHit -= OnShotHit;
                overload.ShotMissed -= OnShotMissed;
                subscribed = false;
            }

            shotVisible = false;
            burstVisible = false;

            HideShot();

            if (burstRing != null)
                burstRing.enabled = false;
        }

        private void OnDestroy()
        {
            if (silkMaterial != null)
                Destroy(silkMaterial);

            if (brightMaterial != null)
                Destroy(brightMaterial);
        }
    }
}
