using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Endless-mode prototype: catches charge one Web Overload blast; right-fist
    /// press while looking at an active boss fires it. The existing shield,
    /// gaze, normal web tether and projectile loop are not modified.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossPowerController : MonoBehaviour
    {
        private enum BlastState { Ready, Flying, Bursting }

        [Header("Auto-resolved References")]
        [SerializeField] private FistGestureInput rightFist;
        [SerializeField] private BossEncounterController bossEncounter;
        [SerializeField] private Transform bossRoot;
        [SerializeField] private Transform centerEye;
        [SerializeField] private Transform rightHand;

        [Header("Charge")]
        [SerializeField, Min(1)] private int catchesPerCharge = 5;
        [SerializeField] private bool persistProgressAfterBoss = true;

        [Header("Target Acquisition")]
        [SerializeField, Range(2f, 30f)] private float bossAimAngleDegrees = 13f;
        [SerializeField, Min(0.5f)] private float bossMaxDistance = 8f;
        [SerializeField] private Transform optionalWeakPoint;

        [Header("Blast Animation")]
        [SerializeField, Min(0.05f)] private float flightSeconds = 0.32f;
        [SerializeField, Min(0.05f)] private float burstSeconds = 0.5f;
        [SerializeField, Min(0.1f)] private float burstRadius = 0.85f;
        [SerializeField, Min(0.01f)] private float coreDiameter = 0.10f;
        [SerializeField] private Color webIvory = new Color(.95f, .88f, .72f, 1f);
        [SerializeField] private Color powerOrange = new Color(1f, .36f, .18f, 1f);

        [Header("Boss Encounter Test Loop")]
        [SerializeField] private bool automaticallySpawnBoss = true;
        [SerializeField, Min(0f)] private float firstBossAfterSeconds = 30f;
        [SerializeField, Min(10f)] private float bossRepeatSeconds = 45f;
        [SerializeField] private bool hideBossAtStart = true;

        [Header("Editor Test Shortcuts")]
        [SerializeField] private bool allowEditorShortcuts = true;

        public int CatchProgress { get; private set; }
        public bool IsCharged { get; private set; }
        public bool IsFiring => blastState != BlastState.Ready;
        public event Action<int, int> ChargeProgressChanged;
        public event Action PowerCharged;
        public event Action PowerFired;
        public event Action BossDestroyed;

        private BlastState blastState;
        private float stateTime;
        private float nextBossTime;
        private Vector3 flightFrom;
        private Vector3 flightTo;
        private Material silkMaterial;
        private Material burstMaterial;
        private Transform coreVisual;
        private LineRenderer trail;
        private LineRenderer burstRing;
        private const int RingSegments = 40;
        private readonly Vector3[] ringPositions = new Vector3[RingSegments + 1];
        private bool subscribed;

        private void Awake()
        {
            ResolveReferences();
            BuildVisuals();
        }

        private void Start()
        {
            if (hideBossAtStart && bossRoot != null &&
                (bossEncounter == null || !bossEncounter.IsRunning))
                bossRoot.gameObject.SetActive(false);
            nextBossTime = Time.time + firstBossAfterSeconds;
            ReportCharge();
        }

        private void OnEnable()
        {
            BugTarget.AnyCatchCompleted += OnBugCaught;
            SubscribeFistIfReady();
        }

        private void OnDisable()
        {
            BugTarget.AnyCatchCompleted -= OnBugCaught;
            if (subscribed && rightFist != null)
                rightFist.FistClosed -= OnRightFistClosed;
            subscribed = false;
            blastState = BlastState.Ready;
            HideVisuals();
        }

        private void SubscribeFistIfReady()
        {
            if (subscribed) return;
            if (rightFist == null) rightFist = FindFirstObjectByType<FistGestureInput>();
            if (rightFist == null) return;
            rightFist.FistClosed += OnRightFistClosed;
            subscribed = true;
        }

        private void ResolveReferences()
        {
            if (rightFist == null) rightFist = FindFirstObjectByType<FistGestureInput>();
            if (bossEncounter == null) bossEncounter = FindFirstObjectByType<BossEncounterController>();
            if (bossRoot == null)
            {
                GameObject go = GameObject.Find("Boss_Test");
                if (go != null) bossRoot = go.transform;
            }
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (centerEye == null) centerEye = rig.centerEyeAnchor;
                if (rightHand == null) rightHand = rig.rightHandAnchor;
            }
            if (centerEye == null && Camera.main != null) centerEye = Camera.main.transform;
        }

        private void OnBugCaught(BugTarget bug)
        {
            if (IsCharged) return; // one stored charge, no overflow
            CatchProgress = Mathf.Min(catchesPerCharge, CatchProgress + 1);
            if (CatchProgress >= catchesPerCharge)
            {
                IsCharged = true;
                PowerCharged?.Invoke();
                Debug.Log("[WS POWER] WEB OVERLOAD CHARGED — look at boss and make a right fist", this);
            }
            ReportCharge();
        }

        private void ReportCharge()
        {
            ChargeProgressChanged?.Invoke(CatchProgress, catchesPerCharge);
            Debug.Log("[WS POWER] Charge " + CatchProgress + "/" + catchesPerCharge, this);
        }

        private Vector3 BossAimPoint => optionalWeakPoint != null
            ? optionalWeakPoint.position
            : bossRoot != null ? bossRoot.position : Vector3.zero;

        private bool BossIsAimable()
        {
            if (!IsCharged || bossRoot == null || centerEye == null ||
                bossEncounter == null || !bossEncounter.IsRunning ||
                !bossRoot.gameObject.activeInHierarchy || IsFiring)
                return false;
            Vector3 to = BossAimPoint - centerEye.position;
            float sqr = to.sqrMagnitude;
            if (sqr < 0.01f || sqr > bossMaxDistance * bossMaxDistance) return false;
            float dot = Vector3.Dot(centerEye.forward, to / Mathf.Sqrt(sqr));
            return dot >= Mathf.Cos(bossAimAngleDegrees * Mathf.Deg2Rad);
        }

        private void OnRightFistClosed()
        {
            if (BossIsAimable()) Fire();
        }

        private void Fire()
        {
            if (!BossIsAimable()) return;
            IsCharged = false;
            CatchProgress = 0;
            ReportCharge();
            flightFrom = rightHand != null ? rightHand.position :
                centerEye.position + centerEye.forward * .35f + centerEye.right * .20f;
            flightTo = BossAimPoint;
            coreVisual.position = flightFrom;
            coreVisual.gameObject.SetActive(true);
            trail.enabled = true;
            blastState = BlastState.Flying;
            stateTime = Time.time;
            PowerFired?.Invoke();
            Debug.Log("[WS POWER] OVERLOAD FIRED", this);
        }

        private void Update()
        {
            if (!subscribed) SubscribeFistIfReady();
            if (centerEye == null || bossRoot == null || bossEncounter == null)
                ResolveReferences();

#if UNITY_EDITOR
            if (allowEditorShortcuts && Keyboard.current != null)
            {
                if (Keyboard.current.cKey.wasPressedThisFrame)
                {
                    CatchProgress = catchesPerCharge;
                    IsCharged = true;
                    PowerCharged?.Invoke();
                    ReportCharge();
                }
                // In test mode, pressing F with the boss centered triggers the
                // normal FistGestureInput event. Do not read legacy Input.
            }
#endif
            if (automaticallySpawnBoss && bossEncounter != null &&
                !bossEncounter.IsRunning && Time.time >= nextBossTime)
            {
                bossEncounter.StartEncounter();
                nextBossTime = Time.time + bossRepeatSeconds;
            }

            if (blastState == BlastState.Flying)
            {
                float t = Mathf.Clamp01((Time.time - stateTime) / flightSeconds);
                Vector3 next = Vector3.Lerp(flightFrom, flightTo, t * t * (3f - 2f * t));
                coreVisual.position = next;
                trail.SetPosition(0, flightFrom);
                trail.SetPosition(1, next);
                if (t >= 1f) ResolveBossHit();
            }
            else if (blastState == BlastState.Bursting)
            {
                float t = Mathf.Clamp01((Time.time - stateTime) / burstSeconds);
                float radius = Mathf.Lerp(.05f, burstRadius, t);
                Vector3 normal = centerEye != null ? centerEye.position - flightTo : Vector3.back;
                if (normal.sqrMagnitude < .01f) normal = Vector3.back;
                Quaternion rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);
                for (int i = 0; i <= RingSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / RingSegments;
                    ringPositions[i] = flightTo + rotation * new Vector3(
                        Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                }
                burstRing.SetPositions(ringPositions);
                if (t >= 1f)
                {
                    blastState = BlastState.Ready;
                    HideVisuals();
                }
            }
        }

        private void ResolveBossHit()
        {
            coreVisual.gameObject.SetActive(false);
            trail.enabled = false;
            blastState = BlastState.Bursting;
            stateTime = Time.time;
            burstRing.enabled = true;

            // Stop firing and end the current encounter. Boss defeat is
            // NOT handled by the temporary BossHitTarget hits-to-defeat counter.
            if (bossEncounter != null && bossEncounter.IsRunning)
                bossEncounter.StopEncounter();
            if (bossRoot != null) bossRoot.gameObject.SetActive(false);
            if (!persistProgressAfterBoss) CatchProgress = 0;
            nextBossTime = Time.time + bossRepeatSeconds;
            BossDestroyed?.Invoke();
            Debug.Log("[WS POWER] MOTHER-BUG DEFEATED — bonus event", this);
        }

        private static Material CreateMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            return material;
        }

        private void BuildVisuals()
        {
            silkMaterial = CreateMaterial("WS_Overload_Silk", webIvory);
            burstMaterial = CreateMaterial("WS_Overload_Impact", powerOrange);
            if (silkMaterial == null || burstMaterial == null) return;

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "WS_Charged_Web_Bomb_Runtime";
            orb.transform.SetParent(transform, false);
            orb.transform.localScale = Vector3.one * coreDiameter;
            Collider c = orb.GetComponent<Collider>();
            if (c != null) Destroy(c);
            orb.GetComponent<Renderer>().sharedMaterial = silkMaterial;
            coreVisual = orb.transform;

            trail = MakeLine("WS_Overload_Trail", silkMaterial, 2, .008f);
            burstRing = MakeLine("WS_Overload_Burst", burstMaterial, RingSegments + 1, .006f);
            HideVisuals();
        }

        private LineRenderer MakeLine(string name, Material material, int points, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void HideVisuals()
        {
            if (coreVisual != null) coreVisual.gameObject.SetActive(false);
            if (trail != null) trail.enabled = false;
            if (burstRing != null) burstRing.enabled = false;
        }

        private void OnDestroy()
        {
            if (silkMaterial != null) Destroy(silkMaterial);
            if (burstMaterial != null) Destroy(burstMaterial);
        }
    }
}
