using System;
using System.Collections.Generic;
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
        [Tooltip("Keep disabled for minimalist bracket-only targeting. Turn on to replace the entire bug material when targeted.")]
        [SerializeField] private bool useBodyHighlight = false;
        [SerializeField, Min(0.01f)] private float targetRadius = 0.12f;

        // The GazeTargeting system reads this registry every frame. It reflects
        // scene and MRUK-spawned/pool-enabled bugs without physics query delays.
        internal static readonly HashSet<BugTarget> ActiveTargets =
            new HashSet<BugTarget>();

        public event Action<BugTarget> CatchStarted;
        public event Action<BugTarget> CatchCompleted;
        public event Action<BugTarget> CatchCancelled;

        public CatchState State { get; private set; } = CatchState.Available;

        public bool CanTarget => isActiveAndEnabled &&
                                 State == CatchState.Available;

        public Vector3 AimPosition => aimPoint != null
            ? aimPoint.position
            : transform.position;

        public float TargetRadius => targetRadius;

        private Material[][] originalMaterials;
        private Material[][] highlightedMaterials;
        private bool isHighlighted;
        private static Material runtimeHighlightMaterial;
        public static event Action<BugTarget> AnyCatchCompleted;

        private void Awake()
        {
            if (highlightRenderers == null || highlightRenderers.Length == 0)
                highlightRenderers = GetComponentsInChildren<Renderer>(true);

            originalMaterials = new Material[highlightRenderers.Length][];
            highlightedMaterials = new Material[highlightRenderers.Length][];

            if (highlightMaterial == null)
                highlightMaterial = GetDefaultHighlightMaterial();

            for (int i = 0; i < highlightRenderers.Length; i++)
            {
                Renderer renderer = highlightRenderers[i];
                if (renderer == null) continue;

                Material[] originals = renderer.sharedMaterials;
                originalMaterials[i] = originals;

                if (originals == null || originals.Length == 0) continue;

                Material[] highlighted = new Material[originals.Length];
                for (int j = 0; j < highlighted.Length; j++)
                    highlighted[j] = highlightMaterial;
                highlightedMaterials[i] = highlighted;
            }

            int bugLayer = LayerMask.NameToLayer("Bug");
            if (bugLayer >= 0)
                SetLayerRecursively(transform, bugLayer);
        }

        private void OnEnable()
        {
            ActiveTargets.Add(this);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        private static Material GetDefaultHighlightMaterial()
        {
            if (runtimeHighlightMaterial != null)
                return runtimeHighlightMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError("[WebSnatcher] Missing highlight shader.");
                return null;
            }

            runtimeHighlightMaterial = new Material(shader);
            runtimeHighlightMaterial.name = "WebSnatcher_Runtime_AmberHighlight";
            Color amber = new Color(0.90f, 0.55f, 0.27f, 1f);
            if (runtimeHighlightMaterial.HasProperty("_BaseColor"))
                runtimeHighlightMaterial.SetColor("_BaseColor", amber);
            else if (runtimeHighlightMaterial.HasProperty("_Color"))
                runtimeHighlightMaterial.SetColor("_Color", amber);
            return runtimeHighlightMaterial;
        }

        public void SetHighlighted(bool enabled)
        {
            enabled = enabled && CanTarget;
            if (isHighlighted == enabled) return;
            isHighlighted = enabled;

            if (!useBodyHighlight) return;
            if (highlightRenderers == null) return;
            for (int i = 0; i < highlightRenderers.Length; i++)
            {
                Renderer renderer = highlightRenderers[i];
                if (renderer == null) continue;

                Material[] materials = enabled
                    ? highlightedMaterials[i]
                    : originalMaterials[i];

                if (materials != null && materials.Length > 0)
                    renderer.sharedMaterials = materials;
            }
        }

        public bool TryBeginCatch()
        {
            if (!CanTarget) return false;
            SetHighlighted(false);
            State = CatchState.Reserved;
            CatchStarted?.Invoke(this);
            return true;
        }

        public bool Yank()
        {
            if (State != CatchState.Reserved) return false;
            State = CatchState.InFlight;
            return true;
        }

        public bool CompleteCatch()
        {
            if (State != CatchState.InFlight) return false;
            State = CatchState.Caught;
            CatchCompleted?.Invoke(this);
            AnyCatchCompleted?.Invoke(this);
            return true;
        }

        public void CancelCatch()
        {
            if (State != CatchState.Reserved && State != CatchState.InFlight)
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
            ActiveTargets.Remove(this);
            SetHighlighted(false);
        }

        private void OnDestroy()
        {
            ActiveTargets.Remove(this);
            SetHighlighted(false);
        }
    }
}
