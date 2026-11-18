using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace WebSnatcher
{
    /// <summary>Room-source independent population manager: prefab or scanned MRUK room.
    /// Distributes enemies by best-of-N positions; avoids overcrowding and repeated clusters.</summary>
    [DisallowMultipleComponent]
    public sealed class MRUKBugSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BugTarget bugPrefab;
        [SerializeField] private Transform playerHead;
        [SerializeField] private Transform spawnedParent;

        [Header("Population")]
        [SerializeField, Range(1, 12)] private int initialPopulation = 4;
        [SerializeField, Range(1, 12)] private int maximumPopulation = 6;
        [SerializeField, Min(.1f)] private float respawnDelay = 1.1f;
        [SerializeField, Range(10, 160)] private int attemptsPerSpawn = 90;

        [Header("Comfort and spacing")]
        [SerializeField, Min(.2f)] private float minimumDistance = .9f;
        [SerializeField, Min(1f)] private float maximumDistance = 4.5f;
        [SerializeField, Range(20f, 170f)] private float horizontalViewDegrees = 115f;
        [SerializeField, Range(20f, 145f)] private float verticalViewDegrees = 90f;
        [SerializeField, Min(.1f)] private float minimumSpacing = .50f;
        [SerializeField, Min(.05f)] private float planeEdgePadding = .16f;
        [SerializeField, Range(0, 1)] private float wallProbability = .7f;
        [SerializeField] private bool allowTabletops = true;
        [SerializeField] private bool distributeAcrossSurfaces = true;
        [Header("Debug")]
        [SerializeField] private bool logFailures;

        private struct Surface
        {
            public MRUKAnchor Anchor;
            public Rect Area;
            public bool Tabletop;
        }
        private readonly List<Surface> surfaces = new List<Surface>(24);
        private readonly List<SurfaceCrawler> crawlers = new List<SurfaceCrawler>(12);
        private MRUKRoom currentRoom;
        private float nextSpawnAt;
        private int nextId;

        private void Awake()
        {
            if (playerHead == null)
            {
                OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig != null) playerHead = rig.centerEyeAnchor;
                if (playerHead == null && Camera.main != null) playerHead = Camera.main.transform;
            }
            if (spawnedParent == null) spawnedParent = transform;
        }

        private void Start() { RefreshRoom(); }

        private void Update()
        {
            if (Time.time < nextSpawnAt) return;
            nextSpawnAt = Time.time + (currentRoom == null ? .5f : respawnDelay);
            if (MRUK.Instance == null) return;
            MRUKRoom updated = MRUK.Instance.GetCurrentRoom();
            if (updated != currentRoom) RefreshRoom();
            if (currentRoom == null) return;
            Prune();
            if (crawlers.Count < Mathf.Min(initialPopulation, maximumPopulation)) SpawnOne();
        }

        public void SetPopulation(int count) { initialPopulation = Mathf.Clamp(count, 1, maximumPopulation); }
        public void SetDifficultyMultiplier(float multiplier)
        {
            foreach (SurfaceCrawler c in crawlers) if (c != null) c.SetDifficultyMultiplier(multiplier);
            difficultyMultiplier = Mathf.Clamp(multiplier, .6f, 2.5f);
        }
        private float difficultyMultiplier = 1f;

        public void RefreshRoom()
        {
            MRUKRoom found = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (found == null) { currentRoom = null; surfaces.Clear(); return; }
            if (found == currentRoom && surfaces.Count > 0) return;
            currentRoom = found;
            surfaces.Clear();
            foreach (MRUKAnchor a in currentRoom.Anchors)
            {
                if (a == null) continue;
                if (a.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE) && a.PlaneRect.HasValue)
                {
                    Rect r = a.PlaneRect.Value;
                    if (r.width > 2 * planeEdgePadding + .2f && r.height > 2 * planeEdgePadding + .2f)
                        surfaces.Add(new Surface { Anchor = a, Area = r, Tabletop = false });
                }
                else if (allowTabletops && a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) && a.VolumeBounds.HasValue)
                {
                    Bounds b = a.VolumeBounds.Value;
                    if (b.size.x > 2 * planeEdgePadding && b.size.z > 2 * planeEdgePadding)
                        surfaces.Add(new Surface { Anchor = a,
                            Area = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z), Tabletop = true });
                }
            }
            Debug.Log($"[WS Spawner V13] MRUK room loaded, {surfaces.Count} usable surfaces", this);
        }

        public bool SpawnOne()
        {
            if (currentRoom == null || bugPrefab == null || playerHead == null) return false;
            Prune();
            if (crawlers.Count >= maximumPopulation || surfaces.Count == 0) return false;
            float bestScore = float.NegativeInfinity;
            Surface bestSurface = default;
            Vector2 bestUV = default;
            Vector3 bestWorld = default;
            float bestSign = 1f;
            bool valid = false;

            for (int i = 0; i < attemptsPerSpawn; i++)
            {
                int index = Random.Range(0, surfaces.Count);
                Surface surface = surfaces[index];
                // Soft preference: prefer walls/tabletops with the requested probability.
                if (surfaces.Count > 1 && Random.value < .7f && surface.Tabletop == (Random.value < wallProbability)) continue;
                Rect r = surface.Area;
                float pad = planeEdgePadding;
                if (r.width < pad * 2 || r.height < pad * 2) continue;
                Vector2 p = new Vector2(Random.Range(r.xMin + pad, r.xMax - pad),
                    Random.Range(r.yMin + pad, r.yMax - pad));
                MRUKAnchor a = surface.Anchor;
                if (!surface.Tabletop && a.PlaneBoundary2D != null && a.PlaneBoundary2D.Count >= 3 && !a.IsPositionInBoundary(p)) continue;
                float sign = surface.Tabletop ? 1f :
                    (Vector3.Dot(a.transform.forward, playerHead.position - a.transform.position) >= 0f ? 1f : -1f);
                Vector3 local = surface.Tabletop
                    ? new Vector3(p.x, a.VolumeBounds.Value.max.y + .028f, p.y)
                    : new Vector3(p.x, p.y, .028f * sign);
                Vector3 pos = a.transform.TransformPoint(local);
                if (!Comfortable(pos)) continue;
                float nearest = NearestActiveDistance(pos);
                if (nearest < minimumSpacing) continue;

                // Prefer underused anchors, then maximize distance from nearby bugs.
                int onThisAnchor = 0;
                foreach (SurfaceCrawler c in crawlers)
                    if (c != null && c.gameObject.activeInHierarchy && c.SurfaceAnchor == a) onThisAnchor++;
                float score = Mathf.Min(nearest, 3f) - (distributeAcrossSurfaces ? onThisAnchor * .65f : 0f)
                    + Random.Range(0f, .12f);
                if (!valid || score > bestScore)
                {
                    valid = true; bestScore = score; bestSurface = surface;
                    bestUV = p; bestWorld = pos; bestSign = sign;
                }
            }
            if (!valid)
            {
                if (logFailures) Debug.LogWarning("[WS Spawner V13] No comfortable, well-spaced location found", this);
                return false;
            }

            BugTarget bug = Instantiate(bugPrefab, bestWorld, Quaternion.identity, spawnedParent);
            bug.name = "WS_Scout_" + (++nextId);
            bug.ResetTarget();
            SurfaceCrawler crawler = bug.GetComponent<SurfaceCrawler>();
            if (crawler == null) crawler = bug.gameObject.AddComponent<SurfaceCrawler>();
            Vector2 heading = Random.insideUnitCircle.normalized;
            crawler.Initialize(bestSurface.Anchor, bestSurface.Area, bestSurface.Tabletop,
                bestUV, heading, bestSign, this);
            crawler.SetDifficultyMultiplier(difficultyMultiplier);
            crawlers.Add(crawler);
            return true;
        }

        // Called only near a failed wall boundary step. Adjacent walls must share a physical corner.
        public bool TryTransitionWall(SurfaceCrawler crawler, MRUKAnchor oldAnchor,
            Vector3 position, Vector3 movement, float probeDistance,
            out MRUKAnchor newAnchor, out Rect newBounds,
            out Vector2 newUV, out Vector2 newHeading, out float newSign)
        {
            newAnchor = null; newBounds = default; newUV = default;
            newHeading = default; newSign = 1f;
            float best = float.MaxValue;
            Vector3 incomingNormal = crawler.WorldNormal;
            foreach (Surface s in surfaces)
            {
                if (s.Tabletop || s.Anchor == oldAnchor || s.Anchor == null) continue;
                MRUKAnchor a = s.Anchor;
                Vector3 local = a.transform.InverseTransformPoint(position);
                // Reject unrelated walls, even when their rectangular UV extents overlap.
                if (Mathf.Abs(local.z) > probeDistance) continue;
                if (local.x < s.Area.xMin + .07f || local.x > s.Area.xMax - .07f ||
                    local.y < s.Area.yMin + .07f || local.y > s.Area.yMax - .07f) continue;
                float sign = Vector3.Dot(a.transform.forward, playerHead.position - a.transform.position) >= 0 ? 1f : -1f;
                Vector3 normal = a.transform.forward * sign;
                float normalsAngle = Vector3.Angle(incomingNormal, normal);
                if (normalsAngle < 35f || normalsAngle > 145f) continue;
                // Project current forward motion onto the new surface; reorient as it wraps corner.
                Vector3 fwd = Vector3.ProjectOnPlane(movement, normal);
                if (fwd.sqrMagnitude < .001f) continue;
                Vector3 localFwd = a.transform.InverseTransformDirection(fwd.normalized);
                Vector2 candidateHeading = new Vector2(localFwd.x, localFwd.y).normalized;
                // Avoid entering a surface while aiming directly back toward its edge.
                Vector2 p = new Vector2(local.x, local.y);
                Vector2 step = p + candidateHeading * .035f;
                if (!s.Area.Contains(step)) continue;
                if (a.PlaneBoundary2D != null && a.PlaneBoundary2D.Count >= 3 && !a.IsPositionInBoundary(step)) continue;
                float quality = Mathf.Abs(local.z);
                if (quality < best)
                {
                    best = quality;
                    newAnchor = a; newBounds = s.Area; newUV = step;
                    newHeading = candidateHeading; newSign = sign;
                }
            }
            return newAnchor != null;
        }

        private float NearestActiveDistance(Vector3 pos)
        {
            float nearest = float.PositiveInfinity;
            foreach (SurfaceCrawler c in crawlers)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(c.transform.position, pos);
                if (d < nearest) nearest = d;
            }
            return nearest;
        }

        private bool Comfortable(Vector3 pos)
        {
            Vector3 local = playerHead.InverseTransformPoint(pos);
            float distance = local.magnitude;
            if (local.z <= .15f || distance < minimumDistance || distance > maximumDistance) return false;
            float yaw = Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg);
            float pitch = Mathf.Abs(Mathf.Atan2(local.y, Mathf.Sqrt(local.x * local.x + local.z * local.z)) * Mathf.Rad2Deg);
            return yaw <= horizontalViewDegrees * .5f && pitch <= verticalViewDegrees * .5f;
        }

        private void Prune()
        {
            for (int i = crawlers.Count - 1; i >= 0; i--)
                if (crawlers[i] == null || !crawlers[i].gameObject.activeInHierarchy)
                    crawlers.RemoveAt(i);
        }
    }
}
