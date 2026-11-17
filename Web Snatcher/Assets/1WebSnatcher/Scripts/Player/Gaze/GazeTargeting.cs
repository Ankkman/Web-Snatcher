using System;
using System.Collections.Generic;
using UnityEngine;

namespace WebSnatcher
{
    /// <summary>
    /// Head-directed, magnetic bug selection with bounded velocity prediction.
    /// Visuals are owned by SpiderSenseTargetHUD; catching reads CurrentTarget.
    /// Independent of MRUK's scene-data source.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GazeTargeting : MonoBehaviour
    {
        [Header("Tracking")]
        [SerializeField] private Transform gazeOrigin;
        [SerializeField, Range(-4f, 4f)] private float pitchCalibrationDegrees = 0f;

        [Header("Magnetic Acquisition")]
        [SerializeField, Min(0.5f)] private float maxTargetDistance = 5f;
        [SerializeField] private LayerMask bugLayerMask;
        [SerializeField, Range(1f, 12f)] private float nearAssistDegrees = 7f;
        [SerializeField, Range(1f, 10f)] private float farAssistDegrees = 4f;
        [SerializeField, Range(0.05f, 1f)] private float maxSidewaysAssistMeters = 0.42f;
        [SerializeField, Range(0f, 1f)] private float targetSizeContribution = 0.35f;

        [Header("Multiple-Bug Stability")]
        [SerializeField, Range(1f, 2f)] private float retainConeMultiplier = 1.35f;
        [SerializeField, Range(0f, 0.5f)] private float lostTargetGraceSeconds = 0.16f;
        [SerializeField, Range(0f, 0.5f)] private float switchHysteresis = 0.16f;
        [SerializeField, Range(0f, 0.3f)] private float switchConfirmSeconds = 0.09f;

        [Header("Predictive Tracking (V6)")]
        [SerializeField] private bool enablePrediction = true;
        [Tooltip("Seconds of look-ahead. Keep modest to avoid selecting the wrong bug.")]
        [SerializeField, Range(0f, 0.20f)] private float predictionSeconds = 0.09f;
        [Tooltip("Fraction of predicted position used for selection; 0 = V5 behavior.")]
        [SerializeField, Range(0f, 1f)] private float predictionInfluence = 0.35f;
        [Tooltip("Smoothing time for measured bug velocity.")]
        [SerializeField, Range(0.02f, 0.25f)] private float velocitySmoothingSeconds = 0.10f;
        [Tooltip("Reject velocity spikes above this speed (m/s).")]
        [SerializeField, Min(0.5f)] private float maxTrackedSpeed = 4f;
        [Tooltip("Position jumps above this distance are treated as teleportation (metres).")]
        [SerializeField, Min(0.05f)] private float teleportResetDistance = 0.45f;
        [Tooltip("Only for a currently selected target: allow a small extra prediction-based hold.")]
        [SerializeField, Range(0f, 0.5f)] private float motionRetentionBonus = 0.10f;

        [Header("Optional Occlusion (configure after MRUK setup)")]
        [SerializeField] private LayerMask occlusionMask;
        [SerializeField, Range(0f, 0.2f)] private float wallSurfaceTolerance = 0.10f;

        [Header("Editor Debug")]
        [SerializeField] private bool drawEditorDebugRay = false;

        public BugTarget CurrentTarget { get; private set; }
        public event Action<BugTarget, BugTarget> TargetChanged;

        private sealed class MotionHistory
        {
            public Vector3 position;
            public Vector3 velocity;
            public float sampleTime;
            public bool initialized;
            public int lastSeenFrame;
        }

        // The dictionary stores one small entry per active bug; no per-frame lists.
        private readonly Dictionary<BugTarget, MotionHistory> history =
            new Dictionary<BugTarget, MotionHistory>(32);
        private readonly List<BugTarget> staleKeys = new List<BugTarget>(16);
        private int nextPruneFrame;

        private BugTarget pendingSwitch;
        private float pendingSwitchStarted;
        private float lastSeenTime = float.NegativeInfinity;

        private void Awake()
        {
            ResolveCamera();
            ResolveBugLayer();
        }

        private void ResolveCamera()
        {
            if (gazeOrigin != null) return;
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                gazeOrigin = rig.centerEyeAnchor;
                return;
            }
            if (Camera.main != null) gazeOrigin = Camera.main.transform;
        }

        private void ResolveBugLayer()
        {
            if (bugLayerMask.value != 0) return;
            int layer = LayerMask.NameToLayer("Bug");
            if (layer >= 0) bugLayerMask = 1 << layer;
            else Debug.LogError("[WebSnatcher] Create a layer named Bug.", this);
        }

