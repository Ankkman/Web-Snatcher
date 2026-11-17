using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Fifteen-second boss prototype: incoming attack -> left palm shield -> right fist web rip.
    /// No dependence on MRUK, room data, scoring, or the retired slingshot.
    /// Call StartEncounter() from the future game manager. For isolated tests use the auto test timer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossEncounterController : MonoBehaviour
    {
        private enum Phase { Idle, Arrival, Incoming, Exposed, Ripping, Victory, Finished }

        [Header("Existing Components (auto-find where safe)")]
        [SerializeField] private Transform centerEye;
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform rightHandAnchor;
        [SerializeField] private FistGestureInput rightFist;
        [SerializeField] private LeftPalmShieldInput leftPalm;
        [SerializeField] private WebTetherController catchTether;
        [SerializeField] private GazeTargeting gaze;
        [SerializeField] private SlingshotController retiredSlingshot;
        [SerializeField] private ProceduralWebRenderer bossWeb;

        [Header("Boss & Prototype")]
        [Tooltip("Optional existing Boss_Test sphere. If absent, a temporary sphere is created.")]
        [SerializeField] private Transform bossRoot;
        [SerializeField, Min(1.3f)] private float bossDistance = 2.4f;
        [SerializeField] private Vector3 coreLocalOffset = new Vector3(0, 0, 0.28f);
        [SerializeField, Min(0.1f)] private float coreDiameter = 0.32f;

        [Header("Timing")]
        [SerializeField, Min(6f)] private float encounterSeconds = 15f;
        [SerializeField, Range(0.5f, 5f)] private float arrivalSeconds = 2.5f;
        [SerializeField, Range(0.5f, 5f)] private float attackTravelSeconds = 3f;
        [SerializeField, Range(0.2f, 3f)] private float victorySeconds = 1.5f;

        [Header("Shield & Boss Interaction")]
        [SerializeField, Range(0.2f, 0.8f)] private float shieldRadius = 0.42f;
        [SerializeField, Range(0.2f, 1.5f)] private float shieldForwardOffset = 0.24f;
        [SerializeField, Range(5f, 30f)] private float coreAimAngle = 18f;
        [SerializeField, Range(0.05f, 0.3f)] private float yankDistance = 0.10f;
        [SerializeField, Range(0f, 0.12f)] private float minBackwardYank = 0.025f;
        [SerializeField, Range(0.5f, 5f)] private float maxHoldSeconds = 3f;

        [Header("Standalone Test")]
        [Tooltip("For Quest testing: run normal catch for this many seconds then start boss.")]
        [SerializeField] private bool autoStartTest = true;
        [SerializeField, Min(1f)] private float testCatchSeconds = 20f;
        [SerializeField] private bool editorDebugKeys = true;
        [SerializeField, Min(0.1f)] private float editorPullSpeed = 0.35f;

        public event Action<bool> EncounterFinished; // true=won, false=not defeated
        public bool IsRunning => phase != Phase.Idle && phase != Phase.Finished;
        public bool ShieldBlocked { get; private set; }
        public bool BossDefeated { get; private set; }

        private Phase phase = Phase.Idle;
        private float phaseStart;
        private float encounterStart;
        private float sceneStart;
        private bool testStarted;
        private bool ownsBoss;
        private Transform core;
        private Renderer coreRenderer;
        private Transform projectile;
        private Material combatMaterial;
        private Material coreMaterial;
        private Material projectileMaterial;
        private readonly LineRenderer[] shieldLines = new LineRenderer[11];
        private LineRenderer coreBracket;
        private Vector3 attackStart;
        private Vector3 attackEnd;
        private float attackStarted;
        private Vector3 startHandRelative;
        private Vector3 pullDirection;
        private Vector3 simulatedHandOffset;
        private float fistStarted;
        private float victoryStarted;
        private Vector3 bossOriginalScale;
        private bool cachedTetherEnabled;
        private bool cachedGazeEnabled;
        private bool cachedSlingshotEnabled;

        private void Awake()
        {
            ResolveReferences();
            BuildVisuals();
            SetBossVisible(false);
            SetShieldVisible(false);
            if (coreBracket != null) coreBracket.enabled = false;
        }

        private void Start() { sceneStart = Time.time; }

        private void OnEnable()
        {
            ResolveReferences();
            if (rightFist != null) rightFist.FistClosed += HandleFistClosed;
        }

        private void OnDisable()
        {
            if (rightFist != null) rightFist.FistClosed -= HandleFistClosed;
            StopEncounter();
        }

        private void ResolveReferences()
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (centerEye == null) centerEye = rig.centerEyeAnchor;
                if (leftHandAnchor == null) leftHandAnchor = rig.leftHandAnchor;
                if (rightHandAnchor == null) rightHandAnchor = rig.rightHandAnchor;
            }
            if (centerEye == null && Camera.main != null) centerEye = Camera.main.transform;
            if (rightFist == null) rightFist = FindFirstObjectByType<FistGestureInput>();
            if (leftPalm == null) leftPalm = FindFirstObjectByType<LeftPalmShieldInput>();
            if (catchTether == null) catchTether = FindFirstObjectByType<WebTetherController>();
            if (gaze == null) gaze = FindFirstObjectByType<GazeTargeting>();
            if (retiredSlingshot == null) retiredSlingshot = FindFirstObjectByType<SlingshotController>();
            if (bossRoot == null)
            {
                var existing = GameObject.Find("Boss_Test");
                if (existing != null) bossRoot = existing.transform;
            }
        }

        private static Material MakeMat(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private LineRenderer NewLine(string name, int count, float width, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = count;
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.sharedMaterial = material;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        private void BuildVisuals()
        {
            combatMaterial = MakeMat("WS_Shield_Silk", new Color(.95f, .88f, .75f));
            coreMaterial = MakeMat("WS_Boss_Core", new Color(1f, .40f, .19f));
            projectileMaterial = MakeMat("WS_Boss_Projectile", new Color(1f, .62f, .32f));

            for (int i = 0; i < 3; ++i)
                shieldLines[i] = NewLine("Shield_Ring_" + i, 33, .0035f, combatMaterial);
            for (int i = 3; i < shieldLines.Length; ++i)
                shieldLines[i] = NewLine("Shield_Spoke_" + (i - 3), 2, .002f, combatMaterial);
            coreBracket = NewLine("Boss_Core_Indicator", 33, .004f, combatMaterial);

            if (bossWeb == null)
            {
                var go = new GameObject("Boss_Web_Visual");
                go.transform.SetParent(transform, false);
                bossWeb = go.AddComponent<ProceduralWebRenderer>();
            }
            bossWeb.HideWeb();

            if (bossRoot == null)
            {
                var created = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                created.name = "Boss_Test_Runtime";
                created.transform.localScale = Vector3.one * .7f;
                bossRoot = created.transform;
                ownsBoss = true;
            }
            bossOriginalScale = bossRoot.localScale;
            var coreGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coreGO.name = "Boss_WeakCore_Runtime";
            core = coreGO.transform;
            core.SetParent(bossRoot, false);
            core.localPosition = coreLocalOffset;
            core.localRotation = Quaternion.identity;
            // Cancel the boss parent scale so core remains a readable physical size.
            core.localScale = new Vector3(
                coreDiameter / Mathf.Max(.001f, Mathf.Abs(bossRoot.lossyScale.x)),
                coreDiameter / Mathf.Max(.001f, Mathf.Abs(bossRoot.lossyScale.y)),
                coreDiameter / Mathf.Max(.001f, Mathf.Abs(bossRoot.lossyScale.z)));
            var coreCol = coreGO.GetComponent<Collider>();
            if (coreCol != null) Destroy(coreCol);
            coreRenderer = coreGO.GetComponent<Renderer>();
            coreRenderer.sharedMaterial = coreMaterial;
            coreGO.SetActive(false);

            var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shot.name = "Boss_Incoming_Energy_Runtime";
            projectile = shot.transform;
            projectile.localScale = Vector3.one * .15f;
            shot.GetComponent<Renderer>().sharedMaterial = projectileMaterial;
            var collider = shot.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            shot.SetActive(false);
        }

        private void SetBossVisible(bool visible)
        {
            if (bossRoot != null) bossRoot.gameObject.SetActive(visible);
        }
        private void SetShieldVisible(bool visible)
        {
            foreach (var line in shieldLines)
                if (line != null) line.enabled = visible;
        }

        public void StartEncounter()
        {
            if (IsRunning) return;
            ResolveReferences();
            if (centerEye == null || leftHandAnchor == null || rightHandAnchor == null || rightFist == null)
            {
                Debug.LogError("[WS Boss] Missing rig hand anchors or FistGestureInput", this);
                return;
            }
            cachedTetherEnabled = catchTether != null && catchTether.enabled;
            cachedGazeEnabled = gaze != null && gaze.enabled;
            cachedSlingshotEnabled = retiredSlingshot != null && retiredSlingshot.enabled;
            if (catchTether != null) catchTether.enabled = false;
            if (gaze != null) gaze.SetTargetingEnabled(false);
            if (retiredSlingshot != null) { retiredSlingshot.DeactivateBossPhase(); retiredSlingshot.enabled = false; }

            Vector3 flatForward = Vector3.ProjectOnPlane(centerEye.forward, Vector3.up);
            if (flatForward.sqrMagnitude < .0001f) flatForward = centerEye.forward;
            flatForward.Normalize();
            bossRoot.position = centerEye.position + flatForward * bossDistance;
            bossRoot.localScale = bossOriginalScale;
            bossRoot.rotation = Quaternion.LookRotation(-flatForward, Vector3.up);
            core.localPosition = coreLocalOffset;
            // If parent has non-unit scale, keep core at predictable apparent size.
            core.localScale = new Vector3(coreDiameter / Mathf.Max(.01f, Mathf.Abs(bossRoot.lossyScale.x)),
                coreDiameter / Mathf.Max(.01f, Mathf.Abs(bossRoot.lossyScale.y)),
                coreDiameter / Mathf.Max(.01f, Mathf.Abs(bossRoot.lossyScale.z)));
            core.gameObject.SetActive(false);
            ShieldBlocked = false;
            BossDefeated = false;
            simulatedHandOffset = Vector3.zero;
            encounterStart = Time.time;
            phaseStart = Time.time;
            phase = Phase.Arrival;
            SetBossVisible(true);
            SetShieldVisible(false);
            coreBracket.enabled = false;
            projectile.gameObject.SetActive(false);
            Debug.Log("[WS Boss] START — watch for the incoming shot", this);
        }

        public void StopEncounter()
        {
            if (phase == Phase.Idle) return;
            phase = Phase.Idle;
            if (bossWeb != null) bossWeb.HideWeb();
            if (projectile != null) projectile.gameObject.SetActive(false);
            if (coreBracket != null) coreBracket.enabled = false;
            SetShieldVisible(false);
            SetBossVisible(false);
            if (catchTether != null) catchTether.enabled = cachedTetherEnabled;
            if (gaze != null) gaze.SetTargetingEnabled(cachedGazeEnabled);
            if (retiredSlingshot != null) retiredSlingshot.enabled = cachedSlingshotEnabled;
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (editorDebugKeys && Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame && !IsRunning)
                StartEncounter();
#endif
            if (phase == Phase.Idle && autoStartTest && !testStarted && Time.time - sceneStart >= testCatchSeconds)
            {
                testStarted = true;
                StartEncounter();
            }
            if (!IsRunning) return;

            if (Time.time - encounterStart >= encounterSeconds && phase != Phase.Victory)
            {
                Finish(false);
                return;
            }

            switch (phase)
            {
                case Phase.Arrival:
                    if (Time.time - phaseStart >= arrivalSeconds) BeginIncoming();
                    break;
                case Phase.Incoming:
                    UpdateIncoming();
                    break;
                case Phase.Exposed:
                    UpdateShield(false);
                    UpdateCoreIndicator();
                    break;
                case Phase.Ripping:
                    UpdateRip();
                    break;
                case Phase.Victory:
                    UpdateVictory();
                    break;
            }
        }

        private void BeginIncoming()
        {
            phase = Phase.Incoming;
            phaseStart = Time.time;
            attackStarted = Time.time;
            attackStart = bossRoot.position;
            attackEnd = centerEye.position;
            projectile.position = attackStart;
            projectile.gameObject.SetActive(true);
            Debug.Log("[WS Boss] INCOMING — OPEN LEFT PALM (H in Editor)", this);
        }

        private Vector3 GetShieldCenter()
        {
            return leftHandAnchor.position + centerEye.forward * shieldForwardOffset;
        }

        private bool IsShieldActive()
        {
            return leftPalm != null && leftPalm.IsOpen;
        }

        private void UpdateIncoming()
        {
            UpdateShield(IsShieldActive());
            float t = Mathf.Clamp01((Time.time - attackStarted) / attackTravelSeconds);
            Vector3 next = Vector3.Lerp(attackStart, attackEnd, t);
            projectile.position = next;
            if (IsShieldActive() &&
                Vector3.Distance(GetShieldCenter(), next) <= shieldRadius + .08f)
            {
                ShieldBlocked = true;
                projectile.gameObject.SetActive(false);
                Debug.Log("[WS Boss] SHIELD BLOCK!", this);
                bossWeb.PlayImpact(next, centerEye.position - next, .35f);
                ExposeCore();
                return;
            }
            if (t >= 1f || Vector3.Distance(next, centerEye.position) < .23f)
            {
                projectile.gameObject.SetActive(false);
                Debug.Log("[WS Boss] SHIELD MISSED — weak point still opens", this);
                ExposeCore();
            }
        }

        private void UpdateShield(bool enabled)
        {
            SetShieldVisible(enabled);
            if (!enabled) return;
            Vector3 center = GetShieldCenter();
            Vector3 towardEye = centerEye.position - center;
            if (towardEye.sqrMagnitude < .001f) towardEye = -centerEye.forward;
            Quaternion orientation = Quaternion.LookRotation(towardEye.normalized, Vector3.up);
            for (int ring = 0; ring < 3; ring++)
            {
                float radius = shieldRadius * (ring + 1) / 3f;
                var lr = shieldLines[ring];
                for (int p = 0; p < 33; p++)
                {
                    float a = Mathf.PI * 2f * p / 32f;
                    lr.SetPosition(p, center + orientation * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0));
                }
            }
            for (int i = 0; i < 8; i++)
            {
                float angle = Mathf.PI * 2f * i / 8f;
                shieldLines[i + 3].SetPosition(0, center);
                shieldLines[i + 3].SetPosition(1, center + orientation * new Vector3(
                    Mathf.Cos(angle) * shieldRadius, Mathf.Sin(angle) * shieldRadius, 0));
            }
        }

        private void ExposeCore()
        {
            phase = Phase.Exposed;
            phaseStart = Time.time;
            SetShieldVisible(false);
            core.gameObject.SetActive(true);
            coreBracket.enabled = true;
            Debug.Log("[WS Boss] CORE EXPOSED — LOOK, FIST, PULL", this);
        }

        private void UpdateCoreIndicator()
        {
            Vector3 p = core.position;
            Vector3 toEye = centerEye.position - p;
            Quaternion rotation = Quaternion.LookRotation(toEye.sqrMagnitude > .0001f ? toEye : -centerEye.forward, Vector3.up);
            float r = coreDiameter * .65f * (1f + .04f * Mathf.Sin(Time.time * 7f));
            for (int i = 0; i < 33; i++)
            {
                float a = Mathf.PI * 2f * i / 32f;
                coreBracket.SetPosition(i, p + rotation * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
            }
        }

        private void HandleFistClosed()
        {
            if (phase != Phase.Exposed || core == null || centerEye == null) return;
            Vector3 toCore = core.position - centerEye.position;
            if (Vector3.Angle(centerEye.forward, toCore) > coreAimAngle) return;
            Vector3 hand = GetRightHand();
            startHandRelative = hand - centerEye.position;
            pullDirection = centerEye.position - hand;
            if (pullDirection.sqrMagnitude < .001f) pullDirection = -centerEye.forward;
            pullDirection.Normalize();
            fistStarted = Time.time;
            simulatedHandOffset = Vector3.zero;
            phase = Phase.Ripping;
            Debug.Log("[WS Boss] CORE WEB ATTACHED — pull toward headset", this);
        }

        private Vector3 GetRightHand() { return rightHandAnchor.position + simulatedHandOffset; }

        private void UpdateRip()
        {
            if (rightFist == null || !rightFist.IsFistClosed)
            {
                ResetRip();
                return;
            }
#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
                simulatedHandOffset += pullDirection * editorPullSpeed * Time.deltaTime;
#endif
            Vector3 hand = GetRightHand();
            Vector3 motion = (hand - centerEye.position) - startHandRelative;
            float tension = Mathf.Clamp01(motion.magnitude / yankDistance);
            bossWeb.Draw(hand, core.position, 1f, tension, Time.time);
            UpdateCoreIndicator();
            if (motion.magnitude >= yankDistance && Vector3.Dot(motion, pullDirection) >= minBackwardYank)
            {
                WinEncounter();
                return;
            }
            if (Time.time - fistStarted > maxHoldSeconds) ResetRip();
        }

        private void ResetRip()
        {
            bossWeb.HideWeb();
            simulatedHandOffset = Vector3.zero;
            phase = Phase.Exposed;
            Debug.Log("[WS Boss] Core web released — try again", this);
        }

        private void WinEncounter()
        {
            BossDefeated = true;
            phase = Phase.Victory;
            victoryStarted = Time.time;
            bossWeb.HideWeb();
            coreBracket.enabled = false;
            bossWeb.PlayImpact(core.position, centerEye.position - core.position, .5f);
            core.gameObject.SetActive(false);
            Debug.Log("[WS Boss] MOTHER-BUG DEFEATED!", this);
        }

        private void UpdateVictory()
        {
            float t = Mathf.Clamp01((Time.time - victoryStarted) / victorySeconds);
            bossRoot.localScale = Vector3.Lerp(bossOriginalScale, Vector3.zero, t);
            if (t >= 1f) Finish(true);
        }

        private void Finish(bool won)
        {
            if (phase == Phase.Finished) return;
            phase = Phase.Finished;
            bossWeb.HideWeb();
            coreBracket.enabled = false;
            projectile.gameObject.SetActive(false);
            SetShieldVisible(false);
            SetBossVisible(false);
            Debug.Log(won ? "[WS Boss] ENCOUNTER COMPLETE: WIN" : "[WS Boss] ENCOUNTER COMPLETE: TIME UP", this);
            EncounterFinished?.Invoke(won);
            // Leave catch disabled until the production Game Manager chooses the next state.
        }

        private void OnDestroy()
        {
            if (ownsBoss && bossRoot != null) Destroy(bossRoot.gameObject);
            if (projectile != null) Destroy(projectile.gameObject);
            if (combatMaterial != null) Destroy(combatMaterial);
            if (coreMaterial != null) Destroy(coreMaterial);
            if (projectileMaterial != null) Destroy(projectileMaterial);
        }
    }
}
