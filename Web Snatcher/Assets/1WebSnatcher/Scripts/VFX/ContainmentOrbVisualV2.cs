using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class ContainmentOrbVisualV2 : MonoBehaviour
    {
        [Header("Theme")]
        [SerializeField] private Color coreColor =
            new Color(0.08f, 0.05f, 0.03f, 1f);

        [SerializeField] private Color ringColor =
            new Color(1f, 0.58f, 0.18f, 1f);

        [SerializeField] private Color pulseColor =
            new Color(1f, 0.82f, 0.46f, 1f);

        [Header("Minimal Design")]
        [SerializeField, Range(0.45f, 1.2f)]
        private float outerRingRadius = 0.75f;

        [SerializeField, Range(0.3f, 1f)]
        private float innerRingRadius = 0.48f;

        [SerializeField, Range(0.003f, 0.025f)]
        private float lineWidth = 0.010f;

        [SerializeField, Range(16, 64)]
        private int segments = 36;

        [Header("Idle Motion")]
        [SerializeField, Range(0f, 80f)]
        private float outerRotationSpeed = 16f;

        [SerializeField, Range(0f, 80f)]
        private float innerRotationSpeed = 28f;

        [SerializeField, Range(0f, 0.08f)]
        private float corePulseAmount = 0.05f;

        [SerializeField, Range(0.2f, 4f)]
        private float corePulseSpeed = 1.6f;

        [Header("Capture Pulse")]
        [SerializeField, Range(0.1f, 1f)]
        private float capturePulseDuration = 0.28f;

        [SerializeField, Range(1f, 2.5f)]
        private float capturePulseScale = 1.7f;

        [SerializeField, Range(0.003f, 0.04f)]
        private float capturePulseWidth = 0.018f;

        private Transform outerRingRoot;
        private Transform innerRingRoot;

        private LineRenderer outerRing;
        private LineRenderer innerRing;
        private LineRenderer capturePulse;

        private Renderer coreRenderer;
        private Material coreMaterial;
        private Material ringMaterial;
        private Material pulseMaterial;

        private Vector3 baseCoreScale;
        private float pulseStart = -100f;
        private bool built;

        private void Awake()
        {
            Build();
        }

        private void Start()
        {
            if (coreRenderer != null && coreMaterial != null)
                coreRenderer.sharedMaterial = coreMaterial;
        }

        private void OnEnable()
        {
            if (!built)
                Build();
        }

        private void Build()
        {
            if (built)
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogError("[WS Orb] Missing unlit shader.", this);
                return;
            }

            coreMaterial = CreateMaterial(shader, "WS_Orb_Core_Minimal", coreColor);
            ringMaterial = CreateMaterial(shader, "WS_Orb_Rings_Minimal", ringColor);
            pulseMaterial = CreateMaterial(shader, "WS_Orb_Pulse_Minimal", pulseColor);

            coreRenderer = GetComponent<Renderer>();
            if (coreRenderer != null)
                coreRenderer.sharedMaterial = coreMaterial;

            baseCoreScale = transform.localScale;

            outerRingRoot = CreateGroup("OuterRingRoot");
            innerRingRoot = CreateGroup("InnerRingRoot");

            outerRing = CreateCircle(
                outerRingRoot,
                "OuterRing",
                outerRingRadius,
                ringMaterial,
                lineWidth,
                Quaternion.Euler(0f, 0f, 0f)
            );

            innerRing = CreateCircle(
                innerRingRoot,
                "InnerRing",
                innerRingRadius,
                ringMaterial,
                lineWidth * 0.85f,
                Quaternion.Euler(62f, 18f, 0f)
            );

            capturePulse = CreateCircle(
                transform,
                "CapturePulse",
                outerRingRadius,
                pulseMaterial,
                capturePulseWidth,
                Quaternion.identity
            );

            capturePulse.enabled = false;

            built = true;
        }

        private Transform CreateGroup(string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static Material CreateMaterial(
            Shader shader,
            string materialName,
            Color color)
        {
            Material mat = new Material(shader);
            mat.name = materialName;

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);

            return mat;
        }

        private LineRenderer CreateCircle(
            Transform parent,
            string name,
            float radius,
            Material material,
            float width,
            Quaternion localRotation)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = localRotation;
            go.transform.localScale = Vector3.one;

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.positionCount = segments + 1;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = true;

            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }

            return line;
        }

        public void PlayCapturePulse()
        {
            if (!built)
                Build();

            pulseStart = Time.time;

            if (capturePulse != null)
            {
                capturePulse.enabled = true;
                capturePulse.transform.localScale = Vector3.one * 0.2f;
            }
        }

        private void Update()
        {
            if (!built)
                return;

            float t = Time.time;

            if (outerRingRoot != null)
            {
                outerRingRoot.Rotate(
                    Vector3.forward,
                    outerRotationSpeed * Time.deltaTime,
                    Space.Self);
            }

            if (innerRingRoot != null)
            {
                innerRingRoot.Rotate(
                    Vector3.up,
                    -innerRotationSpeed * Time.deltaTime,
                    Space.Self);
            }

            if (coreRenderer != null)
            {
                float pulse = 1f + Mathf.Sin(t * corePulseSpeed * Mathf.PI * 2f) * corePulseAmount;
                transform.localScale = baseCoreScale * pulse;
            }

            UpdateCapturePulse();
        }

        private void UpdateCapturePulse()
        {
            if (capturePulse == null || !capturePulse.enabled)
                return;

            float t = Mathf.Clamp01((Time.time - pulseStart) / capturePulseDuration);

            if (t >= 1f)
            {
                capturePulse.enabled = false;
                return;
            }

            float scale = Mathf.Lerp(0.2f, capturePulseScale, t);
            capturePulse.transform.localScale = Vector3.one * scale;
            capturePulse.widthMultiplier = Mathf.Lerp(capturePulseWidth, 0.001f, t);
        }

        private void OnDestroy()
        {
            if (coreMaterial != null) Destroy(coreMaterial);
            if (ringMaterial != null) Destroy(ringMaterial);
            if (pulseMaterial != null) Destroy(pulseMaterial);
        }
    }
}