        private void Update()
        {
            if (gazeOrigin == null) ResolveCamera();
            if (gazeOrigin == null)
            {
                SetTarget(null);
                return;
            }

            Vector3 origin = gazeOrigin.position;
            Vector3 forward = gazeOrigin.rotation *
                (Quaternion.Euler(pitchCalibrationDegrees, 0f, 0f) * Vector3.forward);

            // Refresh ALL active motion samples, even outside the aiming cone.
            // This prevents stale velocity when turning toward a moving bug.
            foreach (BugTarget bug in BugTarget.ActiveTargets)
            {
                if (bug != null && bug.CanTarget)
                    SampleMotion(bug, Time.time);
            }

            if (Time.frameCount >= nextPruneFrame)
            {
                PruneInactiveHistories();
                nextPruneFrame = Time.frameCount + 60;
            }

            SelectBestTarget(origin, forward);

#if UNITY_EDITOR
            if (drawEditorDebugRay)
                Debug.DrawRay(origin, forward * maxTargetDistance,
                    CurrentTarget == null ? Color.yellow : Color.red);
#endif
        }

        private void SampleMotion(BugTarget bug, float now)
        {
            if (!history.TryGetValue(bug, out MotionHistory motion))
            {
                motion = new MotionHistory();
                history.Add(bug, motion);
            }

            Vector3 p = bug.AimPosition;
            if (!motion.initialized)
            {
                motion.position = p;
                motion.velocity = Vector3.zero;
                motion.sampleTime = now;
                motion.initialized = true;
                motion.lastSeenFrame = Time.frameCount;
                return;
            }

            float dt = now - motion.sampleTime;
            if (dt <= 0f) return;

            Vector3 delta = p - motion.position;
            float jump = delta.magnitude;
            float instantaneousSpeed = jump / dt;

            // Ignore large teleports and tracking spikes rather than predicting
            // several metres away after pooling or a scene reset.
            if (jump >= teleportResetDistance || instantaneousSpeed > maxTrackedSpeed)
            {
                motion.velocity = Vector3.zero;
            }
            else
            {
                float alpha = 1f - Mathf.Exp(-dt / velocitySmoothingSeconds);
                Vector3 measured = delta / dt;
                motion.velocity = Vector3.Lerp(motion.velocity, measured, alpha);
            }

            motion.position = p;
            motion.sampleTime = now;
            motion.lastSeenFrame = Time.frameCount;
        }

        private Vector3 GetScoringPosition(BugTarget bug)
        {
            Vector3 actual = bug.AimPosition;
            if (!enablePrediction || predictionSeconds <= 0f || predictionInfluence <= 0f)
                return actual;

            if (!history.TryGetValue(bug, out MotionHistory motion) || !motion.initialized)
                return actual;

            // Interpolate toward the small look-ahead position. Do not lead by
            // the full predicted distance: head aiming must stay intuitive.
            return actual + motion.velocity * (predictionSeconds * predictionInfluence);
        }

        private void PruneInactiveHistories()
        {
            staleKeys.Clear();
            foreach (KeyValuePair<BugTarget, MotionHistory> entry in history)
            {
                if (entry.Key == null || !entry.Key.isActiveAndEnabled ||
                    !entry.Key.CanTarget || !BugTarget.ActiveTargets.Contains(entry.Key))
                    staleKeys.Add(entry.Key);
            }
            foreach (BugTarget key in staleKeys) history.Remove(key);
            staleKeys.Clear();
        }

        private void SelectBestTarget(Vector3 origin, Vector3 forward)
        {
            BugTarget best = null;
            float bestScore = float.PositiveInfinity;

            foreach (BugTarget bug in BugTarget.ActiveTargets)
            {
                if (bug == null || !bug.CanTarget) continue;
                if ((bugLayerMask.value & (1 << bug.gameObject.layer)) == 0) continue;
                if (!TryScore(bug, origin, forward, false, out float score)) continue;

                if (score < bestScore)
                {
                    best = bug;
                    bestScore = score;
                }
            }

            bool currentValid = CurrentTarget != null && CurrentTarget.CanTarget;
            float currentScore = float.PositiveInfinity;
            bool currentInCone = currentValid &&
                TryScore(CurrentTarget, origin, forward, true, out currentScore);

            if (best == CurrentTarget)
            {
                pendingSwitch = null;
                lastSeenTime = Time.time;
                return;
            }

            if (best != null)
            {
                if (currentInCone && currentScore <= bestScore + switchHysteresis)
                {
                    pendingSwitch = null;
                    lastSeenTime = Time.time;
                    return;
                }

                if (currentInCone && switchConfirmSeconds > 0f)
                {
                    if (pendingSwitch != best)
                    {
                        pendingSwitch = best;
                        pendingSwitchStarted = Time.time;
                        return;
                    }
                    if (Time.time - pendingSwitchStarted < switchConfirmSeconds)
                        return;
                }

                pendingSwitch = null;
                SetTarget(best);
                lastSeenTime = Time.time;
                return;
            }

            pendingSwitch = null;
            if (currentInCone)
            {
                lastSeenTime = Time.time;
                return;
            }
            if (currentValid && Time.time - lastSeenTime <= lostTargetGraceSeconds)
                return;

            SetTarget(null);
        }

