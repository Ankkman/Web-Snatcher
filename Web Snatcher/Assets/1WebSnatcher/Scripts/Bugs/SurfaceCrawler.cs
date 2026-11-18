using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace WebSnatcher
{
    /// <summary>Moves on a bounded MRUK wall plane or TABLE volume top. No rigidbody.
    /// +Y away from the supporting surface; +Z is the heading of the crawler root.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BugTarget))]
    public sealed class SurfaceCrawler : MonoBehaviour
    {
        public static readonly HashSet<SurfaceCrawler> ActiveCrawlers = new HashSet<SurfaceCrawler>();

        [Header("Movement (metres / second)")]
        [SerializeField, Min(0.01f)] private float minimumSpeed = 0.065f;
        [SerializeField, Min(0.01f)] private float maximumSpeed = 0.16f;
        [SerializeField, Range(20f, 360f)] private float steeringDegreesPerSecond = 105f;
        [SerializeField, Range(0f, 100f)] private float wanderingDegreesPerSecond = 32f;
        [SerializeField, Min(0.01f)] private float edgeMargin = 0.10f;
        [SerializeField, Min(0.001f)] private float surfaceClearance = 0.028f;
        [SerializeField, Min(1f)] private float rotationResponsiveness = 11f;
        [Header("Crowding")]
        [SerializeField, Min(0.10f)] private float avoidanceRadius = 0.33f;
        [SerializeField, Range(0f, 3f)] private float avoidanceStrength = 1.7f;
        [Header("Wall corners")]
        [SerializeField] private bool allowWallCornerTransitions = true;
        [SerializeField, Range(0.02f, 0.25f)] private float cornerProbeDistance = 0.13f;
        [Header("Debug")]
        [SerializeField] private bool drawSurfaceNormal;

        private MRUKAnchor anchor;
        private MRUKBugSpawner spawner;
        private BugTarget target;
        private Rect bounds2D;
        private Vector2 uv, heading;
        private bool tabletop, initialized;
        private float faceSign = 1f;
        private float speed, difficulty = 1f, noiseSeed, nextDecisionAt;
        private Vector2 desiredHeading;
        private float nextTransitionAllowedAt;

        public MRUKAnchor SurfaceAnchor => anchor;
        public bool IsTabletop => tabletop;
        public bool IsCrawling => initialized && target != null && target.CanTarget;
        public float CurrentSpeed => IsCrawling ? speed * difficulty : 0f;
        public float NormalOffset => surfaceClearance;
        public float FaceSign => faceSign;
        public Vector2 Coordinates => uv;
        public Vector3 WorldNormal => anchor == null ? transform.up :
            anchor.transform.TransformDirection(tabletop ? Vector3.up : Vector3.forward * faceSign).normalized;

        private void Awake() { target = GetComponent<BugTarget>(); }
        private void OnEnable() { ActiveCrawlers.Add(this); }
        private void OnDisable() { ActiveCrawlers.Remove(this); }
        private void OnDestroy() { ActiveCrawlers.Remove(this); }

        public void Initialize(MRUKAnchor surface, Rect area, bool isTable,
            Vector2 initialUV, Vector2 initialHeading, float frontSign = 1f,
            MRUKBugSpawner owner = null)
        {
            if (target == null) target = GetComponent<BugTarget>();
            anchor = surface; spawner = owner;
            bounds2D = area; tabletop = isTable;
            faceSign = Mathf.Sign(frontSign == 0f ? 1f : frontSign);
            uv = initialUV;
            heading = initialHeading.sqrMagnitude > .001f ? initialHeading.normalized : Vector2.up;
            desiredHeading = heading;
            speed = Random.Range(Mathf.Min(minimumSpeed, maximumSpeed), Mathf.Max(minimumSpeed, maximumSpeed));
            noiseSeed = Random.Range(0f, 1000f);
            nextDecisionAt = Time.time + Random.Range(.8f, 2.2f);
            initialized = anchor != null;
            if (initialized) ApplyPose(true);
        }

        /// <summary>Future difficulty manager can set 1 = normal, 2 = twice as fast.</summary>
        public void SetDifficultyMultiplier(float value) { difficulty = Mathf.Clamp(value, 0.6f, 2.5f); }

        private void Update()
        {
            if (!IsCrawling || anchor == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            if (Time.time >= nextDecisionAt)
            {
                float randomAngle = Random.Range(-65f, 65f) * Mathf.Lerp(1f, 1.3f, (difficulty - 1f) / 1.5f);
                desiredHeading = Rotate(heading, randomAngle);
                nextDecisionAt = Time.time + Random.Range(.9f, 2.4f) / Mathf.Sqrt(difficulty);
            }

            Vector2 avoidance = ComputeAvoidance();
            Vector2 aim = (desiredHeading + avoidance * avoidanceStrength).normalized;
            if (aim.sqrMagnitude < 0.01f) aim = desiredHeading;
            float noise = (Mathf.PerlinNoise(noiseSeed, Time.time * .8f) - .5f) * 2f;
            aim = Rotate(aim, noise * wanderingDegreesPerSecond * dt);
            heading = Vector2.MoveTowards(heading, aim, steeringDegreesPerSecond * Mathf.Deg2Rad * dt).normalized;
            if (heading.sqrMagnitude < .0001f) heading = Vector2.up;

            Vector2 proposed = uv + heading * (speed * difficulty * dt);
            float xmin = bounds2D.xMin + edgeMargin, xmax = bounds2D.xMax - edgeMargin;
            float ymin = bounds2D.yMin + edgeMargin, ymax = bounds2D.yMax - edgeMargin;
            if (xmax <= xmin || ymax <= ymin) return;

            bool outOfRect = proposed.x < xmin || proposed.x > xmax || proposed.y < ymin || proposed.y > ymax;
            bool outsidePolygon = !tabletop && !InsidePolygon(proposed);
            if (outOfRect || outsidePolygon)
            {
                // Only wall-to-wall corner transfer; never teleport between unrelated surfaces.
                if (!tabletop && allowWallCornerTransitions && spawner != null &&
                    Time.time > nextTransitionAllowedAt &&
                    spawner.TryTransitionWall(this, anchor, GetWorldPosition(),
                        GetWorldHeading(), cornerProbeDistance,
                        out MRUKAnchor nextAnchor, out Rect nextBounds,
                        out Vector2 nextUV, out Vector2 nextHeading, out float nextSign))
                {
                    anchor = nextAnchor; bounds2D = nextBounds;
                    uv = nextUV; heading = nextHeading.normalized;
                    desiredHeading = heading; faceSign = nextSign;
                    nextTransitionAllowedAt = Time.time + .75f;
                    ApplyPose(false);
                    return;
                }

                // Turn inward and remain inside measured boundary.
                Vector2 center = bounds2D.center;
                Vector2 inward = (center - uv).normalized;
                heading = (inward * .85f + Rotate(heading, 120f) * .15f).normalized;
                desiredHeading = heading;
                nextDecisionAt = Time.time + .6f;
                proposed = uv + heading * (speed * difficulty * dt);
                proposed.x = Mathf.Clamp(proposed.x, xmin, xmax);
                proposed.y = Mathf.Clamp(proposed.y, ymin, ymax);
                if (!InsidePolygon(proposed)) return;
            }

            uv = proposed;
            ApplyPose(false);
        }

        private bool InsidePolygon(Vector2 point)
        {
            return tabletop || anchor == null || anchor.PlaneBoundary2D == null ||
                anchor.PlaneBoundary2D.Count < 3 || anchor.IsPositionInBoundary(point);
        }

        private Vector2 ComputeAvoidance()
        {
            Vector2 sum = Vector2.zero;
            float sqr = avoidanceRadius * avoidanceRadius;
            foreach (SurfaceCrawler other in ActiveCrawlers)
            {
                if (other == null || other == this || !other.IsCrawling) continue;
                Vector3 deltaWorld = transform.position - other.transform.position;
                float distSqr = deltaWorld.sqrMagnitude;
                if (distSqr > sqr || distSqr < 0.000001f) continue;
                // Do not steer based on a bug behind an unrelated wall.
                if (Vector3.Dot(WorldNormal, other.WorldNormal) < .7f) continue;
                Vector3 local = anchor.transform.InverseTransformDirection(deltaWorld);
                Vector2 direction = tabletop ? new Vector2(local.x, local.z) : new Vector2(local.x, local.y);
                if (direction.sqrMagnitude > 0.00001f)
                    sum += direction.normalized * (1f - Mathf.Sqrt(distSqr) / avoidanceRadius);
            }
            return Vector2.ClampMagnitude(sum, 1.5f);
        }

        private Vector3 GetWorldPosition()
        {
            if (anchor == null) return transform.position;
            Vector3 local = tabletop
                ? new Vector3(uv.x, anchor.VolumeBounds.Value.max.y + surfaceClearance, uv.y)
                : new Vector3(uv.x, uv.y, surfaceClearance * faceSign);
            return anchor.transform.TransformPoint(local);
        }

        private Vector3 GetWorldHeading()
        {
            Vector3 local = tabletop ? new Vector3(heading.x, 0, heading.y) : new Vector3(heading.x, heading.y, 0);
            return anchor.transform.TransformDirection(local).normalized;
        }

        private void ApplyPose(bool snap)
        {
            Vector3 forward = Vector3.ProjectOnPlane(GetWorldHeading(), WorldNormal).normalized;
            if (forward.sqrMagnitude < .0001f) return;
            transform.position = GetWorldPosition();
            Quaternion wanted = Quaternion.LookRotation(forward, WorldNormal);
            transform.rotation = snap ? wanted : Quaternion.Slerp(transform.rotation, wanted,
                1f - Mathf.Exp(-rotationResponsiveness * Time.deltaTime));
            if (drawSurfaceNormal) Debug.DrawRay(transform.position, WorldNormal * .15f, Color.green);
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, s = Mathf.Sin(a), c = Mathf.Cos(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c).normalized;
        }
    }
}
