using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Web Overload: right-index pinch grabs a core between tracked hands,
    /// right-hand backward draw charges it, release launches a swept-hit web bomb.
    /// Keeps the SlingshotController API used by InteractionTestDirector.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    public sealed class SlingshotController : MonoBehaviour
    {
        private enum Mode { Inactive, Ready, Drawing, Recovering }

        [Header("Auto-resolved Rig")]
        [SerializeField] private Transform headset;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private OVRHand leftOVRHand;
        [SerializeField] private OVRHand rightOVRHand;

        [Header("Target (optional for aim assist)")]
        [SerializeField] private BossHitTarget boss;
        [SerializeField, Range(0f, 20f)] private float maxAssistAngle = 11f;
        [SerializeField, Range(0f, 1f)] private float assistBlend = 0.6f;

        [Header("Draw and Fire")]
        [SerializeField, Min(0.03f)] private float minimumDraw = 0.09f;
        [SerializeField, Min(0.1f)] private float maximumDraw = 0.38f;
        [SerializeField, Min(0.1f)] private float minimumSpeed = 7f;
        [SerializeField, Min(0.1f)] private float maximumSpeed = 17f;
        [SerializeField, Min(0.05f)] private float recoverySeconds = 0.28f;
        [SerializeField, Min(0f)] private float minimumBackwardFraction = 0.55f;
        [SerializeField, Min(0.02f)] private float coreGrabRadius = 0.26f;
        [SerializeField, Min(0f)] private float muzzleForwardOffset = 0.12f;
        [SerializeField] private bool requireGrabNearCore = true;

        [Header("Web Graphics")]
        [SerializeField] private Color silkColor = new Color(0.94f, 0.90f, 0.82f, 1f);
        [SerializeField] private Color chargeColor = new Color(0.91f, 0.40f, 0.25f, 1f);
        [SerializeField] private float mainStrandWidth = 0.005f;
        [SerializeField] private float supportingStrandWidth = 0.0018f;
        [SerializeField] private float coreSize = 0.065f;

        [Header("Editor Debug")]
        [SerializeField] private bool editorKeyboardSimulation = true;
        [SerializeField] private bool editorBHotkeyWithoutTestDirector = false;
        [SerializeField, Min(0.05f)] private float editorPullSpeed = 0.65f;
        [SerializeField] private bool logStateChanges = true;

        public bool IsBossPhaseActive => mode != Mode.Inactive;
        public bool IsDrawing => mode == Mode.Drawing;
        public float NormalizedCharge { get; private set; }

        private Mode mode = Mode.Inactive;
        private LineRenderer leftMain, rightMain, leftFine, rightFine, chargeRing;
        private Material silkMat, chargeMat, coreMat;
        private Transform core;
        private Vector3 restAtGrab;
        private Vector3 rightAtGrab;
        private Vector3 leftAtGrab;
        private Vector3 virtualPull;
        private Vector3 drawPosition;
        private bool priorPinch;
        private float recoveryUntil;
        private readonly Vector3[] ringPoints = new Vector3[33];
        private bool warnedMissing;

        private void Awake()
        {
            FindReferences();
            CreateVisuals();
            SetVisuals(false);
        }

        private void FindReferences()
        {
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (headset == null) headset = rig.centerEyeAnchor;
                if (leftHand == null) leftHand = rig.leftHandAnchor;
                if (rightHand == null) rightHand = rig.rightHandAnchor;
            }
            if (headset == null && Camera.main != null) headset = Camera.main.transform;
            if (leftOVRHand == null || rightOVRHand == null)
            {
                OVRHand[] hands = FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (OVRHand h in hands)
                {
                    if (h.GetHand() == OVRPlugin.Hand.HandLeft && leftOVRHand == null) leftOVRHand = h;
                    if (h.GetHand() == OVRPlugin.Hand.HandRight && rightOVRHand == null) rightOVRHand = h;
                }

            }
            if (boss == null) boss = FindFirstObjectByType<BossHitTarget>();
        }

        private static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError("[WebSnatcher] Missing URP Unlit shader. Keep the URP shader included in the Quest build.");
                return null;
            }
            Material m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private LineRenderer CreateLine(string name, Material mat, float width, int points)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            LineRenderer l = go.AddComponent<LineRenderer>();
            l.useWorldSpace = true;
            l.sharedMaterial = mat;
            l.positionCount = points;
            l.widthMultiplier = width;
            l.numCapVertices = 3;
            l.shadowCastingMode = ShadowCastingMode.Off;
            l.receiveShadows = false;
            return l;
        }

        private void CreateVisuals()
        {
            silkMat = MakeMaterial("WS_Overload_Silk", silkColor);
            chargeMat = MakeMaterial("WS_Overload_Charge", chargeColor);
            coreMat = MakeMaterial("WS_Overload_Core", silkColor);

            leftMain = CreateLine("Overload_LeftWeb", silkMat, mainStrandWidth, 9);
            rightMain = CreateLine("Overload_RightWeb", silkMat, mainStrandWidth, 9);
            leftFine = CreateLine("Overload_LeftFilament", chargeMat, supportingStrandWidth, 9);
            rightFine = CreateLine("Overload_RightFilament", chargeMat, supportingStrandWidth, 9);
            chargeRing = CreateLine("Overload_ChargeHalo", chargeMat, 0.002f, ringPoints.Length);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Overload_WebCore";
            visual.transform.SetParent(transform, false);
            visual.transform.localScale = Vector3.one * coreSize;
            Collider c = visual.GetComponent<Collider>();
            if (c != null) Destroy(c);
            visual.GetComponent<Renderer>().sharedMaterial = coreMat;
            core = visual.transform;
        }

        public void ActivateBossPhase()
        {
            if (headset == null || leftHand == null || rightHand == null) FindReferences();
            mode = Mode.Ready;
            priorPinch = false;
            virtualPull = Vector3.zero;
            NormalizedCharge = 0;
            SetVisuals(true);
            if (logStateChanges) Debug.Log("[WebSnatcher] WEB OVERLOAD READY", this);
        }

        public void DeactivateBossPhase()
        {
            mode = Mode.Inactive;
            priorPinch = false;
            virtualPull = Vector3.zero;
            NormalizedCharge = 0;
            SetVisuals(false);
        }

        private void OnDisable() => DeactivateBossPhase();

        private bool ValidHands()
        {
#if UNITY_EDITOR
            if (editorKeyboardSimulation && Keyboard.current != null && Keyboard.current.gKey.isPressed)
                return true;
#endif
            return leftOVRHand != null && rightOVRHand != null &&
                   leftOVRHand.IsTracked && leftOVRHand.IsDataValid &&
                   rightOVRHand.IsTracked && rightOVRHand.IsDataValid;
        }

        private bool RightPinching()
        {
            bool pinching = rightOVRHand != null && rightOVRHand.IsTracked &&
                            rightOVRHand.IsDataValid &&
                            !rightOVRHand.IsSystemGestureInProgress &&
                            rightOVRHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
#if UNITY_EDITOR
            if (editorKeyboardSimulation && Keyboard.current != null)
                pinching |= Keyboard.current.gKey.isPressed;
#endif
            return pinching;
        }

        private Vector3 RestPosition() => (leftHand.position + rightHand.position) * 0.5f;

        private void Update()
        {
#if UNITY_EDITOR
            // B toggles only when not controlled by the test director.
            if (editorKeyboardSimulation && Keyboard.current != null &&
                Keyboard.current.bKey.wasPressedThisFrame &&
                editorBHotkeyWithoutTestDirector)
            {
                if (IsBossPhaseActive) DeactivateBossPhase();
                else ActivateBossPhase();
            }
#endif
            if (mode == Mode.Inactive) return;
            if (headset == null || leftHand == null || rightHand == null)
            {
                if (!warnedMissing) { Debug.LogWarning("[WebSnatcher] Assign headset + two hand anchors.", this); warnedMissing = true; }
                return;
            }

            bool tracked = ValidHands();
            bool pinch = tracked && RightPinching();
            if (!tracked && mode == Mode.Drawing)
            {
                CancelDraw(); // never fire on tracking loss
                priorPinch = false;
                UpdateGraphics(RestPosition(), 0f);
                return;
            }

            if (mode == Mode.Recovering && Time.time >= recoveryUntil)
                mode = Mode.Ready;

            if (mode == Mode.Ready && pinch && !priorPinch && tracked)
                TryBeginDraw();
            else if (mode == Mode.Drawing)
            {
                if (pinch) UpdateDraw();
                else if (priorPinch && tracked) FireOrCancel();
            }

            priorPinch = pinch;
            Vector3 pos = mode == Mode.Drawing ? drawPosition : RestPosition();
            UpdateGraphics(pos, NormalizedCharge);
        }

        private void TryBeginDraw()
        {
            Vector3 rest = RestPosition();
            if (requireGrabNearCore && Vector3.Distance(rightHand.position, rest) > coreGrabRadius)
            {
                if (logStateChanges) Debug.Log("[WebSnatcher] Move hands a little closer to load web core.", this);
                return;
            }
            mode = Mode.Drawing;
            restAtGrab = rest;
            rightAtGrab = rightHand.position;
            leftAtGrab = leftHand.position;
            drawPosition = rest;
            virtualPull = Vector3.zero;
            NormalizedCharge = 0f;
            if (logStateChanges) Debug.Log("[WebSnatcher] WEB CORE GRABBED", this);
        }

        private void UpdateDraw()
        {
#if UNITY_EDITOR
            if (editorKeyboardSimulation && Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
                virtualPull -= headset.forward * (editorPullSpeed * Time.deltaTime);
#endif
            // Track how the right hand moves RELATIVE to a moving two-hand midpoint.
            Vector3 restNow = RestPosition();
            // Measure full right-hand travel relative to the supporting left hand.
            Vector3 relativeDelta = (rightHand.position - rightAtGrab) - (leftHand.position - leftAtGrab);
            Vector3 requested = relativeDelta + virtualPull;

            // Only backward draw charges the weapon. Keep sideways motion for a little natural give.
            float back = Mathf.Clamp(Vector3.Dot(requested, -headset.forward), 0f, maximumDraw);
            Vector3 sideways = Vector3.ProjectOnPlane(requested, headset.forward);
            sideways = Vector3.ClampMagnitude(sideways, maximumDraw * 0.22f);
            drawPosition = restNow - headset.forward * back + sideways;
            NormalizedCharge = Mathf.Clamp01(back / maximumDraw);
        }

        private void FireOrCancel()
        {
            float backwards = Vector3.Dot(RestPosition() - drawPosition, headset.forward);
            if (backwards < minimumDraw || NormalizedCharge < minimumBackwardFraction * minimumDraw / maximumDraw)
            {
                CancelDraw();
                return;
            }

            float charge = NormalizedCharge;
            Vector3 direction = headset.forward;
            Vector3 spawn = RestPosition() + direction * muzzleForwardOffset;

            if (boss == null || !boss.isActiveAndEnabled) boss = FindFirstObjectByType<BossHitTarget>();
            if (boss != null && !boss.IsDefeated)
            {
                Collider bossCollider = boss.GetComponentInChildren<Collider>();
                Vector3 target = bossCollider != null ? bossCollider.bounds.center : boss.transform.position;
                Vector3 towardBoss = target - spawn;
                if (towardBoss.sqrMagnitude > 0.0001f &&
                    Vector3.Angle(direction, towardBoss) <= maxAssistAngle)
                    direction = Vector3.Slerp(direction, towardBoss.normalized, assistBlend).normalized;
            }

            float speed = Mathf.Lerp(minimumSpeed, maximumSpeed, charge);
            WebOverloadProjectile.LaunchNew(spawn, direction * speed, boss, charge, silkColor, chargeColor);
            if (logStateChanges) Debug.Log($"[WebSnatcher] WEB OVERLOAD FIRED - charge {charge:P0}", this);

            mode = Mode.Recovering;
            recoveryUntil = Time.time + recoverySeconds;
            virtualPull = Vector3.zero;
            NormalizedCharge = 0;
        }

        private void CancelDraw()
        {
            mode = Mode.Ready;
            virtualPull = Vector3.zero;
            NormalizedCharge = 0;
        }

        private void UpdateGraphics(Vector3 orbPosition, float charge)
        {
            if (core == null) return;
            core.position = orbPosition;
            core.localScale = Vector3.one * coreSize * (1f + 0.4f * charge + 0.045f * Mathf.Sin(Time.time * (5f + 16f * charge)));
            DrawArm(leftMain, leftHand.position, orbPosition, 0f, charge);
            DrawArm(rightMain, rightHand.position, orbPosition, 0f, charge);
            DrawArm(leftFine, leftHand.position, orbPosition, 0.012f, charge);
            DrawArm(rightFine, rightHand.position, orbPosition, -0.012f, charge);

            // Small halo around charged web core, billboarded toward player.
            chargeRing.enabled = charge > 0.12f;
            if (chargeRing.enabled)
            {
                Vector3 towardEye = headset.position - orbPosition;
                if (towardEye.sqrMagnitude < 0.001f) towardEye = -headset.forward;
                Quaternion q = Quaternion.LookRotation(towardEye, Vector3.up);
                float radius = coreSize * (0.7f + charge * 0.9f);
                for (int i = 0; i < ringPoints.Length; i++)
                {
                    float a = i * Mathf.PI * 2f / (ringPoints.Length - 1);
                    ringPoints[i] = orbPosition + q * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                }
                chargeRing.SetPositions(ringPoints);
            }
        }

        private void DrawArm(LineRenderer line, Vector3 start, Vector3 end, float spread, float charge)
        {
            if (line == null) return;
            Vector3 direction = end - start;
            Vector3 side = Vector3.Cross(direction, headset.forward).normalized;
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f;
                Vector3 point = Vector3.Lerp(start, end, t);
                float arch = Mathf.Sin(t * Mathf.PI);
                point += Vector3.down * (0.025f * (1f - charge) * arch);
                point += side * spread * arch;
                line.SetPosition(i, point);
            }
            line.widthMultiplier = (line == leftMain || line == rightMain ? mainStrandWidth : supportingStrandWidth) * (1f + charge * 0.8f);
        }

        private void SetVisuals(bool show)
        {
            if (leftMain != null) leftMain.enabled = show;
            if (rightMain != null) rightMain.enabled = show;
            if (leftFine != null) leftFine.enabled = show;
            if (rightFine != null) rightFine.enabled = show;
            if (chargeRing != null) chargeRing.enabled = false;
            if (core != null) core.gameObject.SetActive(show);
        }

        private void OnDestroy()
        {
            if (silkMat != null) Destroy(silkMat);
            if (chargeMat != null) Destroy(chargeMat);
            if (coreMat != null) Destroy(coreMat);
        }
    }
}
