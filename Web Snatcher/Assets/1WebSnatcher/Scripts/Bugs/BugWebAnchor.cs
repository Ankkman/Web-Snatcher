
using UnityEngine;

namespace WebSnatcher
{
    /// <summary>
    /// Provides an exact body attachment point for Web Snatch.
    /// Does not alter gaze targeting, crawling, or catching.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BugTarget))]
    public sealed class BugWebAnchor : MonoBehaviour
    {
        [Header("Attachment")]
        [SerializeField]
        private Transform attachmentPoint;

        [SerializeField]
        private bool autoFindByName = true;

        [Header("Debug")]
        [SerializeField]
        private bool drawGizmo = true;

        [SerializeField, Min(0.001f)]
        private float gizmoRadius = 0.015f;

        private BugTarget target;

        public Transform AttachmentPoint => attachmentPoint;

        public Vector3 Position
        {
            get
            {
                if (attachmentPoint != null)
                    return attachmentPoint.position;

                if (target == null)
                    target = GetComponent<BugTarget>();

                return target != null
                    ? target.AimPosition
                    : transform.position;
            }
        }

        private void Awake()
        {
            target = GetComponent<BugTarget>();

            if (attachmentPoint == null && autoFindByName)
                FindAttachmentPoint();
        }

        private void OnValidate()
        {
            if (attachmentPoint == null && autoFindByName)
                FindAttachmentPoint();
        }

        [ContextMenu("Find WebAttachPoint")]
        private void FindAttachmentPoint()
        {
            Transform[] children =
                GetComponentsInChildren<Transform>(true);

            foreach (Transform child in children)
            {
                if (child == transform)
                    continue;

                if (child.name == "WebAttachPoint")
                {
                    attachmentPoint = child;
                    return;
                }
            }
        }

        public Vector3 GetFacingNormal(Vector3 origin)
        {
            Vector3 direction = origin - Position;

            if (direction.sqrMagnitude < 0.000001f)
                return transform.up;

            return direction.normalized;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmo)
                return;

            Gizmos.color = new Color(1f, 0.55f, 0.2f);

            Vector3 position = Position;

            Gizmos.DrawWireSphere(
                position,
                gizmoRadius
            );

            Gizmos.DrawLine(
                position,
                position + transform.up * 0.04f
            );
        }
    }
}
