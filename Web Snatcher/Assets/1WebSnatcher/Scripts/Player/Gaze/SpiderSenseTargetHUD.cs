using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// World-space target brackets. No central crosshair, no screen canvas,
    /// no continuous beam. Pure visuals; target choice belongs to GazeTargeting.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpiderSenseTargetHUD : MonoBehaviour
    {
        [Header("Auto-resolved References")]
        [SerializeField] private GazeTargeting gaze;
        [SerializeField] private Transform centerEye;

        [Header("Target Brackets")]
        [SerializeField] private Color acquiredColor = new Color(0.90f, 0.55f, 0.27f, 1f);
        [SerializeField] private Color webLockColor = new Color(0.93f, 0.28f, 0.19f, 1f);
        [SerializeField, Range(1.0f, 2.0f)] private float paddingMultiplier = 1.35f;
        [SerializeField, Range(0.15f, 0.65f)] private float cornerLengthFraction = 0.32f;
        [SerializeField, Range(0.001f, 0.02f)] private float lineWidthAtOneMetre = 0.0028f;
        [SerializeField, Range(0.05f, 0.5f)] private float webLockFlashSeconds = 0.18f;
        [SerializeField, Range(1f, 30f)] private float appearanceSpeed = 18f;

        private readonly LineRenderer[] corners = new LineRenderer[4];
        private Transform bracketRoot;
        private Material acquiredMaterial;
        private Material lockedMaterial;
        private BugTarget observedTarget;
        private BugTarget flashTarget;
        private float flashEndTime;
        private float visualScale;
        private bool subscribed;

        private void Awake()
        {
            ResolveReferences();
            CreateVisuals();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        private void ResolveReferences()
        {
            if (gaze == null)
                gaze = FindFirstObjectByType<GazeTargeting>();

            if (centerEye != null) return;

            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null) centerEye = rig.centerEyeAnchor;
            if (centerEye == null && Camera.main != null)
                centerEye = Camera.main.transform;
        }

        private void Subscribe()
        {
            if (subscribed || gaze == null) return;
            gaze.TargetChanged += OnTargetChanged;
            subscribed = true;
            OnTargetChanged(null, gaze.CurrentTarget);
        }

        private void Unsubscribe()
        {
            if (subscribed && gaze != null)
                gaze.TargetChanged -= OnTargetChanged;
            subscribed = false;
            if (observedTarget != null)
                observedTarget.CatchStarted -= OnCatchStarted;
            observedTarget = null;
        }

        private void OnTargetChanged(BugTarget oldTarget, BugTarget newTarget)
        {
            if (observedTarget != null)
                observedTarget.CatchStarted -= OnCatchStarted;

            observedTarget = newTarget;
            if (observedTarget != null)
                observedTarget.CatchStarted += OnCatchStarted;

            visualScale = 0.78f;
        }

        private void OnCatchStarted(BugTarget bug)
        {
            flashTarget = bug;
            flashEndTime = Time.time + webLockFlashSeconds;
        }

        private static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            Material material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            return material;
        }

        private void CreateVisuals()
        {
            acquiredMaterial = MakeMaterial("WS_Target_Acquired_Amber", acquiredColor);
            lockedMaterial = MakeMaterial("WS_Target_WebLock_Red", webLockColor);
            if (acquiredMaterial == null || lockedMaterial == null)
            {
                Debug.LogError("[WebSnatcher] No compatible Unlit shader for SpiderSenseTargetHUD.", this);
                enabled = false;
                return;
            }

            bracketRoot = new GameObject("SpiderSense_WorldBrackets_Runtime").transform;
            bracketRoot.SetParent(transform, false);
            bracketRoot.gameObject.SetActive(false);

            for (int i = 0; i < 4; i++)
            {
                GameObject piece = new GameObject("Bracket_" + i);
                piece.transform.SetParent(bracketRoot, false);
                LineRenderer lr = piece.AddComponent<LineRenderer>();
                lr.sharedMaterial = acquiredMaterial;
                lr.useWorldSpace = false;
                lr.positionCount = 3; // One disconnected L-shape per corner.
                lr.alignment = LineAlignment.View;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 2;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                corners[i] = lr;
            }
        }

        private void LateUpdate()
        {
            if (!subscribed) Subscribe();
            if (centerEye == null) ResolveReferences();
            if (bracketRoot == null || centerEye == null || gaze == null) return;

            bool flash = flashTarget != null && Time.time < flashEndTime && flashTarget.gameObject.activeInHierarchy;
            if (!flash) flashTarget = null;

            BugTarget target = flash ? flashTarget :
                (gaze.isActiveAndEnabled ? gaze.CurrentTarget : null);

            if (target == null)
            {
                bracketRoot.gameObject.SetActive(false);
                return;
            }

            bracketRoot.gameObject.SetActive(true);
            Vector3 directionToEye = centerEye.position - target.AimPosition;
            if (directionToEye.sqrMagnitude < 0.001f)
                directionToEye = -centerEye.forward;
            directionToEye.Normalize();

            bracketRoot.position = target.AimPosition + directionToEye * 0.025f;
            bracketRoot.rotation = Quaternion.LookRotation(directionToEye, centerEye.up);

            float distance = Vector3.Distance(centerEye.position, target.AimPosition);
            // Minimum readable angular size, but allow larger physical bug bounds.
            float halfExtent = Mathf.Max(target.TargetRadius * paddingMultiplier,
                                         distance * 0.018f);
            float segment = halfExtent * cornerLengthFraction;

            visualScale = Mathf.MoveTowards(visualScale, 1f, Time.deltaTime * appearanceSpeed);
            float r = halfExtent * visualScale;
            float len = segment * visualScale;
            float width = Mathf.Clamp(distance * lineWidthAtOneMetre, 0.0018f, 0.007f);
            Material material = flash ? lockedMaterial : acquiredMaterial;

            for (int i = 0; i < 4; i++)
            {
                int xSign = (i == 0 || i == 2) ? -1 : 1;
                int ySign = (i < 2) ? 1 : -1;
                float x = xSign * r;
                float y = ySign * r;

                LineRenderer line = corners[i];
                line.sharedMaterial = material;
                line.widthMultiplier = width;
                line.SetPosition(0, new Vector3(x - xSign * len, y, 0f));
                line.SetPosition(1, new Vector3(x, y, 0f));
                line.SetPosition(2, new Vector3(x, y - ySign * len, 0f));
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            flashTarget = null;
            if (bracketRoot != null) bracketRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (acquiredMaterial != null) Destroy(acquiredMaterial);
            if (lockedMaterial != null) Destroy(lockedMaterial);
        }
    }
}
