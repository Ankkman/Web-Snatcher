using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Small reusable mesh web splat, independent of MRUK and bug meshes.
    /// One mesh renderer; no colliders, particles, allocations during UpdatePose.
    /// Call Show on tether attachment, UpdatePose during attached/flying, Hide on cancel/catch.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WebAttachmentVisual : MonoBehaviour
    {
        [Header("Web Splat")]
        [SerializeField, Range(5, 12)] private int spokes = 8;
        [SerializeField, Range(2, 5)] private int rings = 3;
        [SerializeField, Range(0.0005f, 0.005f)] private float threadWidth = 0.0015f;
        [SerializeField, Range(0.5f, 2.0f)] private float radiusMultiplier = 1.15f;
        [SerializeField, Min(0.002f)] private float surfaceOffset = 0.01f;
        [SerializeField, Range(0.03f, 0.35f)] private float unfoldSeconds = 0.14f;
        [SerializeField] private Color webColor = new Color(0.94f, 0.89f, 0.80f, 1f);

        private MeshFilter filter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private Material material;
        private bool showing;
        private float revealStart;
        private float radius = 0.12f;
        private Vector3 center;
        private Vector3 normal = Vector3.forward;
        private readonly List<Vector3> vertices = new List<Vector3>(400);
        private readonly List<int> triangles = new List<int>(600);
        private readonly List<Vector2> uv = new List<Vector2>(400);
        private bool built;

        private void Awake()
        {
            EnsureBuilt();
            Hide();
        }

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;
            mesh = new Mesh { name = "WS_WebAttachment_Splat" };
            mesh.MarkDynamic();
            filter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError("[WebSnatcher] WebAttachmentVisual: URP Unlit shader not found.", this);
                enabled = false;
                return;
            }
            material = new Material(shader) { name = "WS_WebSplat_Runtime" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", webColor);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", webColor);
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            BuildMesh();
        }

        // A web with straight radial spokes and scalloped ring links, as in the user's image.
        // Every thread is a thin quad, all combined into one mesh and material.
        private void BuildMesh()
        {
            vertices.Clear(); triangles.Clear(); uv.Clear();
            float step = Mathf.PI * 2f / spokes;
            for (int s = 0; s < spokes; s++)
            {
                float a = s * step;
                Vector2 direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                AddThread(Vector2.zero, direction, threadWidth / 0.12f);
            }
            for (int r = 1; r <= rings; r++)
            {
                float radiusAtRing = r / (float)rings;
                for (int s = 0; s < spokes; s++)
                {
                    float a = s * step;
                    float b = (s + 1) * step;
                    Vector2 start = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radiusAtRing;
                    Vector2 end = new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radiusAtRing;
                    // Slightly scalloped web geometry, 3 segments per radial gap.
                    Vector2 previous = start;
                    for (int j = 1; j <= 3; j++)
                    {
                        float t = j / 3f;
                        float angle = Mathf.Lerp(a, b, t);
                        float inward = 1f - 0.11f * Mathf.Sin(t * Mathf.PI);
                        Vector2 next = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radiusAtRing * inward);
                        AddThread(previous, next, threadWidth * 0.75f / 0.12f);
                        previous = next;
                    }
                }
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void AddThread(Vector2 from, Vector2 to, float width)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < 0.00001f) return;
            Vector2 side = new Vector2(-delta.y, delta.x).normalized * width * 0.5f;
            int i = vertices.Count;
            vertices.Add(new Vector3(from.x + side.x, from.y + side.y, 0f));
            vertices.Add(new Vector3(from.x - side.x, from.y - side.y, 0f));
            vertices.Add(new Vector3(to.x + side.x, to.y + side.y, 0f));
            vertices.Add(new Vector3(to.x - side.x, to.y - side.y, 0f));
            uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.right); uv.Add(Vector2.one);
            triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 1);
            triangles.Add(i + 2); triangles.Add(i + 3); triangles.Add(i + 1);
            // Two-sided geometry for Quest camera/view-angle changes.
            triangles.Add(i + 1); triangles.Add(i + 2); triangles.Add(i);
            triangles.Add(i + 1); triangles.Add(i + 3); triangles.Add(i + 2);
        }

        public void Show(Vector3 position, Vector3 towardViewer, float targetRadius)
        {
            EnsureBuilt();
            if (meshRenderer == null) return;
            radius = Mathf.Max(0.025f, targetRadius) * radiusMultiplier;
            showing = true;
            revealStart = Time.time;
            meshRenderer.enabled = true;
            UpdatePose(position, towardViewer);
        }

        public void UpdatePose(Vector3 position, Vector3 towardViewer)
        {
            if (!showing) return;
            center = position;
            if (towardViewer.sqrMagnitude > 0.0001f)
                normal = towardViewer.normalized;
            transform.position = center + normal * surfaceOffset;
            transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
            float t = Mathf.Clamp01((Time.time - revealStart) / Mathf.Max(0.01f, unfoldSeconds));
            float eased = 1f - (1f - t) * (1f - t);
            transform.localScale = Vector3.one * (radius * eased);
        }

        public void Hide()
        {
            showing = false;
            if (meshRenderer != null) meshRenderer.enabled = false;
        }

        private void OnDisable() { Hide(); }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