        private bool TryScore(BugTarget bug, Vector3 origin, Vector3 forward,
            bool forRetention, out float score)
        {
            score = float.PositiveInfinity;
            if (bug == null || !bug.CanTarget) return false;

            // Physical target position is authoritative for distance/occlusion.
            Vector3 actualPosition = bug.AimPosition;
            Vector3 actualToBug = actualPosition - origin;
            float actualDistance = actualToBug.magnitude;
            if (actualDistance < 0.05f || actualDistance > maxTargetDistance)
                return false;

            Vector3 scoredPosition = GetScoringPosition(bug);
            Vector3 toBug = scoredPosition - origin;
            float depth = Vector3.Dot(toBug, forward);
            if (depth <= 0f) return false;

            float side = Vector3.Cross(forward, toBug).magnitude;
            float angle = Mathf.Atan2(side, depth) * Mathf.Rad2Deg;

            float blend = Mathf.InverseLerp(0.8f, 4f, actualDistance);
            float cone = Mathf.Lerp(nearAssistDegrees, farAssistDegrees, blend);
            float allowedSide = Mathf.Min(Mathf.Tan(cone * Mathf.Deg2Rad) * depth,
                maxSidewaysAssistMeters);
            allowedSide += Mathf.Clamp(bug.TargetRadius, 0.01f, 0.5f) * targetSizeContribution;

            float allowedAngle = Mathf.Atan2(allowedSide, depth) * Mathf.Rad2Deg;
            float retentionMultiplier = forRetention ? retainConeMultiplier : 1f;
            // Extra retention is intentionally tiny and only for genuinely moving bugs.
            if (forRetention && enablePrediction && history.TryGetValue(bug, out MotionHistory motion) &&
                motion.velocity.sqrMagnitude > 0.04f)
                retentionMultiplier += motionRetentionBonus;

            if (angle > allowedAngle * retentionMultiplier) return false;
            if (IsOccluded(origin, actualToBug, actualDistance)) return false;

            score = angle / Mathf.Max(0.1f, allowedAngle);
            float physicalAngle = Mathf.Atan2(bug.TargetRadius * 0.5f, depth) * Mathf.Rad2Deg;
            if (angle <= physicalAngle) score -= 0.20f;
            score += (actualDistance / maxTargetDistance) * 0.02f;
            return true;
        }

        private bool IsOccluded(Vector3 origin, Vector3 toBug, float distance)
        {
            int mask = occlusionMask.value & ~bugLayerMask.value;
            if (mask == 0 || distance <= wallSurfaceTolerance) return false;
            return Physics.Raycast(origin, toBug / distance,
                distance - wallSurfaceTolerance, mask, QueryTriggerInteraction.Ignore);
        }

        private void SetTarget(BugTarget next)
        {
            if (CurrentTarget == next) return;
            BugTarget previous = CurrentTarget;
            if (previous != null) previous.SetHighlighted(false);
            CurrentTarget = next;
            if (next != null) next.SetHighlighted(true);
            TargetChanged?.Invoke(previous, next);
        }

        public void SetTargetingEnabled(bool enabled)
        {
            this.enabled = enabled;
            if (!enabled)
            {
                pendingSwitch = null;
                SetTarget(null);
            }
        }

        private void OnDisable()
        {
            pendingSwitch = null;
            SetTarget(null);
        }

        private void OnValidate()
        {
            maxTargetDistance = Mathf.Max(0.5f, maxTargetDistance);
            farAssistDegrees = Mathf.Min(farAssistDegrees, nearAssistDegrees);
            velocitySmoothingSeconds = Mathf.Max(0.02f, velocitySmoothingSeconds);
            maxTrackedSpeed = Mathf.Max(0.5f, maxTrackedSpeed);
            teleportResetDistance = Mathf.Max(0.05f, teleportResetDistance);
        }
    }
}
