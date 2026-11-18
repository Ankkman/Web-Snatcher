using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Flying Striker attack cycle. Uses the existing room-aware flight motor and
    /// left-hand shield. Does not require the old BossEncounterController.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FlyingStrikerController))]
    public sealed class StrikerAttackController : MonoBehaviour
    {
        private enum AttackState { Cooldown, Telegraph, ShotInFlight }

        [Header("References (auto-resolved where safe)")]
        [SerializeField] private FlyingStrikerController flight;
        [SerializeField] private Transform playerHead;
        [SerializeField] private WebShieldController shield;
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private Animator modelAnimator;

        [Header("Attack timing")]
        [SerializeField] private bool attackAutomatically = true;
        [SerializeField] private bool attackOnlyWhileHovering = true;
        [SerializeField] private Vector2 attackIntervalSeconds = new Vector2(4.5f, 6f);
        [SerializeField, Min(0.2f)] private float warningSeconds = 0.9f;
        [SerializeField, Min(0.25f)] private float projectileSpeed = 1.6f;
        [SerializeField, Min(0.01f)] private float projectileRadius = 0.07f;
        [SerializeField, Min(0.05f)] private float playerHitRadius = 0.20f;
        [SerializeField, Range(0f, 180f)] private float maxFireAngleFromHeadset = 75f;
        [SerializeField, Min(0.5f)] private float maxFireDistance = 6f;

        [Header("Intentional attack setup")]
        [SerializeField, Range(2f, 60f)] private float facingToleranceDegrees = 16f;
        [SerializeField, Min(0f)] private float aimSettleSeconds = 0.35f;
        [SerializeField] private bool pauseFlightDuringWarning = true;

        [Header("Optional animation")]
        [Tooltip("Leave empty until the Animator Controller has this Trigger parameter.")]
        [SerializeField] private string attackTriggerParameter = "";

        [Header("Editor diagnostics")]
        [SerializeField] private bool logEvents = true;
        [SerializeField] private bool showTelegraphGlow = true;
        [SerializeField, Min(1)] private int testStartingHealth = 5;

        public event Action<StrikerAttackController> PlayerHit;
        public event Action<StrikerAttackController> ShotBlocked;
        public event Action<StrikerAttackController> ShotFired;
        public event Action<StrikerAttackController> ShotMissed;

        public int TestHealth { get; private set; }
        public float DifficultyMultiplier { get; private set; } = 1f;
        public bool IsTelegraphing => state == AttackState.Telegraph;
        public EnemyProjectile ActiveProjectile => projectile;

        private AttackState state;
        private float nextAttackAt;
        private float telegraphStarted;
        private float facingSince = -1f;
        private EnemyProjectile projectile;
        private GameObject projectileObject;
        private Renderer[] modelRenderers;
        private Material projectileMaterial;
        private Material warningMaterial;
        private Transform warningVisual;

        private void Awake()
        {
            ResolveReferences();
            BuildProjectile();
            BuildWarning();
            TestHealth = testStartingHealth;
        }

        private void OnEnable()
        {
            state = AttackState.Cooldown;
            facingSince = -1f;
            ScheduleNext();
        }

        private void Start()
        {
            ResolveReferences();
            if (playerHead == null || flight == null)
                Debug.LogError("[WS Striker Attack] Missing flight motor or headset.", this);
        }

        private void ResolveReferences()
        {
            if (flight == null) flight = GetComponent<FlyingStrikerController>();
            if (muzzlePoint == null)
            {
                Transform[] transforms = GetComponentsInChildren<Transform>(true);
                foreach (Transform t in transforms)
                    if (t.name == "MuzzlePoint") { muzzlePoint = t; break; }
            }
            if (modelAnimator == null) modelAnimator = GetComponentInChildren<Animator>(true);
            if (playerHead == null)
            {
                OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig != null) playerHead = rig.centerEyeAnchor;
                if (playerHead == null && Camera.main != null)
                    playerHead = Camera.main.transform;
            }
            if (shield == null) shield = FindFirstObjectByType<WebShieldController>();
        }

        private static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            Material m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private void BuildProjectile()
        {
            projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectileObject.name = "WS_Striker_Projectile_Runtime";
            Collider collider = projectileObject.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            projectileMaterial = MakeMaterial("WS_Striker_Bolt_Orange", new Color(1f, 0.37f, 0.10f));
            Renderer renderer = projectileObject.GetComponent<Renderer>();
            if (renderer != null && projectileMaterial != null)
                renderer.sharedMaterial = projectileMaterial;
            projectile = projectileObject.AddComponent<EnemyProjectile>();
            projectile.Resolved += HandleProjectileResolved;
            projectileObject.SetActive(false);
        }

        private void BuildWarning()
        {
            if (!showTelegraphGlow) return;
            GameObject warning = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            warning.name = "WS_Striker_Warning_Runtime";
            warning.transform.SetParent(transform, false);
            warning.transform.localPosition = new Vector3(0f, 0f, 0.20f);
            warning.transform.localScale = Vector3.one * 0.10f;
            Collider collider = warning.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            warningMaterial = MakeMaterial("WS_Striker_Warning_Red", new Color(1f, 0.18f, 0.10f));
            warning.GetComponent<Renderer>().sharedMaterial = warningMaterial;
            warningVisual = warning.transform;
            warning.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!attackAutomatically || flight == null || playerHead == null) return;
            switch (state)
            {
                case AttackState.Cooldown:
                    if (Time.time < nextAttackAt) break;
                    if (CanFire())
                    {
                        if (facingSince < 0f) facingSince = Time.time;
                        if (Time.time - facingSince >= aimSettleSeconds)
                            BeginTelegraph();
                    }
                    else facingSince = -1f;
                    break;
                case AttackState.Telegraph:
                    if (!CanFireWhileTelegraphing())
                    {
                        AbortTelegraph();
                    }
                    else if (Time.time - telegraphStarted >= warningSeconds)
                        FireShot();
                    break;
                case AttackState.ShotInFlight:
                    // Await pooled projectile's result event.
                    break;
            }
        }

        private bool CanFire()
        {
            if (attackOnlyWhileHovering && !flight.IsAttackReady) return false;
            if (!flight.IsFacingPlayer(facingToleranceDegrees)) return false;
            Vector3 from = muzzlePoint != null ? muzzlePoint.position : transform.position;
            Vector3 toHead = playerHead.position - from;
            if (toHead.sqrMagnitude > maxFireDistance * maxFireDistance) return false;
            Vector3 toEnemy = from - playerHead.position;
            float angle = Vector3.Angle(playerHead.forward, toEnemy);
            return angle <= maxFireAngleFromHeadset;
        }

        private bool CanFireWhileTelegraphing()
        {
            if (flight == null || playerHead == null || !flight.IsPlacedInRoom) return false;
            return flight.IsFacingPlayer(facingToleranceDegrees + 7f);
        }

        private void AbortTelegraph()
        {
            if (warningVisual != null) warningVisual.gameObject.SetActive(false);
            if (flight != null) flight.SetAttackHold(false);
            facingSince = -1f;
            ReturnToCooldown();
        }

        private void BeginTelegraph()
        {
            if (pauseFlightDuringWarning && flight != null) flight.SetAttackHold(true);
            state = AttackState.Telegraph;
            telegraphStarted = Time.time;
            if (warningVisual != null) warningVisual.gameObject.SetActive(true);
            if (logEvents) Debug.Log("[WS Striker] ATTACK WARNING", this);
        }

        private void FireShot()
        {
            if (warningVisual != null) warningVisual.gameObject.SetActive(false);
            if (!CanFireWhileTelegraphing()) { AbortTelegraph(); return; }
            if (projectile == null || playerHead == null)
            {
                if (flight != null) flight.SetAttackHold(false);
                ReturnToCooldown();
                return;
            }

            if (modelAnimator != null && !string.IsNullOrEmpty(attackTriggerParameter))
                modelAnimator.SetTrigger(attackTriggerParameter);

            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position;
            projectile.Configure(playerHead, shield, projectileRadius, playerHitRadius);
            projectile.Fire(origin, playerHead.position, projectileSpeed * DifficultyMultiplier);
            if (flight != null) flight.SetAttackHold(false);
            state = AttackState.ShotInFlight;
            ShotFired?.Invoke(this);
            if (logEvents) Debug.Log("[WS Striker] SHOT FIRED", this);
        }

        private void HandleProjectileResolved(EnemyProjectile bolt, EnemyProjectile.Outcome result)
        {
            switch (result)
            {
                case EnemyProjectile.Outcome.Blocked:
                    ShotBlocked?.Invoke(this);
                    if (logEvents) Debug.Log("[WS Striker] SHIELD BLOCK", this);
                    break;
                case EnemyProjectile.Outcome.HitPlayer:
                    TestHealth = Mathf.Max(0, TestHealth - 1);
                    PlayerHit?.Invoke(this);
                    if (logEvents) Debug.Log("[WS Striker] PLAYER HIT. Test HP: " + TestHealth, this);
                    break;
                case EnemyProjectile.Outcome.Missed:
                    ShotMissed?.Invoke(this);
                    if (logEvents) Debug.Log("[WS Striker] SHOT MISSED", this);
                    break;
            }
            ReturnToCooldown();
        }

        private void ReturnToCooldown()
        {
            facingSince = -1f;
            state = AttackState.Cooldown;
            ScheduleNext();
        }

        private void ScheduleNext()
        {
            float low = Mathf.Max(0.3f, Mathf.Min(attackIntervalSeconds.x, attackIntervalSeconds.y));
            float high = Mathf.Max(low, Mathf.Max(attackIntervalSeconds.x, attackIntervalSeconds.y));
            nextAttackAt = Time.time + UnityEngine.Random.Range(low, high) / DifficultyMultiplier;
        }

        public void SetDifficultyMultiplier(float value)
        {
            DifficultyMultiplier = Mathf.Clamp(value, 0.5f, 3f);
        }

        private void OnDisable()
        {
            if (warningVisual != null) warningVisual.gameObject.SetActive(false);
            if (flight != null) flight.SetAttackHold(false);
            if (projectile != null) projectile.Cancel();
            state = AttackState.Cooldown;
        }

        private void OnDestroy()
        {
            if (projectile != null) projectile.Resolved -= HandleProjectileResolved;
            if (projectileObject != null) Destroy(projectileObject);
            if (projectileMaterial != null) Destroy(projectileMaterial);
            if (warningMaterial != null) Destroy(warningMaterial);
        }
    }
}
