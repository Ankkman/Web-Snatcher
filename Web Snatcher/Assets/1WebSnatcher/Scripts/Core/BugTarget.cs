
using System;
using UnityEngine;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class BugTarget : MonoBehaviour
    {
        public enum CatchState
        {
            Available,
            Reserved,
            InFlight,
            Caught
        }

        [Header("Target Configuration")]
        [SerializeField] private Transform aimPoint;
        [SerializeField] private Renderer[] highlightRenderers;
        [SerializeField] private Material highlightMaterial;
        [SerializeField, Min(0.01f)] private float targetRadius = 0.12f;

        public event Action<BugTarget> CatchStarted;
        public event Action<BugTarget> CatchCompleted;
        public event Action<BugTarget> CatchCancelled;

        public CatchState State { get; private set; }
            = CatchState.Available;

        public bool CanTarget =>
            isActiveAndEnabled &&
            State == CatchState.Available;

        public Vector3 AimPosition =>
            aimPoint != null ? aimPoint.position : transform.position;

        public float TargetRadius => targetRadius;

        private Material[][] originalMaterials;
        private bool isHighlighted;

        // One runtime material shared by all bugs.
        private static Material runtimeHighlightMaterial;

        private void Awake()
        {
            if (highlightRenderers == null ||
                highlightRenderers.Length == 0)
            {
                highlightRenderers =
                    GetComponentsInChildren<Renderer>(true);
            }

            originalMaterials =
                new Material[highlightRenderers.Length][];

            for (int i = 0; i < highlightRenderers.Length; i++)
            {
                Renderer renderer = highlightRenderers[i];

                if (renderer != null)
                    originalMaterials[i] = renderer.sharedMaterials;
            }

            if (highlightMaterial == null)
                highlightMaterial = GetDefaultHighlightMaterial();

            // Automatically apply the layer to this bug
            // and its descendants.
            int bugLayer = LayerMask.NameToLayer("Bug");

            if (bugLayer >= 0)
                SetLayerRecursively(transform, bugLayer);
        }

        private static void SetLayerRecursively(
            Transform root, int layer)
        {
            root.gameObject.layer = layer;

            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        private static Material GetDefaultHighlightMaterial()
        {
            if (runtimeHighlightMaterial != null)
                return runtimeHighlightMaterial;

            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            runtimeHighlightMaterial = new Material(shader);
            runtimeHighlightMaterial.name =
                "WebSnatcher_Runtime_CyanHighlight";

            Color cyan = new Color(0f, 0.95f, 1f, 1f);

            if (runtimeHighlightMaterial.HasProperty("_BaseColor"))
                runtimeHighlightMaterial.SetColor("_BaseColor", cyan);
            else if (runtimeHighlightMaterial.HasProperty("_Color"))
                runtimeHighlightMaterial.SetColor("_Color", cyan);

            return runtimeHighlightMaterial;
        }

        public void SetHighlighted(bool enabled)
        {
            enabled = enabled && CanTarget;

            if (isHighlighted == enabled)
                return;

            isHighlighted = enabled;

            for (int i = 0; i < highlightRenderers.Length; i++)
            {
                Renderer renderer = highlightRenderers[i];
                Material[] originals = originalMaterials[i];

                if (renderer == null || originals == null ||
                    originals.Length == 0)
                    continue;

                if (!enabled)
                {
                    renderer.sharedMaterials = originals;
                    continue;
                }

                Material[] highlighted =
                    new Material[originals.Length];

                for (int j = 0; j < highlighted.Length; j++)
                    highlighted[j] = highlightMaterial;

                renderer.sharedMaterials = highlighted;
            }
        }

        public bool TryBeginCatch()
        {
            if (!CanTarget)
                return false;

            SetHighlighted(false);
            State = CatchState.Reserved;

            CatchStarted?.Invoke(this);
            return true;
        }

        public bool Yank()
        {
            if (State != CatchState.Reserved)
                return false;

            State = CatchState.InFlight;
            return true;
        }

        public bool CompleteCatch()
        {
            if (State != CatchState.InFlight)
                return false;

            State = CatchState.Caught;
            CatchCompleted?.Invoke(this);
            return true;
        }

        public void CancelCatch()
        {
            if (State != CatchState.Reserved &&
                State != CatchState.InFlight)
                return;

            State = CatchState.Available;
            CatchCancelled?.Invoke(this);
        }

        public void ResetTarget()
        {
            SetHighlighted(false);
            State = CatchState.Available;
        }

        private void OnDisable()
        {
            SetHighlighted(false);
        }

        private void OnDestroy()
        {
            SetHighlighted(false);
        }
    }
}
