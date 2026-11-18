using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WebSnatcher
{
    /// <summary>
    /// V11 isolated boss projectile / shield test.
    /// No core-rip or phase-exclusive input; catching stays enabled.
    /// Replace this prototype with SurvivalGameManager integration later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossEncounterController : MonoBehaviour
    {
        private enum Phase { Idle, Arrival, Incoming, BetweenShots }

        [Header("Dependencies")]
        [SerializeField] private Transform bossRoot;
        [SerializeField] private Transform centerEye;
        [SerializeField] private WebShieldController shield;

        [Header("Test loop")]
        [SerializeField] private bool autoStartTest = true;
        [SerializeField, Min(0f)] private float testStartDelay = 5f;
        [SerializeField, Min(0.5f)] private float arrivalSeconds = 1.0f;
        [SerializeField, Min(0.5f)] private float shotTravelSeconds = 3.0f;
        [SerializeField, Min(0.2f)] private float secondsBetweenShots = 1.4f;
        [SerializeField, Min(0.5f)] private float bossDistance = 2.6f;
        [SerializeField, Min(0.03f)] private float projectileRadius = 0.075f;
        [SerializeField] private bool editorStartWithB = true;

        [Header("Temporary player health")]
        [SerializeField, Min(1)] private int startingHealth = 5;
        [SerializeField] private bool showHitFeedback = true;

        public event Action PlayerHit; // Subscribe HealthManager here later.
        public event Action ShieldBlock;
        public event Action<bool> EncounterFinished; // Compatibility with V10
        public bool IsRunning => phase != Phase.Idle;
        public bool ShieldBlocked { get; private set; }
        public bool BossDefeated => false; // Defeat mechanic comes in next sprint.
        public string CurrentBossState => phase.ToString();
        public int TestHealth { get; private set; }

        private Phase phase = Phase.Idle;
        private float phaseTime;
        private float sceneStart;
        private Vector3 shotStart;
        private Vector3 shotEnd;
        private Vector3 lastShotPosition;
        private Transform projectile;
        private Material projectileMaterial;
        private bool createdBoss;
        private Vector3 bossOriginalPosition;
        private Quaternion bossOriginalRotation;
        private bool firedAuto;
        private TextMesh hitText;
        private float hitFeedbackUntil;

        private void Awake()
        {
            ResolveReferences();
            CreateVisuals();
            TestHealth = startingHealth;
            if (projectile != null) projectile.gameObject.SetActive(false);
        }

        private void Start() { sceneStart = Time.time; }

        private void ResolveReferences()
        {
            if (centerEye == null)
            {
                var rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig != null) centerEye = rig.centerEyeAnchor;
            }
            if (centerEye == null && Camera.main != null) centerEye = Camera.main.transform;
            if (shield == null) shield = FindFirstObjectByType<WebShieldController>();
            if (bossRoot == null)
            {
                var go = GameObject.Find("Boss_Test");
                if (go != null) bossRoot = go.transform;
            }
            if (bossRoot == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Boss_Test_Runtime";
                go.transform.localScale = Vector3.one * .65f;
                bossRoot = go.transform;
                createdBoss = true;
            }
            bossOriginalPosition = bossRoot.position;
            bossOriginalRotation = bossRoot.rotation;
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

        private void CreateVisuals()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "WS_Boss_Incoming_Shot_Runtime";
            projectile = go.transform;
            projectile.localScale = Vector3.one * projectileRadius * 2f;
            projectileMaterial = CreateMaterial("WS_Orange_Incoming", new Color(1f, .43f, .15f, 1f));
            if (projectileMaterial != null) go.GetComponent<Renderer>().sharedMaterial = projectileMaterial;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider); // segment test, not Unity collision timing

            if (showHitFeedback && centerEye != null)
            {
                var textGO = new GameObject("WS_HitFeedback_Runtime");
                textGO.transform.SetParent(centerEye, false);
                textGO.transform.localPosition = new Vector3(0f, .06f, .9f);
                hitText = textGO.AddComponent<TextMesh>();
                hitText.anchor = TextAnchor.MiddleCenter;
                hitText.alignment = TextAlignment.Center;
                hitText.characterSize = .008f;
                hitText.fontSize = 42;
                hitText.color = new Color(1f, .29f, .15f);
                hitText.text = string.Empty;
            }
        }

        public void StartEncounter()
        {
            if (IsRunning) return;
            ResolveReferences();
            if (centerEye == null || shield == null)
            {
                Debug.LogError("[WS Boss] Requires CenterEyeAnchor and WebShieldController", this);
                return;
            }

            // Preserve gaze, catching, right fist and left palm input at all times.
            Vector3 forward = Vector3.ProjectOnPlane(centerEye.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f) forward = centerEye.forward;
            forward.Normalize();
            bossRoot.gameObject.SetActive(true);
            bossRoot.position = centerEye.position + forward * bossDistance;
            bossRoot.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            TestHealth = startingHealth;
            ShieldBlocked = false;
            phase = Phase.Arrival;
            phaseTime = Time.time;
            Debug.Log("[WS Boss] TEST STARTED — H raises shield; release H lowers it", this);
        }

        public void StopEncounter()
        {
            if (!IsRunning) return;
            phase = Phase.Idle;
            if (projectile != null) projectile.gameObject.SetActive(false);
            if (bossRoot != null && !createdBoss)
            {
                bossRoot.position = bossOriginalPosition;
                bossRoot.rotation = bossOriginalRotation;
            }
            EncounterFinished?.Invoke(false);
        }

        private void OnDisable()
        {
            StopEncounter();
            if (hitText != null) hitText.text = string.Empty;
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (editorStartWithB && Keyboard.current != null &&
                Keyboard.current.bKey.wasPressedThisFrame && !IsRunning)
            {
                firedAuto = true;
                StartEncounter();
            }
#endif
            if (!IsRunning && autoStartTest && !firedAuto &&
                Time.time - sceneStart >= testStartDelay)
            {
                firedAuto = true;
                StartEncounter();
            }

            if (hitText != null && Time.time > hitFeedbackUntil)
                hitText.text = string.Empty;

            switch (phase)
            {
                case Phase.Arrival:
                    if (Time.time - phaseTime >= arrivalSeconds) BeginShot();
                    break;
                case Phase.Incoming:
                    UpdateShot();
                    break;
                case Phase.BetweenShots:
                    if (Time.time - phaseTime >= secondsBetweenShots) BeginShot();
                    break;
            }
        }

        private void BeginShot()
        {
            if (centerEye == null || projectile == null) return;
            shotStart = bossRoot.position;
            shotEnd = centerEye.position; // impact point for this shot, fixed at fire time
            lastShotPosition = shotStart;
            projectile.position = shotStart;
            projectile.gameObject.SetActive(true);
            phase = Phase.Incoming;
            phaseTime = Time.time;
            Debug.Log("[WS Boss] SHOT FIRED — block with H/open left palm", this);
        }

        private void UpdateShot()
        {
            float t = Mathf.Clamp01((Time.time - phaseTime) / shotTravelSeconds);
            Vector3 next = Vector3.Lerp(shotStart, shotEnd, t);
            projectile.position = next;

            if (shield != null && shield.TryBlockSegment(lastShotPosition, next, projectileRadius))
            {
                ShieldBlocked = true;
                projectile.gameObject.SetActive(false);
                phase = Phase.BetweenShots;
                phaseTime = Time.time;
                ShieldBlock?.Invoke();
                Debug.Log("[WS Boss] SHIELD BLOCK!", this);
                return;
            }
            lastShotPosition = next;

            // Do not resolve early: shot stays visible until it reaches the endpoint.
            if (t < 1f) return;
            projectile.gameObject.SetActive(false);
            TestHealth = Mathf.Max(0, TestHealth - 1);
            PlayerHit?.Invoke();
            if (hitText != null)
            {
                hitText.text = "HIT!  HP: " + TestHealth;
                hitFeedbackUntil = Time.time + .8f;
            }
            Debug.Log("[WS Boss] PLAYER HIT! Test HP: " + TestHealth, this);
            phase = Phase.BetweenShots;
            phaseTime = Time.time;
            // Keep shooting for this test even if HP=0. Actual game-over comes later.
        }

        private void OnDestroy()
        {
            if (projectile != null) Destroy(projectile.gameObject);
            if (projectileMaterial != null) Destroy(projectileMaterial);
            if (createdBoss && bossRoot != null) Destroy(bossRoot.gameObject);
        }
    }
}
