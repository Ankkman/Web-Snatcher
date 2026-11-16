
using UnityEngine;
using UnityEngine.InputSystem;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class InteractionTestDirector : MonoBehaviour
    {
        private enum TestPhase
        {
            Catch,
            Boss
        }

        [Header("Interaction Systems")]
        [SerializeField] private GazeTargeting gaze;
        [SerializeField] private WebTetherController tether;
        [SerializeField] private SlingshotController slingshot;

        [Header("Test Objects")]
        [SerializeField] private BugTarget testBug;
        [SerializeField] private BossHitTarget testBoss;
        [SerializeField] private Transform headset;

        [Header("Test Timing")]
        [SerializeField, Min(5f)]
        private float catchPhaseDuration = 30f;

        [SerializeField, Min(5f)]
        private float bossPhaseDuration = 30f;

        [SerializeField] private bool autoCycle = true;

        [Header("Test Placement")]
        [SerializeField]
        private bool positionTargetsInFrontOfPlayer = true;

        [SerializeField] private float bugDistance = 1.6f;
        [SerializeField] private float bossDistance = 2.5f;

        [SerializeField]
        private float targetHeightOffset = -0.05f;

        [Header("VR Debug Display")]
        [SerializeField] private bool showDebugDisplay = true;

        private TestPhase currentPhase;
        private float phaseStartedAt;
        private bool started;

        private Vector3 originalBugPosition;
        private Vector3 originalBossPosition;

        private TextMesh debugText;
        private Transform debugRoot;
        private float nextDisplayUpdate;

        private void Awake()
        {
            ResolveReferences();
            CreateDebugDisplay();
        }

        private void Start()
        {
            if (testBug != null)
                originalBugPosition = testBug.transform.position;

            if (testBoss != null)
                originalBossPosition = testBoss.transform.position;

            StartCatchPhase();
        }

        private void ResolveReferences()
        {
            if (gaze == null)
                gaze = FindFirstObjectByType<GazeTargeting>();

            if (tether == null)
                tether =
                    FindFirstObjectByType<WebTetherController>();

            if (slingshot == null)
                slingshot =
                    FindFirstObjectByType<SlingshotController>();

            if (testBug == null)
                testBug = FindFirstObjectByType<BugTarget>();

            if (testBoss == null)
                testBoss = FindFirstObjectByType<BossHitTarget>();

            if (headset == null)
            {
                OVRCameraRig rig =
                    FindFirstObjectByType<OVRCameraRig>();

                if (rig != null)
                    headset = rig.centerEyeAnchor;
            }

            if (headset == null && Camera.main != null)
                headset = Camera.main.transform;

            if (gaze == null || tether == null ||
                slingshot == null || headset == null)
            {
                Debug.LogError(
                    "[WebSnatcher] Test Director: " +
                    "One or more required systems are missing.",
                    this);
            }
        }

        private void CreateDebugDisplay()
        {
            if (!showDebugDisplay || headset == null)
                return;

            GameObject root =
                new GameObject("WS_VR_Test_Display");

            debugRoot = root.transform;
            debugRoot.SetParent(headset, false);

            // Upper left of central view, rather than
            // directly in front of the user's eyes.
            debugRoot.localPosition =
                new Vector3(-0.27f, 0.17f, 0.85f);

            debugRoot.localRotation = Quaternion.identity;

            GameObject textObject =
                new GameObject("Test_Status");

            textObject.transform.SetParent(
                debugRoot, false);

            debugText = textObject.AddComponent<TextMesh>();
            debugText.fontSize = 64;
            debugText.characterSize = 0.005f;
            debugText.anchor = TextAnchor.UpperLeft;
            debugText.alignment = TextAlignment.Left;
            debugText.color = Color.cyan;

            debugText.text = "WEB SNATCHER";
        }

        private Vector3 GetTargetPosition(float distance)
        {
            // Use horizontal headset forward to avoid
            // targets moving onto the floor or ceiling
            // when the player looks up or down.
            Vector3 forward = Vector3.ProjectOnPlane(
                headset.forward, Vector3.up);

            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            forward.Normalize();

            return headset.position +
                   forward * distance +
                   Vector3.up * targetHeightOffset;
        }

        private void ResetTestObjects()
        {
            if (testBug != null)
            {
                testBug.gameObject.SetActive(true);

                testBug.ResetTarget();

                testBug.transform.position =
                    positionTargetsInFrontOfPlayer
                        ? GetTargetPosition(bugDistance)
                        : originalBugPosition;
            }

            if (testBoss != null)
            {
                testBoss.gameObject.SetActive(true);
                testBoss.ResetBoss();

                testBoss.transform.position =
                    positionTargetsInFrontOfPlayer
                        ? GetTargetPosition(bossDistance)
                        : originalBossPosition;
            }
        }

        private void StartCatchPhase()
        {
            currentPhase = TestPhase.Catch;
            phaseStartedAt = Time.time;
            started = true;

            if (slingshot != null)
                slingshot.DeactivateBossPhase();

            // Re-enable the working catch system.
            if (tether != null)
                tether.enabled = true;

            if (gaze != null)
                gaze.SetTargetingEnabled(true);

            ResetTestObjects();

            if (testBoss != null)
                testBoss.gameObject.SetActive(false);

            Debug.Log(
                "[WebSnatcher] TEST PHASE: BUG CATCHING");
        }

        private void StartBossPhase()
        {
            currentPhase = TestPhase.Boss;
            phaseStartedAt = Time.time;

            // Disabling tether cancels any incomplete
            // catch and removes the active web.
            if (tether != null)
                tether.enabled = false;

            if (gaze != null)
                gaze.SetTargetingEnabled(false);

            if (testBug != null)
                testBug.gameObject.SetActive(false);

            if (testBoss != null)
            {
                testBoss.gameObject.SetActive(true);
                testBoss.ResetBoss();

                testBoss.transform.position =
                    positionTargetsInFrontOfPlayer
                        ? GetTargetPosition(bossDistance)
                        : originalBossPosition;
            }

            if (slingshot != null)
                slingshot.ActivateBossPhase();

            Debug.Log(
                "[WebSnatcher] TEST PHASE: BOSS SLINGSHOT");
        }

        private void Update()
        {
            if (!started)
                return;

            float duration =
                currentPhase == TestPhase.Catch
                    ? catchPhaseDuration
                    : bossPhaseDuration;

            float remaining = Mathf.Max(
                0f,
                duration - (Time.time - phaseStartedAt));

            if (Time.time >= nextDisplayUpdate)
            {
                UpdateDebugDisplay(remaining);
                nextDisplayUpdate = Time.time + 0.1f;
            }

#if UNITY_EDITOR
            if (Keyboard.current != null &&
                Keyboard.current.tKey.wasPressedThisFrame)
            {
                SwitchPhase();
                return;
            }
#endif

            if (autoCycle && remaining <= 0f)
                SwitchPhase();
        }

        private void SwitchPhase()
        {
            if (currentPhase == TestPhase.Catch)
                StartBossPhase();
            else
                StartCatchPhase();
        }

        private void UpdateDebugDisplay(float remaining)
        {
            if (debugText == null)
                return;

            string phaseName =
                currentPhase == TestPhase.Catch
                    ? "CATCH BUG"
                    : "BOSS SLINGSHOT";

            string instruction =
                currentPhase == TestPhase.Catch
                    ? "LOOK > FIST > PULL"
                    : "PINCH > DRAW > RELEASE";

            debugText.text =
                "WEB SNATCHER TEST\n" +
                phaseName + "  " +
                Mathf.CeilToInt(remaining) + "s\n" +
                instruction;
        }

        private void OnDestroy()
        {
            if (debugRoot != null)
                Destroy(debugRoot.gameObject);
        }
    }
}
