
using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class WebShieldController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LeftPalmShieldInput input;
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform centerEye;

        [Header("Precision Shield")]
        [SerializeField, Range(0.18f, 0.50f)]
        private float shieldRadius = 0.28f;

        [SerializeField, Range(0f, 0.25f)]
        private float forwardOffset = 0.06f;

        [SerializeField, Range(0f, 0.05f)]
        private float collisionPadding = 0.01f;

        [Header("Appearance")]
        [SerializeField] private Color webColor =
            new Color(1f, 0.64f, 0.30f, 1f);

        [SerializeField] private Color impactColor =
            new Color(1f, 0.84f, 0.48f, 1f);

        [SerializeField, Range(0.001f, 0.01f)]
        private float strandWidth = 0.003f;

        [SerializeField, Min(0.05f)]
        private float impactDuration = 0.24f;

        [SerializeField] private bool showDebugLogs = false;

        [Header("Quest Activation")]
        [SerializeField]
        private bool requireRaisedHandOnQuest = true;

        [SerializeField, Range(-0.6f, 0.2f)]
        private float minHandHeightBelowEyes = -0.48f;

        [SerializeField, Range(-0.1f, 0.6f)]
        private float minHandForwardOfEyes = 0.16f;

        public bool IsShieldActive { get; private set; }
        public Vector3 ShieldCenter { get; private set; }
        public float ShieldRadius => shieldRadius;

        private const int Segments = 40;
        private const int RingCount = 3;
        private const int SpokeCount = 12;

        private readonly LineRenderer[] rings =
            new LineRenderer[RingCount];

        private readonly LineRenderer[] spokes =
            new LineRenderer[SpokeCount];

        private LineRenderer impactRing;
        private Material webMaterial;
        private Material impactMaterial;

        private bool previousActive;
        private Vector3 shieldNormal;
        private Quaternion shieldRotation;

        private float lastImpactAt = -100f;
        private Vector3 lastImpactPosition;

        private void Awake()
        {
            ResolveReferences();
            BuildVisuals();
            SetShieldVisible(false);
        }

        private void ResolveReferences()
        {
            if (input == null)
                input =
                    FindFirstObjectByType<LeftPalmShieldInput>();

            OVRCameraRig rig =
                FindFirstObjectByType<OVRCameraRig>();

            if (rig != null)
            {
                if (leftHandAnchor == null)
                    leftHandAnchor = rig.leftHandAnchor;

                if (centerEye == null)
                    centerEye = rig.centerEyeAnchor;
            }

            if (centerEye == null && Camera.main != null)
                centerEye = Camera.main.transform;
        }

        private void Update()
        {
            if (input == null ||
                leftHandAnchor == null ||
                centerEye == null)
            {
                ResolveReferences();
            }

            if (input == null ||
                leftHandAnchor == null ||
                centerEye == null)
            {
                IsShieldActive = false;
                SetShieldVisible(false);
                UpdateImpact();
                return;
            }

            // Position follows the actual tracked left-hand anchor.
            ShieldCenter =
                leftHandAnchor.position +
                centerEye.forward * forwardOffset;

            // The disc faces the headset, so the web stays
            // readable and blocks front-facing projectiles.
            shieldNormal =
                centerEye.position - ShieldCenter;

            if (shieldNormal.sqrMagnitude < 0.0001f)
                shieldNormal = -centerEye.forward;

            shieldNormal.Normalize();

            shieldRotation = Quaternion.LookRotation(
                shieldNormal, Vector3.up);

            bool active = input.IsOpen;

#if !UNITY_EDITOR
            if (requireRaisedHandOnQuest)
            {
                Vector3 relative =
                    leftHandAnchor.position -
                    centerEye.position;

                bool raised =
                    relative.y >= minHandHeightBelowEyes &&
                    Vector3.Dot(
                        relative, centerEye.forward) >=
                    minHandForwardOfEyes;

                active &= raised;
            }
#endif

            IsShieldActive = active;

            if (active != previousActive)
            {
                previousActive = active;

                if (showDebugLogs)
                {
                    Debug.Log(
                        active
                        ? "[WS Shield] DEPLOYED"
                        : "[WS Shield] LOWERED",
                        this);
                }
            }

            SetShieldVisible(active);

            if (active)
                DrawShield();

            UpdateImpact();
        }

        private void BuildVisuals()
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogError(
                    "[WS Shield] Could not find an unlit shader.",
                    this);
                return;
            }

            webMaterial = CreateMaterial(
                shader, "WS_Shield_Web", webColor);

            impactMaterial = CreateMaterial(
                shader, "WS_Shield_Impact", impactColor);

            for (int i = 0; i < RingCount; i++)
            {
                rings[i] = CreateLine(
                    "WebRing_" + i,
                    Segments + 1,
                    strandWidth);
            }

            for (int i = 0; i < SpokeCount; i++)
            {
                spokes[i] = CreateLine(
                    "WebStrand_" + i,
                    2,
                    strandWidth * 0.6f);
            }

            impactRing = CreateLine(
                "ShieldImpact",
                Segments + 1,
                strandWidth * 2.5f);

            if (impactRing != null)
            {
                impactRing.sharedMaterial = impactMaterial;
                impactRing.enabled = false;
            }
        }

        private static Material CreateMaterial(
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

        private LineRenderer CreateLine(
            string name,
            int positionCount,
            float width)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(transform, false);

            LineRenderer line =
                obj.AddComponent<LineRenderer>();

            line.sharedMaterial = webMaterial;
            line.positionCount = positionCount;
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            return line;
        }

        private void DrawShield()
        {
            // Concentric spiderweb rings.
            for (int r = 0; r < rings.Length; r++)
            {
                if (rings[r] == null)
                    continue;

                float radius =
                    shieldRadius * (r + 1f) / RingCount;

                for (int i = 0; i <= Segments; i++)
                {
                    float angle =
                        i * Mathf.PI * 2f / Segments;

                    // Slight distortion creates a more
                    // natural woven-web appearance.
                    float distortion =
                        1f +
                        0.018f *
                        Mathf.Sin(angle * SpokeCount);

                    Vector3 local = new Vector3(
                        Mathf.Cos(angle) * radius *
                            distortion,
                        Mathf.Sin(angle) * radius *
                            distortion,
                        0f);

                    rings[r].SetPosition(
                        i,
                        ShieldCenter +
                        shieldRotation * local);
                }
            }

            // Radial web strands.
            for (int i = 0; i < spokes.Length; i++)
            {
                if (spokes[i] == null)
                    continue;

                float angle =
                    i * Mathf.PI * 2f / SpokeCount;

                Vector3 direction =
                    shieldRotation *
                    new Vector3(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle),
                        0f);

                spokes[i].SetPosition(
                    0, ShieldCenter);

                spokes[i].SetPosition(
                    1,
                    ShieldCenter +
                    direction * shieldRadius);
            }
        }

        private void SetShieldVisible(bool visible)
        {
            foreach (LineRenderer ring in rings)
            {
                if (ring != null)
                    ring.enabled = visible;
            }

            foreach (LineRenderer spoke in spokes)
            {
                if (spoke != null)
                    spoke.enabled = visible;
            }
        }

        // Existing projectile API is preserved.
        // Uses segment-versus-disc collision instead of
        // treating the shield as a large sphere.
        public bool TryBlockSegment(
            Vector3 from,
            Vector3 to,
            float projectileRadius)
        {
            if (!isActiveAndEnabled ||
                !IsShieldActive)
                return false;

            Vector3 delta = to - from;
            float segmentLengthSquared =
                delta.sqrMagnitude;

            if (segmentLengthSquared < 0.0000001f)
                return false;

            float radius =
                Mathf.Max(0f, projectileRadius);

            float startDistance =
                Vector3.Dot(
                    from - ShieldCenter,
                    shieldNormal);

            float endDistance =
                Vector3.Dot(
                    to - ShieldCenter,
                    shieldNormal);

            float distanceChange =
                endDistance - startDistance;

            float t;

            if (Mathf.Abs(distanceChange) >
                0.00001f)
            {
                // Closest position to shield plane.
                t = Mathf.Clamp01(
                    -startDistance / distanceChange);
            }
            else
            {
                // Nearly parallel travel: choose the
                // nearest point along the segment.
                t = Mathf.Clamp01(
                    Vector3.Dot(
                        ShieldCenter - from, delta) /
                    segmentLengthSquared);
            }

            Vector3 closest = from + delta * t;

            float planeDistance = Mathf.Abs(
                Vector3.Dot(
                    closest - ShieldCenter,
                    shieldNormal));

            // Projectile must actually touch the plane.
            if (planeDistance >
                radius + collisionPadding)
                return false;

            Vector3 radial =
                closest - ShieldCenter;

            radial -= shieldNormal *
                Vector3.Dot(radial, shieldNormal);

            float allowedRadius =
                shieldRadius + radius + collisionPadding;

            if (radial.sqrMagnitude >
                allowedRadius * allowedRadius)
                return false;

            RegisterImpact(closest);

            return true;
        }

        private void RegisterImpact(Vector3 position)
        {
            lastImpactPosition = position;
            lastImpactAt = Time.time;

            if (impactRing != null)
                impactRing.enabled = true;

            if (showDebugLogs)
                Debug.Log(
                    "[WS Shield] PRECISION BLOCK",
                    this);
        }

        private void UpdateImpact()
        {
            if (impactRing == null ||
                !impactRing.enabled)
                return;

            float elapsed =
                Time.time - lastImpactAt;

            float t =
                Mathf.Clamp01(
                    elapsed / impactDuration);

            if (t >= 1f)
            {
                impactRing.enabled = false;
                return;
            }

            float radius = Mathf.Lerp(
                0.025f,
                Mathf.Min(0.22f, shieldRadius),
                t);

            float thickness =
                strandWidth * Mathf.Lerp(3f, 0.4f, t);

            impactRing.widthMultiplier = thickness;

            for (int i = 0; i <= Segments; i++)
            {
                float angle =
                    i * Mathf.PI * 2f / Segments;

                Vector3 offset =
                    shieldRotation *
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f);

                impactRing.SetPosition(
                    i,
                    lastImpactPosition +
                    shieldNormal * 0.004f +
                    offset);
            }
        }

        private void OnDisable()
        {
            IsShieldActive = false;
            previousActive = false;
            SetShieldVisible(false);

            if (impactRing != null)
                impactRing.enabled = false;
        }

        private void OnDestroy()
        {
            if (webMaterial != null)
                Destroy(webMaterial);

            if (impactMaterial != null)
                Destroy(impactMaterial);
        }
    }
}
