
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class WebSnatcherOnboarding : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SurvivalGameManager game;
        [SerializeField] private FistGestureInput fistInput;
        [SerializeField] private Transform head;

        [Header("Layout")]
        [SerializeField] private Vector3 localOffset =
            new Vector3(0f, -0.025f, 1.4f);

        [SerializeField, Range(0.8f, 1.4f)]
        private float hudScale = 1.15f;

        [Header("Timing")]
        [SerializeField, Min(0.5f)]
        private float bootDuration = 2.7f;

        [SerializeField, Min(1f)]
        private float briefingDuration = 3.5f;

        [SerializeField, Min(1f)]
        private float countdownDuration = 3f;

        [Header("Amber Theme")]
        [SerializeField] private Color amber =
            new Color(1f, 0.55f, 0.19f, 1f);

        [SerializeField] private Color softAmber =
            new Color(1f, 0.78f, 0.48f, 1f);

        [SerializeField] private Color dimAmber =
            new Color(0.52f, 0.32f, 0.17f, 1f);

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        private enum Phase
        {
            Boot,
            Briefing,
            Ready,
            Countdown,
            Complete
        }

        private Phase phase;
        private float phaseStartedAt;

        private GameObject hudRoot;
        private TextMesh header;
        private TextMesh title;
        private TextMesh description;
        private TextMesh footer;

        //private LineRenderer frame;
        private LineRenderer progressLine;
        private Material lineMaterial;

        private bool subscribed;

        private void Start()
        {
            if (game == null)
                game = FindFirstObjectByType<SurvivalGameManager>();

            if (fistInput == null)
                fistInput = FindFirstObjectByType<FistGestureInput>();

            if (head == null)
            {
                OVRCameraRig rig =
                    FindFirstObjectByType<OVRCameraRig>();

                if (rig != null)
                    head = rig.centerEyeAnchor;
            }

            if (head == null && Camera.main != null)
                head = Camera.main.transform;

            if (game == null || head == null)
            {
                Debug.LogError(
                    "[WS Onboarding] Missing game or headset reference.",
                    this
                );
                enabled = false;
                return;
            }

            if (game.IsRunning)
            {
                Debug.LogWarning(
                    "[WS Onboarding] Disable Start Immediately " +
                    "on SurvivalGameManager.", this
                );
                enabled = false;
                return;
            }

            BuildHUD();

            if (fistInput != null)
            {
                fistInput.FistClosed += HandleFist;
                subscribed = true;
            }

            EnterPhase(Phase.Boot);
        }

        private void Update()
        {
            float elapsed =
                Time.unscaledTime - phaseStartedAt;

            switch (phase)
            {
                case Phase.Boot:
                    UpdateProgress(
                        Mathf.Clamp01(elapsed / bootDuration)
                    );

                    if (elapsed >= bootDuration)
                        EnterPhase(Phase.Briefing);

                    break;

                case Phase.Briefing:
                    UpdateProgress(
                        Mathf.Clamp01(
                            elapsed / briefingDuration
                        )
                    );

                    if (elapsed >= briefingDuration)
                        EnterPhase(Phase.Ready);

                    break;

                case Phase.Ready:
                    UpdateProgress(1f);

                    // A subtle pulse, not a strong flashing effect.
                    float pulse = 0.78f +
                        Mathf.Sin(Time.unscaledTime * 3f) * 0.22f;

                    footer.color =
                        Color.Lerp(dimAmber, softAmber, pulse);

#if UNITY_EDITOR
                    if (Keyboard.current != null &&
                        Keyboard.current.enterKey.wasPressedThisFrame)
                    {
                        BeginCountdown();
                    }
#endif
                    break;

                case Phase.Countdown:
                    float remaining =
                        countdownDuration - elapsed;

                    int number = Mathf.Max(
                        1, Mathf.CeilToInt(remaining)
                    );

                    title.text = number.ToString();
                    description.text =
                        "SURVIVAL SYSTEM ENGAGING";

                    UpdateProgress(
                        Mathf.Clamp01(
                            elapsed / countdownDuration
                        )
                    );

                    if (remaining <= 0f)
                        CompleteOnboarding();

                    break;
            }
        }

        private void HandleFist()
        {
            if (phase == Phase.Ready)
                BeginCountdown();
        }

        private void BeginCountdown()
        {
            EnterPhase(Phase.Countdown);

            if (logEvents)
                Debug.Log(
                    "[WS Onboarding] Countdown started",
                    this
                );
        }

        private void CompleteOnboarding()
        {
            if (phase == Phase.Complete)
                return;

            phase = Phase.Complete;

            if (hudRoot != null)
                hudRoot.SetActive(false);

            game.BeginRun();

            if (logEvents)
                Debug.Log(
                    "[WS Onboarding] Survival started",
                    this
                );

            enabled = false;
        }

        private void EnterPhase(Phase next)
        {
            phase = next;
            phaseStartedAt = Time.unscaledTime;

            if (header == null)
                return;

            switch (next)
            {
                case Phase.Boot:
                    header.text = "WS // SYSTEM BOOT";
                    title.text = "WEB SNATCHER";
                    description.text =
                        "CALIBRATING TACTICAL VISION";
                    footer.text =
                        "ENVIRONMENT LINK INITIALIZING";
                    break;

                case Phase.Briefing:
                    header.text = "WS // FIELD BRIEFING";
                    title.text = "TACTICAL PROTOCOL";
                    description.text =
                        "RIGHT FIST  /  WEB SNATCH\n" +
                        "LEFT PALM   /  WEB SHIELD\n" +
                        "5 CAPTURES  /  OVERLOAD";
                    footer.text =
                        "PREPARING SURVIVAL SYSTEM";
                    break;

                case Phase.Ready:
                    header.text = "WS // READY";
                    title.text = "SYSTEM ONLINE";
                    description.text =
                        "DEFEND YOUR SPACE\n" +
                        "CAPTURE SCOUTS. STOP STRIKERS.";
                    footer.text =
                        "CLOSE RIGHT FIST TO BEGIN";
                    break;

                case Phase.Countdown:
                    header.text = "WS // ENGAGING";
                    title.text = "3";
                    description.text =
                        "SURVIVAL SYSTEM ENGAGING";
                    footer.text = "STAY ALERT";
                    break;
            }
        }

        private void BuildHUD()
        {
            hudRoot = new GameObject(
                "WS_Onboarding_TacticalHUD"
            );

            hudRoot.transform.SetParent(head, false);
            hudRoot.transform.localPosition = localOffset;
            hudRoot.transform.localRotation =
                Quaternion.identity;

            hudRoot.transform.localScale =
                Vector3.one * hudScale;

            header = CreateText(
                "Header",
                new Vector3(-0.28f, 0.145f, 0f),
                0.0030f,
                softAmber,
                TextAnchor.MiddleLeft
            );

            title = CreateText(
                "Title",
                new Vector3(0f, 0.050f, 0f),
                0.0063f,
                amber,
                TextAnchor.MiddleCenter
            );

            description = CreateText(
                "Description",
                new Vector3(0f, -0.045f, 0f),
                0.0035f,
                softAmber,
                TextAnchor.MiddleCenter
            );

            footer = CreateText(
                "Footer",
                new Vector3(0f, -0.165f, 0f),
                0.0032f,
                amber,
                TextAnchor.MiddleCenter
            );

            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit"
            );

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader != null)
            {
                lineMaterial = new Material(shader);
                lineMaterial.name =
                    "WS_Onboarding_AmberLines";

                if (lineMaterial.HasProperty("_BaseColor"))
                    lineMaterial.SetColor(
                        "_BaseColor", amber
                    );
                else if (lineMaterial.HasProperty("_Color"))
                    lineMaterial.SetColor(
                        "_Color", amber
                    );
            }

            // frame = CreateLine("CornerFrame", 16, 0.0017f);

            // // Four separate L-shaped corners.
            // Vector3[] corners =
            // {
            //     new Vector3(-0.34f,  0.12f, 0f),
            //     new Vector3(-0.34f,  0.19f, 0f),
            //     new Vector3(-0.26f,  0.19f, 0f),
            //     new Vector3(-0.26f,  0.19f, 0f),

            //     new Vector3( 0.26f,  0.19f, 0f),
            //     new Vector3( 0.34f,  0.19f, 0f),
            //     new Vector3( 0.34f,  0.12f, 0f),
            //     new Vector3( 0.34f,  0.12f, 0f),

            //     new Vector3( 0.34f, -0.12f, 0f),
            //     new Vector3( 0.34f, -0.19f, 0f),
            //     new Vector3( 0.26f, -0.19f, 0f),
            //     new Vector3( 0.26f, -0.19f, 0f),

            //     new Vector3(-0.26f, -0.19f, 0f),
            //     new Vector3(-0.34f, -0.19f, 0f),
            //     new Vector3(-0.34f, -0.12f, 0f),
            //     new Vector3(-0.34f, -0.12f, 0f)
            // };

            // // Avoid connected diagonal lines:
            // // Render corners as four separate line objects.
            // Destroy(frame.gameObject);
            // frame = null;

            // for (int c = 0; c < 4; c++)
            // {
            //     LineRenderer corner =
            //         CreateLine("Corner_" + c, 3, 0.0017f);

            //     for (int p = 0; p < 3; p++)
            //     {
            //         corner.SetPosition(
            //             p, corners[c * 4 + p]
            //         );
            //     }
            // }

            progressLine =
                CreateLine("BootProgress", 2, 0.003f);

            UpdateProgress(0f);
        }

        private TextMesh CreateText(
            string objectName,
            Vector3 localPosition,
            float characterSize,
            Color color,
            TextAnchor anchor)
        {
            GameObject obj = new GameObject(objectName);
            obj.transform.SetParent(
                hudRoot.transform, false
            );

            obj.transform.localPosition = localPosition;

            TextMesh mesh = obj.AddComponent<TextMesh>();
            mesh.fontSize = 64;
            mesh.characterSize = characterSize;
            mesh.anchor = anchor;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;

            return mesh;
        }

        private LineRenderer CreateLine(
            string objectName,
            int positions,
            float width)
        {
            GameObject obj = new GameObject(objectName);
            obj.transform.SetParent(
                hudRoot.transform, false
            );

            LineRenderer lr =
                obj.AddComponent<LineRenderer>();

            lr.sharedMaterial = lineMaterial;
            lr.useWorldSpace = false;
            lr.positionCount = positions;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode =
                ShadowCastingMode.Off;

            lr.receiveShadows = false;
            return lr;
        }

        private void UpdateProgress(float value)
        {
            if (progressLine == null)
                return;

            progressLine.SetPosition(
                0, new Vector3(-0.32f, -0.12f, 0f)
            );

            progressLine.SetPosition(
                1,
                new Vector3(
                    Mathf.Lerp(-0.32f, 0.32f, value),
                    -0.12f,
                    0f
                )
            );
        }

        private void OnDestroy()
        {
            if (subscribed && fistInput != null)
                fistInput.FistClosed -= HandleFist;

            if (hudRoot != null)
                Destroy(hudRoot);

            if (lineMaterial != null)
                Destroy(lineMaterial);
        }
    }
}
