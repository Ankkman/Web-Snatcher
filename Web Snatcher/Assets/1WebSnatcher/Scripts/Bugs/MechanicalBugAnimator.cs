using UnityEngine;

namespace WebSnatcher
{
    /// <summary>Low-cost procedural animation for a single grouped legs mesh.
    /// Does not alter collider, MRUK root, gaze aim point, or web capture trajectory.
    /// Animates VisualRoot only. Optional legs transform can be assigned manually.</summary>
    [DisallowMultipleComponent]
    public sealed class MechanicalBugAnimator : MonoBehaviour
    {
        [Header("Model references")]
        [Tooltip("Assign the VisualRoot child. Automatically searched by name if empty.")]
        [SerializeField] private Transform visualRoot;
        [Tooltip("Optional combined legs mesh Object_3. Never animate the crawler root.")]
        [SerializeField] private Transform groupedLegs;
        [SerializeField] private bool autoFindGroupedLegs = true;

        [Header("Crawl feel")]
        [SerializeField, Range(1f, 18f)] private float footstepsPerSecond = 8f;
        [SerializeField, Range(0f, .008f)] private float bodyBobMetres = .0015f;
        [SerializeField, Range(0f, 5f)] private float bodyRollDegrees = 1.2f;
        [SerializeField, Range(0f, 4f)] private float groupedLegRockDegrees = 1.4f;
        [SerializeField, Range(0f, .004f)] private float groupedLegLiftMetres = .0009f;
        [SerializeField, Min(.1f)] private float blendSpeed = 8f;
        [Header("Debug")]
        [SerializeField] private bool animateWhileStationary = false;

        private SurfaceCrawler crawler;
        private Vector3 visualPosition, legPosition;
        private Quaternion visualRotation, legRotation;
        private bool captured;
        private float phase, blend;

        private void Awake()
        {
            crawler = GetComponent<SurfaceCrawler>();
            if (visualRoot == null) visualRoot = FindChild(transform, "VisualRoot");
            if (visualRoot == null)
            {
                Debug.LogWarning("[WS Animator] VisualRoot not found; animation disabled to protect physics root", this);
                enabled = false; return;
            }
            if (groupedLegs == null && autoFindGroupedLegs) groupedLegs = FindChild(visualRoot, "Object_3");
            visualPosition = visualRoot.localPosition;
            visualRotation = visualRoot.localRotation;
            if (groupedLegs != null)
            {
                legPosition = groupedLegs.localPosition;
                legRotation = groupedLegs.localRotation;
            }
            captured = true;
            phase = Random.value * Mathf.PI * 2f;
        }

        private void LateUpdate()
        {
            if (!captured || visualRoot == null) return;
            bool walking = crawler != null && crawler.IsCrawling;
            float desired = (walking || animateWhileStationary) ? 1f : 0f;
            blend = Mathf.MoveTowards(blend, desired, Time.deltaTime * blendSpeed);
            float speed = Mathf.Max(.25f, crawler != null ? crawler.CurrentSpeed / .12f : 1f);
            if (blend > .001f) phase += Time.deltaTime * footstepsPerSecond * Mathf.Clamp(speed, .5f, 2.4f) * Mathf.PI * 2f;
            float cycle = Mathf.Sin(phase), halfCycle = Mathf.Sin(phase * 2f);
            visualRoot.localPosition = visualPosition + Vector3.up * (Mathf.Abs(cycle) * bodyBobMetres * blend);
            visualRoot.localRotation = visualRotation * Quaternion.Euler(0f, 0f, halfCycle * bodyRollDegrees * blend);
            if (groupedLegs != null)
            {
                // Since six legs share ONE mesh, keep rocking extremely small.
                // A true tripod gait requires independent leg pivots in Blender.
                groupedLegs.localPosition = legPosition + Vector3.up * (Mathf.Abs(halfCycle) * groupedLegLiftMetres * blend);
                groupedLegs.localRotation = legRotation * Quaternion.Euler(cycle * groupedLegRockDegrees * blend, 0f, 0f);
            }
        }

        private static Transform FindChild(Transform t, string exactName)
        {
            if (t.name == exactName) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform found = FindChild(t.GetChild(i), exactName);
                if (found != null) return found;
            }
            return null;
        }

        private void OnDisable()
        {
            if (!captured) return;
            if (visualRoot != null)
            {
                visualRoot.localPosition = visualPosition;
                visualRoot.localRotation = visualRotation;
            }
            if (groupedLegs != null)
            {
                groupedLegs.localPosition = legPosition;
                groupedLegs.localRotation = legRotation;
            }
        }
    }
}
