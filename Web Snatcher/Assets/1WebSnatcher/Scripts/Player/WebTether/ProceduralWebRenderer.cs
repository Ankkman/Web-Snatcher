using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Three lightweight procedural web strands and one reusable attachment pulse.
    /// Render-only: does not own the target or interaction state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralWebRenderer : MonoBehaviour
    {
        [Header("Appearance")]
        [SerializeField] private Color coreColor = new Color(0.96f, 0.92f, 0.84f, 1f);
        [SerializeField] private Color filamentColor = new Color(0.77f, 0.68f, 0.59f, 1f);
        [SerializeField] private Color tensionColor = new Color(1f, 0.43f, 0.29f, 1f);
        [SerializeField, Range(0.001f, 0.012f)] private float coreWidth = 0.004f;
        [SerializeField, Range(0.0005f, 0.006f)] private float filamentWidth = 0.0016f;
        [SerializeField, Range(0f, 0.065f)] private float relaxedSag = 0.024f;
        [SerializeField, Range(0f, 0.025f)] private float filamentSpread = 0.008f;
        [SerializeField, Range(0f, 0.02f)] private float pulseRadius = 0.012f;

        private const int Points = 18;
        private const int PulsePoints = 25;
        private readonly Vector3[] path = new Vector3[Points];
        private readonly Vector3[] pulsePath = new Vector3[PulsePoints];
        private LineRenderer core;
        private LineRenderer left;
        private LineRenderer right;
        private LineRenderer pulse;
        private Material webMaterial;
        private Material pulseMaterial;
        private bool ready;

        private float pulseUntil;
        private float pulseStarted;
        private Vector3 pulsePosition;
        private Vector3 pulseFacing;

        private void Awake() { Initialize(); }

        private void Initialize()
        {
            if (ready) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError("[WebSnatcher] No unlit shader found for procedural web.", this);
                enabled = false;
                return;
            }

            webMaterial = new Material(shader) { name = "WS_ProceduralWeb" };
            pulseMaterial = new Material(shader) { name = "WS_ImpactPulse" };
            SetColor(webMaterial, Color.white);
            SetColor(pulseMaterial, Color.white);

            core = CreateLine("Web_Core", webMaterial, coreWidth, Points);
            left = CreateLine("Web_Filament_A", webMaterial, filamentWidth, Points);
            right = CreateLine("Web_Filament_B", webMaterial, filamentWidth, Points);
            pulse = CreateLine("Web_ImpactPulse", pulseMaterial, filamentWidth * 1.5f, PulsePoints);
            pulse.loop = false;
            ready = true;
            HideWeb();
            pulse.enabled = false;
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private LineRenderer CreateLine(string label, Material material, float width, int count)
        {
            GameObject go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.positionCount = count;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
            return line;
        }

        public void HideWeb()
        {
            if (core != null) core.enabled = false;
            if (left != null) left.enabled = false;
            if (right != null) right.enabled = false;
        }

        /// <param name="travel">0..1, visible growing tip during firing; 1 after attachment.</param>
        /// <param name="tension">0..1, straightens and warms the filaments.</param>
        public void Draw(Vector3 hand, Vector3 target, float travel, float tension, float elapsed)
        {
            Initialize();
            if (!ready) return;

            travel = Mathf.Clamp01(travel);
            tension = Mathf.Clamp01(tension);
            Vector3 tip = Vector3.Lerp(hand, target, travel);
            Vector3 axis = tip - hand;
            Vector3 side = Vector3.Cross(axis, Vector3.up);
            if (side.sqrMagnitude < 0.00001f) side = Vector3.Cross(axis, Vector3.right);
            side.Normalize();

            float length = axis.magnitude;
            float sag = relaxedSag * (1f - tension) * Mathf.Clamp01(length / 1.2f);
            float spread = filamentSpread * (0.65f + tension * 0.65f);
            float oscillation = Mathf.Sin(elapsed * 15f) * (1f - tension) * 0.0015f;

            core.enabled = left.enabled = right.enabled = travel > 0.001f;
            Color current = Color.Lerp(coreColor, tensionColor, tension * 0.8f);
            core.startColor = core.endColor = current;
            Color thin = Color.Lerp(filamentColor, tensionColor, tension * 0.60f);
            left.startColor = left.endColor = thin;
            right.startColor = right.endColor = thin;
            core.widthMultiplier = coreWidth * (1f + tension * 0.3f);

            for (int i = 0; i < Points; i++)
            {
                float t = i / (float)(Points - 1);
                float bulge = Mathf.Sin(Mathf.PI * t);
                Vector3 point = Vector3.Lerp(hand, tip, t) + Vector3.down * (sag * bulge);
                path[i] = point;
                core.SetPosition(i, point);
                Vector3 offset = side * ((spread + oscillation) * bulge);
                left.SetPosition(i, point + offset);
                right.SetPosition(i, point - offset);
            }
        }

        public void PlayImpact(Vector3 position, Vector3 towardViewer, float duration = 0.18f)
        {
            Initialize();
            if (!ready) return;
            pulsePosition = position;
            pulseFacing = towardViewer.sqrMagnitude > 0.0001f ? towardViewer.normalized : Vector3.forward;
            pulseStarted = Time.time;
            pulseUntil = pulseStarted + Mathf.Max(0.02f, duration);
            pulse.enabled = true;
        }

        private void LateUpdate()
        {
            if (!ready || pulse == null || !pulse.enabled) return;
            float duration = pulseUntil - pulseStarted;
            float t = Mathf.Clamp01((Time.time - pulseStarted) / duration);
            if (t >= 1f) { pulse.enabled = false; return; }

            float radius = pulseRadius + t * 0.065f;
            Quaternion face = Quaternion.LookRotation(pulseFacing, Vector3.up);
            pulse.startColor = pulse.endColor = new Color(1f, 0.67f, 0.47f, 1f - t);
            for (int i = 0; i < PulsePoints; i++)
            {
                float a = i / (float)(PulsePoints - 1) * Mathf.PI * 2f;
                pulsePath[i] = pulsePosition + face * new Vector3(
                    Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            }
            pulse.SetPositions(pulsePath);
        }

        private void OnDisable()
        {
            HideWeb();
            if (pulse != null) pulse.enabled = false;
        }

        private void OnDestroy()
        {
            if (webMaterial != null) Destroy(webMaterial);
            if (pulseMaterial != null) Destroy(pulseMaterial);
        }
    }
}
