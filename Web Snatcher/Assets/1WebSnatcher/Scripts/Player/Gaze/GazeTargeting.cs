
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class GazeTargeting : MonoBehaviour
    {
        [Header("Tracking")]
        [SerializeField] private Transform gazeOrigin;

        [Header("Target Acquisition")]
        [SerializeField, Min(0.1f)]
        private float maxTargetDistance = 5f;

        [SerializeField] private LayerMask bugLayerMask;

        [SerializeField, Range(0f, 0.25f)]
        private float aimAssistRadius = 0.09f;

        [SerializeField, Range(0f, 0.5f)]
        private float targetMemorySeconds = 0.15f;

        [Tooltip("Positive values aim slightly downward.")]
        [SerializeField, Range(-5f, 5f)]
        private float verticalAimOffsetDegrees = 1.5f;

        [Header("Visuals")]
        [SerializeField] private bool showTargetReticle = true;
        [SerializeField] private bool showGazeBeam = false;
        [SerializeField] private bool animateReticle = true;

        [SerializeField, Min(0.001f)]
        private float reticleLineWidth = 0.006f;

        [SerializeField] private Color targetingColor =
            new Color(0f, 0.95f, 1f, 1f);

        [Header("Debug")]
        [SerializeField] private bool drawEditorDebugRay = true;

        public BugTarget CurrentTarget { get; private set; }

        public event Action<BugTarget, BugTarget> TargetChanged;

        private LineRenderer reticle;
        private LineRenderer beam;
        private Material visualMaterial;

        private float lastSeenTime = float.NegativeInfinity;

        private const int RingSegments = 48;
        private const float TwoPi = Mathf.PI * 2f;

        private readonly Vector3[] ringPoints =
            new Vector3[RingSegments + 1];

        private void Awake()
        {
            ResolveCamera();

            if (bugLayerMask.value == 0)
            {
                int layer = LayerMask.NameToLayer("Bug");

                if (layer >= 0)
                    bugLayerMask = 1 << layer;
                else
                    Debug.LogError(
                        "[WebSnatcher] Missing Bug layer.", this);
            }

            CreateVisuals();
        }

        private void ResolveCamera()
        {
            if (gazeOrigin != null)
                return;

            OVRCameraRig rig =
                FindFirstObjectByType<OVRCameraRig>();

            if (rig != null && rig.centerEyeAnchor != null)
            {
                gazeOrigin = rig.centerEyeAnchor;
                return;
            }

            if (Camera.main != null)
                gazeOrigin = Camera.main.transform;
        }

        private void Update()
        {
            if (gazeOrigin == null)
                ResolveCamera();

            if (gazeOrigin == null)
            {
                SetTarget(null);
                HideVisuals();
                return;
            }

            // Pitch locally, relative to the headset.
            Quaternion correction = Quaternion.Euler(
                verticalAimOffsetDegrees, 0f, 0f);

            Vector3 direction =
                gazeOrigin.rotation * correction * Vector3.forward;

            Ray gazeRay = new Ray(
                gazeOrigin.position, direction);

            BugTarget detected = FindTarget(gazeRay);

            if (detected != null)
            {
                lastSeenTime = Time.time;
                SetTarget(detected);
            }
            else if (CurrentTarget != null &&
                     CurrentTarget.CanTarget &&
                     Time.time - lastSeenTime <
                     targetMemorySeconds)
            {
                // Keep the previous target temporarily.
            }
            else
            {
                SetTarget(null);
            }

            UpdateVisuals();

#if UNITY_EDITOR
            if (drawEditorDebugRay)
            {
                Debug.DrawRay(
                    gazeRay.origin,
                    gazeRay.direction * maxTargetDistance,
                    CurrentTarget != null
                        ? Color.cyan
                        : Color.yellow);
            }
#endif
        }

        private BugTarget FindTarget(Ray ray)
        {
            if (bugLayerMask.value == 0)
                return null;

            // Exact hit takes priority.
            if (Physics.Raycast(
                ray,
                out RaycastHit directHit,
                maxTargetDistance,
                bugLayerMask,
                QueryTriggerInteraction.Collide))
            {
                BugTarget direct =
                    directHit.collider
                        .GetComponentInParent<BugTarget>();

                if (direct != null && direct.CanTarget)
                    return direct;
            }

            if (aimAssistRadius <= 0f)
                return null;

            // Small physical tolerance around the gaze ray.
            // Non-allocating single-hit query.
            if (Physics.SphereCast(
                ray,
                aimAssistRadius,
                out RaycastHit assistedHit,
                maxTargetDistance,
                bugLayerMask,
                QueryTriggerInteraction.Collide))
            {
                BugTarget assisted =
                    assistedHit.collider
                        .GetComponentInParent<BugTarget>();

                if (assisted != null && assisted.CanTarget)
                    return assisted;
            }

            return null;
        }

        private void SetTarget(BugTarget next)
        {
            if (ReferenceEquals(CurrentTarget, next))
                return;

            BugTarget previous = CurrentTarget;

            if (previous != null)
                previous.SetHighlighted(false);

            CurrentTarget = next;

            if (next != null)
                next.SetHighlighted(true);

            TargetChanged?.Invoke(previous, next);
        }

        private void CreateVisuals()
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            visualMaterial = new Material(shader);
            visualMaterial.name = "WS_Gaze_Runtime";

            if (visualMaterial.HasProperty("_BaseColor"))
                visualMaterial.SetColor(
                    "_BaseColor", targetingColor);
            else if (visualMaterial.HasProperty("_Color"))
                visualMaterial.SetColor(
                    "_Color", targetingColor);

            GameObject reticleObject =
                new GameObject("Gaze_Target_Reticle");

            reticleObject.transform.SetParent(transform, false);

            reticle = reticleObject.AddComponent<LineRenderer>();
            ConfigureLine(reticle, reticleLineWidth);
            reticle.positionCount = RingSegments + 1;

            GameObject beamObject =
                new GameObject("Gaze_Target_Beam");

            beamObject.transform.SetParent(transform, false);

            beam = beamObject.AddComponent<LineRenderer>();
            ConfigureLine(beam, 0.003f);
            beam.positionCount = 2;

            HideVisuals();
        }

        private void ConfigureLine(
            LineRenderer line, float width)
        {
            line.sharedMaterial = visualMaterial;
            line.useWorldSpace = true;
            line.widthMultiplier = width;

            line.numCapVertices = 2;
            line.numCornerVertices = 2;

            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        private void UpdateVisuals()
        {
            if (CurrentTarget == null || gazeOrigin == null)
            {
                HideVisuals();
                return;
            }

            Vector3 center = CurrentTarget.AimPosition;

            Vector3 facing =
                gazeOrigin.position - center;

            if (facing.sqrMagnitude < 0.0001f)
                facing = -gazeOrigin.forward;

            facing.Normalize();

            Quaternion rotation =
                Quaternion.LookRotation(facing, Vector3.up);

            float radius = CurrentTarget.TargetRadius;

            if (animateReticle)
            {
                radius *= 1f +
                    Mathf.Sin(Time.time * 5f) * 0.06f;
            }

            center += facing * 0.015f;

            reticle.enabled = showTargetReticle;

            if (showTargetReticle)
            {
                for (int i = 0; i <= RingSegments; i++)
                {
                    float angle = i * TwoPi / RingSegments;

                    Vector3 local = new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f);

                    ringPoints[i] =
                        center + rotation * local;
                }

                reticle.SetPositions(ringPoints);
            }

            beam.enabled = showGazeBeam;

            if (showGazeBeam)
            {
                beam.SetPosition(
                    0,
                    gazeOrigin.position +
                    gazeOrigin.forward * 0.15f);

                beam.SetPosition(1, center);
            }
        }

        private void HideVisuals()
        {
            if (reticle != null)
                reticle.enabled = false;

            if (beam != null)
                beam.enabled = false;
        }

        public void SetTargetingEnabled(bool enabled)
        {
            this.enabled = enabled;

            if (!enabled)
            {
                SetTarget(null);
                HideVisuals();
            }
        }

        private void OnDisable()
        {
            SetTarget(null);
            HideVisuals();
        }

        private void OnDestroy()
        {
            if (visualMaterial != null)
                Destroy(visualMaterial);
        }
    }
}
