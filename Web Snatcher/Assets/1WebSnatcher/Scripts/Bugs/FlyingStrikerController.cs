using UnityEngine;
using Meta.XR.MRUtilityKit;

namespace WebSnatcher
{
    /// <summary>
    /// Room-aware, seated-friendly flying enemy motor. No NavMesh or physics body required.
    /// Compatible with MRUK prefab and device room sources.
    /// Controls ONLY its gameplay root; imported flight animation stays on VisualRoot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlyingStrikerController : MonoBehaviour
    {
        private enum FlightMode { Seeking, Hovering }

        [Header("References (auto-resolved)")]
        [SerializeField] private Transform headset;
        [SerializeField] private Transform visualRoot;

        [Header("Flight Zone (meters)")]
        [SerializeField, Min(0.5f)] private float minPlayerDistance = 1.55f;
        [SerializeField, Min(0.8f)] private float maxPlayerDistance = 3.3f;
        [SerializeField, Range(10f, 179f)] private float maxHorizontalAngle = 145f;
        [SerializeField] private Vector2 eyeRelativeHeightRange = new Vector2(-0.15f, 0.75f);
        [SerializeField, Min(0f)] private float roomWallClearance = 0.25f;
        [SerializeField, Min(0f)] private float obstacleClearance = 0.30f;
        [SerializeField] private LayerMask obstacleLayers;
        [SerializeField] private bool requireMRUKRoomWhenAvailable = true;

        [Header("Seated Patrol & Attack Coordination")]
        [Tooltip("Use the starting headset direction for patrol. Turning your head will not move the patrol zone.")]
        [SerializeField] private bool lockPatrolToInitialHeading = true;
        [Tooltip("Do not attack before at least one actual patrol leg has completed.")]
        [SerializeField] private bool requireFirstPatrolBeforeAttack = true;
        [Tooltip("Degrees the bug must face the player's head before an attack.")]
        [SerializeField, Range(1f, 60f)] private float attackFacingTolerance = 16f;
        [SerializeField, Min(0.25f)] private float minimumPatrolBeforeAttackSeconds = 2f;

        [Header("Flight Motion")]
        [SerializeField, Min(0.05f)] private float cruiseSpeed = 1.15f;
        [SerializeField, Min(0.05f)] private float acceleration = 2.6f;
        [SerializeField, Min(30f)] private float turnSpeedDegrees = 240f;
        [SerializeField, Min(0.05f)] private float waypointArrivalDistance = 0.16f;
        [SerializeField] private Vector2 hoverSecondsRange = new Vector2(0.8f, 1.8f);
        [SerializeField, Range(0f, 0.1f)] private float hoverBob = 0.025f;
        [SerializeField, Range(0f, 12f)] private float bankDegrees = 6f;
        [SerializeField, Range(0.2f, 1f)] private float waypointCruiseFraction = 0.65f;

        [Header("Patrol variety")]
        [Tooltip("Minimum desired bearing change around the player between patrol waypoints. Soft preference, not a hard ban.")]
        [SerializeField, Range(0f, 120f)] private float desiredBearingChange = 38f;
        [Tooltip("Higher values favor unexplored bearings rather than repeatedly picking the farthest left/right point.")]
        [SerializeField, Range(0f, 3f)] private float sectorNoveltyWeight = 1.5f;
        [Tooltip("How strongly the enemy prefers medium-length moves over maximum-distance zigzags.")]
        [SerializeField, Range(0f, 2f)] private float travelPreference = 0.4f;
        [Header("Sampling")]
        [SerializeField, Range(4, 100)] private int waypointAttempts = 36;
        [SerializeField, Min(0.1f)] private float fallbackRecheckSeconds = 1f;
        [SerializeField] private bool debugDraw = true;
        [Header("Prefab Room Test")]
        [Tooltip("If the headset is outside the prefab room, sample within that room for editor debugging. Do not use this to disguise a misaligned live room.")]
        [SerializeField] private bool allowRoomCenteredEditorFallback = true;
        [SerializeField, Range(0.05f, 0.8f)] private float roomBoundsMargin = 0.35f;
        [SerializeField, Min(1f)] private float roomRetrySeconds = 1f;
        [SerializeField] private bool logDiagnostics = true;

        public float DifficultyMultiplier { get; private set; } = 1f;
        public bool IsHovering => mode == FlightMode.Hovering;
        public bool HasMRUKRoom => currentRoom != null;
        public bool IsPlacedInRoom => placedInRoom;
        public bool HasPatrolled => completedPatrolLeg;
        public bool IsAttackReady => placedInRoom && mode == FlightMode.Hovering &&
            (!requireFirstPatrolBeforeAttack || completedPatrolLeg) &&
            Time.time - initialPlacementTime >= minimumPatrolBeforeAttackSeconds &&
            IsFacingPlayer(attackFacingTolerance);

        /// <summary>Hold position and face the player during an attack telegraph.</summary>
        public void SetAttackHold(bool value)
        {
            attackHold = value;
            if (value && placedInRoom)
            {
                mode = FlightMode.Hovering;
                velocity = Vector3.zero;
            }
            else if (placedInRoom)
            {
                // Force a fresh patrol attempt after firing, not a long idle hold.
                hoverUntil = Time.time;
            }
        }

        public bool IsFacingPlayer(float toleranceDegrees)
        {
            if (headset == null) return false;
            Vector3 direction = headset.position - transform.position;
            if (direction.sqrMagnitude < Tiny) return false;
            direction.y *= 0.5f;
            return Vector3.Angle(transform.forward, direction) <= toleranceDegrees;
        }

        private FlightMode mode;
        private MRUKRoom currentRoom;
        private Vector3 waypoint;
        private Vector3 velocity;
        private float hoverUntil;
        private float nextRoomCheck;
        private float bobPhase;
        private Quaternion visualRestRotation;
        private Vector3 visualRestPosition;
        private readonly Collider[] overlapBuffer = new Collider[24];
        private readonly RaycastHit[] castBuffer = new RaycastHit[24];
        private Collider[] selfColliders;
        private bool initialized;
        private bool attackHold;
        private bool completedPatrolLeg;
        private float initialPlacementTime;
        private Vector3 patrolForward;
        private bool placedInRoom;
        private bool warnedMisalignment;
        private float lastSelectedBearing;
        private float previousSelectedBearing;
        private bool hasBearingHistory;
        private float nextPlacementRetry;
        private const float Tiny = 0.0001f;

        private void Awake()
        {
            ResolveReferences();
            selfColliders = GetComponentsInChildren<Collider>(true);
            if (visualRoot != null)
            {
                visualRestPosition = visualRoot.localPosition;
                visualRestRotation = visualRoot.localRotation;
            }
        }

        private void Start()
        {
            RefreshRoom();
            if (headset == null)
            {
                Debug.LogError("[WS Striker] CenterEyeAnchor not found.", this);
                enabled = false;
                return;
            }
            initialized = true;
            patrolForward = Vector3.ProjectOnPlane(headset.forward, Vector3.up);
            if (patrolForward.sqrMagnitude < Tiny) patrolForward = Vector3.forward;
            patrolForward.Normalize();
            TryInitialPlacement();
        }

        private void TryInitialPlacement()
        {
            if (currentRoom == null)
            {
                placedInRoom = false;
                if (logDiagnostics) Debug.Log("[WS Striker] Waiting for MRUK room...", this);
                return;
            }

            bool headInside = currentRoom.IsPositionInRoom(headset.position, true);
            if (!headInside && !warnedMisalignment)
            {
                warnedMisalignment = true;
                Debug.LogWarning("[WS Striker] CenterEyeAnchor is OUTSIDE the loaded MRUK room. Check XR Simulator/room prefab alignment. Editor room-centered fallback may place the Striker inside the room but not in front of the headset.", this);
            }

            // Starting location must NOT require a clear path from the old scene position.
            // Prefer the existing location only if it actually lies in the safe room zone.
            if (IsCandidateSafe(transform.position, false))
            {
                placedInRoom = true;
                initialPlacementTime = Time.time;
                completedPatrolLeg = false;
                BeginHover();
                return;
            }

            if (TryPickWaypoint(out Vector3 start, false, out int candidates))
            {
                transform.position = start;
                velocity = Vector3.zero;
                placedInRoom = true;
                initialPlacementTime = Time.time;
                completedPatrolLeg = false;
                BeginHover();
                if (logDiagnostics) Debug.Log($"[WS Striker] Relocated inside room. Valid candidates: {candidates}, position: {start}", this);
            }
            else
            {
                placedInRoom = false;
                nextPlacementRetry = Time.time + roomRetrySeconds;
                Debug.LogWarning("[WS Striker] Could not find a safe position inside room. Check player alignment, room bounds, and distance/clearance settings.", this);
            }
        }

        private void ResolveReferences()
        {
            if (headset == null)
            {
                OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig != null) headset = rig.centerEyeAnchor;
                if (headset == null && Camera.main != null) headset = Camera.main.transform;
            }
            if (visualRoot == null)
            {
                Transform child = transform.Find("VisualRoot");
                if (child != null) visualRoot = child;
            }
        }

