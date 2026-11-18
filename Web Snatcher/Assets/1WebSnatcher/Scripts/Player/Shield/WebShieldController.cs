using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>Reusable left-hand shield. Independent of boss/game phases.
    /// H in Editor or a configured left open-hand ISDK shape on device.
    /// No collision objects; boss/projectile systems call TryBlockSegment.</summary>
    [DisallowMultipleComponent]
    public sealed class WebShieldController : MonoBehaviour
    {
        [Header("References (auto-detect if empty)")]
        [SerializeField] private LeftPalmShieldInput input;
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform centerEye;

        [Header("Shield")]
        [SerializeField, Range(0.2f, 0.7f)] private float shieldRadius = 0.42f;
        [SerializeField, Range(0.05f, 0.5f)] private float forwardOffset = 0.20f;
        [SerializeField] private Color webColor = new Color(0.93f, 0.88f, 0.78f, 1f);
        [SerializeField] private bool showDebugLogs = true;

        [Header("Quest activation (not required for Editor H)")]
        [Tooltip("Prevents the shield appearing when a tracked hand is idle/open at the player's side.")]
        [SerializeField] private bool requireRaisedHandOnQuest = true;
        [SerializeField, Range(-0.6f, 0.2f)] private float minHandHeightBelowEyes = -0.48f;
        [SerializeField, Range(-0.1f, 0.6f)] private float minHandForwardOfEyes = 0.16f;

        private const int Segments = 32;
        private readonly LineRenderer[] rings = new LineRenderer[3];
        private readonly LineRenderer[] spokes = new LineRenderer[8];
        private Material sharedMaterial;
        private bool lastActive;

        public bool IsShieldActive { get; private set; }
        public Vector3 ShieldCenter { get; private set; }
        public float ShieldRadius => shieldRadius;

        private void Awake()
        {
            ResolveReferences();
            BuildShield();
            SetVisible(false);
        }

        private void ResolveReferences()
        {
            if (input == null) input = FindFirstObjectByType<LeftPalmShieldInput>();
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (leftHandAnchor == null) leftHandAnchor = rig.leftHandAnchor;
                if (centerEye == null) centerEye = rig.centerEyeAnchor;
            }
            if (centerEye == null && Camera.main != null) centerEye = Camera.main.transform;
        }

        private LineRenderer MakeLine(string name, int points, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = sharedMaterial;
            lr.widthMultiplier = width;
            lr.positionCount = points;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        private void BuildShield()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) { Debug.LogError("[WS Shield] Missing unlit shader", this); return; }
            sharedMaterial = new Material(shader) { name = "WS_Shield_Runtime_Silk" };
            if (sharedMaterial.HasProperty("_BaseColor")) sharedMaterial.SetColor("_BaseColor", webColor);
            else if (sharedMaterial.HasProperty("_Color")) sharedMaterial.SetColor("_Color", webColor);
            for (int i = 0; i < rings.Length; i++) rings[i] = MakeLine("Shield_Ring_" + i, Segments + 1, 0.003f);
            for (int i = 0; i < spokes.Length; i++) spokes[i] = MakeLine("Shield_Spoke_" + i, 2, 0.0018f);
        }

        private void Update()
        {
            if (input == null || leftHandAnchor == null || centerEye == null)
                ResolveReferences();
            if (input == null || leftHandAnchor == null || centerEye == null)
            {
                IsShieldActive = false;
                SetVisible(false);
                return;
            }

            ShieldCenter = leftHandAnchor.position + centerEye.forward * forwardOffset;
            bool active = input.IsOpen;
#if !UNITY_EDITOR
            if (requireRaisedHandOnQuest)
            {
                Vector3 relative = leftHandAnchor.position - centerEye.position;
                bool raised = relative.y >= minHandHeightBelowEyes &&
                              Vector3.Dot(relative, centerEye.forward) >= minHandForwardOfEyes;
                active &= raised;
            }
#endif
            IsShieldActive = active;
            if (active != lastActive)
            {
                if (showDebugLogs)
                    Debug.Log(active ? "[WS Shield] DEPLOYED" : "[WS Shield] LOWERED", this);
                lastActive = active;
            }
            SetVisible(active);
            if (active) DrawShield();
        }

        private void SetVisible(bool show)
        {
            foreach (var l in rings) if (l != null) l.enabled = show;
            foreach (var l in spokes) if (l != null) l.enabled = show;
        }

        private void DrawShield()
        {
            // Facing the headset so a shield is readable in MR.
            Vector3 normal = centerEye.position - ShieldCenter;
            if (normal.sqrMagnitude < .001f) normal = -centerEye.forward;
            Quaternion rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);
            for (int ring = 0; ring < rings.Length; ring++)
            {
                float radius = shieldRadius * (ring + 1f) / rings.Length;
                for (int i = 0; i <= Segments; i++)
                {
                    float a = 2f * Mathf.PI * i / Segments;
                    rings[ring].SetPosition(i, ShieldCenter + rotation *
                        new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
                }
            }
            for (int i = 0; i < spokes.Length; i++)
            {
                float a = 2f * Mathf.PI * i / spokes.Length;
                spokes[i].SetPosition(0, ShieldCenter);
                spokes[i].SetPosition(1, ShieldCenter + rotation *
                    new Vector3(Mathf.Cos(a) * shieldRadius, Mathf.Sin(a) * shieldRadius, 0f));
            }
        }

        public bool TryBlockSegment(Vector3 from, Vector3 to, float projectileRadius)
        {
            if (!IsShieldActive || centerEye == null) return false;
            Vector3 segment = to - from;
            float sq = segment.sqrMagnitude;
            if (sq < 0.000001f) return false;
            float t = Mathf.Clamp01(Vector3.Dot(ShieldCenter - from, segment) / sq);
            Vector3 closest = from + segment * t;
            float allowed = shieldRadius + Mathf.Max(0f, projectileRadius);
            // Only block when the incoming projectile actually crosses the shield region.
            return (closest - ShieldCenter).sqrMagnitude <= allowed * allowed;
        }

        private void OnDisable()
        {
            IsShieldActive = false;
            lastActive = false;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (sharedMaterial != null) Destroy(sharedMaterial);
        }
    }
}