        private void RefreshRoom()
        {
            nextRoomCheck = Time.time + fallbackRecheckSeconds;
            currentRoom = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            // GetCurrentRoom is based on the headset location and may be null
            // when an Editor simulator starts outside its prefab room.
            // Use the loaded prefab room for diagnostics only, never as a
            // substitute for valid device-local room alignment on Quest.
            if (currentRoom == null && Application.isEditor && allowRoomCenteredEditorFallback && MRUK.Instance != null)
            {
                var loadedRooms = MRUK.Instance.GetRooms();
                if (loadedRooms != null && loadedRooms.Count > 0)
                    currentRoom = loadedRooms[0];
            }
        }

        private void Update()
        {
            if (!initialized || headset == null) return;
            if (Time.time >= nextRoomCheck) RefreshRoom();
            if (!placedInRoom)
            {
                if (Time.time >= nextPlacementRetry)
                {
                    nextPlacementRetry = Time.time + roomRetrySeconds;
                    TryInitialPlacement();
                }
                return;
            }
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (mode == FlightMode.Hovering)
            {
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, acceleration * dt);
                Vector3 hoverProposed = transform.position + velocity * dt;
                if (IsCandidateSafe(hoverProposed, true)) transform.position = hoverProposed;
                else velocity = Vector3.zero;
                FacePlayer(dt);
                if (!attackHold && Time.time >= hoverUntil)
                {
                    if (TryPickWaypoint(out Vector3 next, true, out _))
                    {
                        waypoint = next;
                        mode = FlightMode.Seeking;
                    }
                    else BeginHover();
                }
            }
            else
            {
                Vector3 delta = waypoint - transform.position;
                float distance = delta.magnitude;
                if (distance <= waypointArrivalDistance)
                {
                    completedPatrolLeg = true;
                    BeginHover();
                }
                else
                {
                    Vector3 desired = delta / Mathf.Max(distance, Tiny) * cruiseSpeed * DifficultyMultiplier;
                    // Slow before arriving and avoid abrupt changes.
                    desired *= Mathf.Clamp01(distance / waypointCruiseFraction);
                    velocity = Vector3.MoveTowards(velocity, desired, acceleration * dt);
                    Vector3 proposed = transform.position + velocity * dt;
                    if (!IsCandidateSafe(proposed, true) || IsObstacleInSegment(transform.position, proposed))
                    {
                        velocity = Vector3.zero;
                        BeginHover();
                    }
                    else
                    {
                        transform.position = proposed;
                        if (velocity.sqrMagnitude > Tiny)
                            FaceTravelDirection(velocity, dt);
                    }
                }
            }
            AnimateVisual(dt);
        }

        private void BeginHover()
        {
            mode = FlightMode.Hovering;
            hoverUntil = Time.time + Random.Range(
                Mathf.Min(hoverSecondsRange.x, hoverSecondsRange.y),
                Mathf.Max(hoverSecondsRange.x, hoverSecondsRange.y));
        }

        private void FacePlayer(float dt)
        {
            Vector3 direction = headset.position - transform.position;
            direction.y *= 0.5f;
            RotateToward(direction, dt);
        }

        private void FaceTravelDirection(Vector3 direction, float dt) => RotateToward(direction, dt);

        private void RotateToward(Vector3 direction, float dt)
        {
            if (direction.sqrMagnitude < Tiny) return;
            Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeedDegrees * dt);
        }

        private void AnimateVisual(float dt)
        {
            if (visualRoot == null) return;
            bobPhase += dt * 5f;
            visualRoot.localPosition = visualRestPosition +
                Vector3.up * (Mathf.Sin(bobPhase) * hoverBob);
            // Only a subtle visual bank. Keep skeletal animation independent.
            float bank = Mathf.Clamp(Vector3.Dot(velocity, transform.right) * bankDegrees,
                -bankDegrees, bankDegrees);
            visualRoot.localRotation = visualRestRotation * Quaternion.Euler(0f, 0f, -bank);
        }

        private bool TryPickWaypoint(out Vector3 result, bool requireTravel, out int validCount)
        {
            result = transform.position;
            validCount = 0;
            if (currentRoom == null || headset == null) return false;

            bool headInside = currentRoom.IsPositionInRoom(headset.position, true);
            bool roomCentered = !headInside && Application.isEditor && allowRoomCenteredEditorFallback;
            if (!headInside && !roomCentered) return false;

            Bounds roomBounds = currentRoom.GetRoomBounds();
            if (roomBounds.size.x < 0.4f || roomBounds.size.y < 0.4f || roomBounds.size.z < 0.4f)
                return false;

            float bestScore = float.NegativeInfinity;
            Vector3 best = transform.position;
            Vector3 eye = headset.position;
            Vector3 flatForward = lockPatrolToInitialHeading
                ? patrolForward : Vector3.ProjectOnPlane(headset.forward, Vector3.up);
            if (flatForward.sqrMagnitude < Tiny) flatForward = Vector3.forward;
            flatForward.Normalize();
            Quaternion basis = Quaternion.LookRotation(flatForward, Vector3.up);

            for (int i = 0; i < waypointAttempts; i++)
            {
                Vector3 candidate;
                if (roomCentered)
                {
                    // Editor-only diagnostic fallback for a prefab room not containing the simulator headset.
                    candidate = new Vector3(
                        Random.Range(roomBounds.min.x + roomBoundsMargin, roomBounds.max.x - roomBoundsMargin),
                        Random.Range(roomBounds.min.y + roomBoundsMargin, roomBounds.max.y - roomBoundsMargin),
                        Random.Range(roomBounds.min.z + roomBoundsMargin, roomBounds.max.z - roomBoundsMargin));
                }
                else
                {
                    float yaw = Random.Range(-maxHorizontalAngle, maxHorizontalAngle);
                    float distance = Random.Range(minPlayerDistance, maxPlayerDistance);
                    float height = Random.Range(eyeRelativeHeightRange.x, eyeRelativeHeightRange.y);
                    Vector3 direction = basis * Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    candidate = eye + direction * distance + Vector3.up * height;
                }

                if (requireTravel && (candidate - transform.position).sqrMagnitude < 0.4f * 0.4f)
                    continue;
                if (!IsCandidateSafe(candidate, false)) continue;
                // A NEW spawn does not need a valid path from the old staging position.
                if (requireTravel && !IsPathSafe(transform.position, candidate)) continue;

                validCount++;
                // Reward a different bearing relative to the player, not just left/right extremes.
                // This maintains patrol variety even when the MRUK room offers relatively few safe paths.
                Vector3 playerToCandidate = Vector3.ProjectOnPlane(candidate - eye, Vector3.up);
                Vector3 playerToCurrent = Vector3.ProjectOnPlane(transform.position - eye, Vector3.up);
                float bearing = Vector3.SignedAngle(flatForward, playerToCandidate, Vector3.up);
                float angleTravel = playerToCurrent.sqrMagnitude > Tiny
                    ? Vector3.Angle(playerToCurrent, playerToCandidate) : 90f;
                float novelty = !hasBearingHistory ? 1f :
                    Mathf.Clamp01(Mathf.Abs(Mathf.DeltaAngle(bearing, lastSelectedBearing)) /
                    Mathf.Max(1f, desiredBearingChange));
                float olderNovelty = !hasBearingHistory ? 1f :
                    Mathf.Clamp01(Mathf.Abs(Mathf.DeltaAngle(bearing, previousSelectedBearing)) / 75f);
                float travel = (candidate - transform.position).magnitude;
                float distancePreference = 1f - Mathf.Clamp01(Mathf.Abs(travel - 1.3f) / 2f);
                float score = sectorNoveltyWeight * (novelty * 0.7f + olderNovelty * 0.3f)
                    + Mathf.Clamp01(angleTravel / 70f) * 0.35f
                    + distancePreference * travelPreference
                    + Random.Range(0f, 0.65f);
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            if (validCount == 0) return false;
            result = best;
            Vector3 bearingVector = Vector3.ProjectOnPlane(best - eye, Vector3.up);
            if (bearingVector.sqrMagnitude > Tiny)
            {
                previousSelectedBearing = lastSelectedBearing;
                lastSelectedBearing = Vector3.SignedAngle(flatForward, bearingVector, Vector3.up);
                hasBearingHistory = true;
            }
            return true;
        }

        private bool IsPathSafe(Vector3 from, Vector3 to)
        {
            if (IsObstacleInSegment(from, to)) return false;
            // Reject a path that leaves room bounds or crosses captured furniture volumes.
            for (int j = 1; j <= 8; j++)
            {
                Vector3 sample = Vector3.Lerp(from, to, j / 8f);
                if (!IsCandidateSafe(sample, true)) return false;
            }
            return true;
        }

        private bool IsCandidateSafe(Vector3 position, bool inFlight)
        {
            if (headset == null) return false;
            if (currentRoom == null) return false;
            if (requireMRUKRoomWhenAvailable && !currentRoom.IsPositionInRoom(position, true))
                return false;
            // Reject furniture volumes even when the Physics layer mask is empty.
            if (currentRoom.IsPositionInSceneVolume(position, obstacleClearance)) return false;

            bool headInside = currentRoom.IsPositionInRoom(headset.position, true);
            bool roomCentered = !headInside && Application.isEditor && allowRoomCenteredEditorFallback;
            if (!headInside && !roomCentered) return false;
            if (!roomCentered)
            {
                Vector3 diff = position - headset.position;
                float dist = diff.magnitude;
                if (dist < minPlayerDistance || dist > maxPlayerDistance + 0.35f) return false;
                Vector3 flatForward = lockPatrolToInitialHeading
                    ? patrolForward : Vector3.ProjectOnPlane(headset.forward, Vector3.up);
                Vector3 flatDelta = Vector3.ProjectOnPlane(diff, Vector3.up);
                if (flatForward.sqrMagnitude > Tiny && flatDelta.sqrMagnitude > Tiny &&
                    Vector3.Angle(flatForward, flatDelta) > maxHorizontalAngle + (inFlight ? 8f : 0f)) return false;
                float minHeight = eyeRelativeHeightRange.x - 0.15f;
                float maxHeight = eyeRelativeHeightRange.y + 0.15f;
                if (diff.y < minHeight || diff.y > maxHeight) return false;
            }

            int count = Physics.OverlapSphereNonAlloc(position, obstacleClearance,
                overlapBuffer, obstacleLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = overlapBuffer[i];
                if (c == null || IsOwnCollider(c)) continue;
                return false;
            }
            return true;
        }

        private bool IsObstacleInSegment(Vector3 from, Vector3 to)
        {
            if (obstacleLayers.value == 0) return false;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length <= Tiny) return false;
            int count = Physics.SphereCastNonAlloc(from, obstacleClearance,
                delta / length, castBuffer, length, obstacleLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = castBuffer[i].collider;
                if (collider != null && !IsOwnCollider(collider)) return true;
            }
            return false;
        }

        private bool IsOwnCollider(Collider collider)
        {
            if (selfColliders == null) return false;
            for (int i = 0; i < selfColliders.Length; i++)
                if (selfColliders[i] == collider) return true;
            return false;
        }

        public void SetDifficultyMultiplier(float multiplier)
        {
            DifficultyMultiplier = Mathf.Clamp(multiplier, 0.5f, 2.5f);
        }

        public void SetHeadset(Transform target) => headset = target;

        private void OnDrawGizmosSelected()
        {
            if (!debugDraw) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, obstacleClearance);
            if (Application.isPlaying && mode == FlightMode.Seeking)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, waypoint);
                Gizmos.DrawWireSphere(waypoint, waypointArrivalDistance);
            }
        }
    }
}
