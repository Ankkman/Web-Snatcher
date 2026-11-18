
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class SurvivalStatusHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SurvivalGameManager game;
        [SerializeField] private SurvivalOverloadController overload;
        [SerializeField] private Transform head;

        [Header("Headset Placement")]
        [SerializeField] private Vector3 localOffset =
            new Vector3(0f, -0.025f, 1.4f);
        [SerializeField, Range(0.0006f, 0.0014f)]
        private float canvasScale = 0.001f;

        [Header("Visual Theme")]
        [SerializeField] private Color amber =
            new Color(1f, 0.64f, 0.31f, 1f);
        [SerializeField] private Color muted =
            new Color(0.64f, 0.47f, 0.32f, 0.85f);
        [SerializeField] private Color danger =
            new Color(1f, 0.30f, 0.20f, 1f);

        [Header("Threat Detection")]
        [SerializeField, Range(35f, 90f)]
        private float offscreenAngle = 58f;
        [SerializeField, Range(95f, 175f)]
        private float rearAngle = 125f;

        [Header("Refresh")]
        [SerializeField, Range(0.05f, 0.3f)]
        private float refreshInterval = 0.1f;

        private GameObject visualRoot;
        private CanvasGroup canvasGroup;
        private Font uiFont;

        private Text scoreValue;
        private Text comboValue;
        private Text timeValue;
        private Text healthLabel;
        private Text chargeLabel;
        private Text threatLabel;
        private Text gameOverLabel;

        private readonly List<Image> healthPips =
            new List<Image>();
        private readonly List<Image> chargePips =
            new List<Image>();

        private float nextRefresh;
        private int maximumHealth = 5;
        private bool hasRunStarted;

        private void Start()
        {
            if (game == null)
                game =
                    FindFirstObjectByType<SurvivalGameManager>();

            if (overload == null)
                overload =
                    FindFirstObjectByType<SurvivalOverloadController>();

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
                    "[WS HUD] Missing game or headset.", this);
                enabled = false;
                return;
            }

            maximumHealth = Mathf.Max(1, game.Health);
            uiFont = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

            if (uiFont == null)
                uiFont = Font.CreateDynamicFontFromOSFont(
                    "Arial", 18);

            BuildInterface();
            RefreshDisplay();
        }

        private void Update()
        {
            if (game == null || visualRoot == null)
                return;

            if (Time.unscaledTime < nextRefresh)
                return;

            nextRefresh =
                Time.unscaledTime + refreshInterval;

            RefreshDisplay();
        }

        private void BuildInterface()
        {
            visualRoot =
                new GameObject("WS_TacticalHUD_Runtime",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasGroup));

            visualRoot.transform.SetParent(head, false);
            visualRoot.transform.localPosition = localOffset;
            visualRoot.transform.localRotation =
                Quaternion.identity;
            visualRoot.transform.localScale =
                Vector3.one * canvasScale;

            RectTransform rootRect =
                visualRoot.GetComponent<RectTransform>();

            rootRect.sizeDelta =
                new Vector2(1080f, 620f);

            Canvas canvas =
                visualRoot.GetComponent<Canvas>();

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 50;

            canvasGroup =
                visualRoot.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            // Minimal peripheral decorative brackets.
            AddRect("TopLeftHorizontal",
                -496, 275, 64, 2, muted);
            AddRect("TopLeftVertical",
                -528, 257, 2, 38, muted);
            AddRect("TopRightHorizontal",
                496, 275, 64, 2, muted);
            AddRect("TopRightVertical",
                528, 257, 2, 38, muted);

            AddRect("BottomLeftHorizontal",
                -496, -275, 64, 2, muted);
            AddRect("BottomLeftVertical",
                -528, -257, 2, 38, muted);
            AddRect("BottomRightHorizontal",
                496, -275, 64, 2, muted);
            AddRect("BottomRightVertical",
                528, -257, 2, 38, muted);

            // Score / upper left.
            MakeText("ScoreLabel", "SCORE // TOTAL",
                -388, 246, 240, 32, 17,
                muted, TextAnchor.MiddleLeft);

            scoreValue = MakeText("ScoreValue", "000000",
                -388, 209, 260, 52, 37,
                amber, TextAnchor.MiddleLeft);

            AddRect("ScoreUnderline",
                -399, 177, 218, 2, muted);

            // Combo / upper right.
            MakeText("ComboLabel", "COMBO MULTIPLIER",
                382, 246, 260, 30, 17,
                muted, TextAnchor.MiddleRight);

            comboValue = MakeText("ComboValue", "x01",
                393, 209, 240, 52, 36,
                amber, TextAnchor.MiddleRight);

            // Elapsed survival time / upper center.
            MakeText("TimeLabel", "SURVIVAL TIME",
                0, 257, 200, 26, 15,
                muted, TextAnchor.MiddleCenter);

            timeValue = MakeText("TimeValue", "00:00",
                0, 225, 190, 45, 29,
                amber, TextAnchor.MiddleCenter);

            AddRect("TimeUnderline",
                0, 198, 100, 2, muted);

            // Lower left: player integrity.
            healthLabel = MakeText("HealthLabel",
                "INTEGRITY 05 / 05",
                -382, -205, 275, 30, 17,
                muted, TextAnchor.MiddleLeft);

            BuildHealthPips();

            // Lower right: web overload.
            chargeLabel = MakeText("ChargeLabel",
                "WEB OVERLOAD 0 / 5",
                370, -205, 275, 30, 17,
                muted, TextAnchor.MiddleRight);

            BuildChargePips();

            // Directional Striker awareness.
            threatLabel = MakeText("ThreatDirection", "",
                0, 140, 440, 42, 21,
                danger, TextAnchor.MiddleCenter);

            threatLabel.gameObject.SetActive(false);

            // Simple temporary game-over readout.
            gameOverLabel = MakeText("GameOver", "",
                0, 0, 700, 140, 31,
                danger, TextAnchor.MiddleCenter);

            gameOverLabel.gameObject.SetActive(false);
        }

        private void BuildHealthPips()
        {
            for (int i = 0; i < maximumHealth; i++)
            {
                Image pip = AddRect(
                    "Integrity_" + i,
                    -500f + i * 40f,
                    -246f,
                    31f,
                    11f,
                    amber);

                healthPips.Add(pip);
            }
        }

        private void BuildChargePips()
        {
            int count = overload != null
                ? Mathf.Max(1, overload.RequiredCatches)
                : 5;

            for (int i = 0; i < count; i++)
            {
                Image pip = AddRect(
                    "Overload_" + i,
                    318f + i * 40f,
                    -246f,
                    31f,
                    11f,
                    muted * 0.35f);

                chargePips.Add(pip);
            }
        }

        private void RefreshDisplay()
        {
            if (game.IsRunning && !hasRunStarted)
            {
                int initialHealth = Mathf.Max(1, game.Health);

                if (initialHealth != maximumHealth)
                {
                    foreach (Image pip in healthPips)
                        Destroy(pip.gameObject);

                    healthPips.Clear();
                    maximumHealth = initialHealth;
                    BuildHealthPips();
                }

                hasRunStarted = true;
            }

            // Hide during boot / onboarding, not during game over.
            if (!hasRunStarted && !game.IsRunning)
            {
                canvasGroup.alpha = 0f;
                return;
            }

            canvasGroup.alpha = 1f;

            scoreValue.text =
                game.Score.ToString("D6");

            comboValue.text =
                "x" + game.CurrentMultiplier.ToString("D2");

            int totalSeconds =
                Mathf.FloorToInt(game.ElapsedSeconds);

            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            timeValue.text =
                minutes.ToString("D2") + ":" +
                seconds.ToString("D2");

            healthLabel.text =
                "INTEGRITY " +
                game.Health.ToString("D2") + " / " +
                maximumHealth.ToString("D2");

            for (int i = 0; i < healthPips.Count; i++)
            {
                healthPips[i].color =
                    i < game.Health
                        ? (game.Health <= 2 ? danger : amber)
                        : muted * 0.20f;
            }

            int progress =
                overload != null ? overload.Progress : 0;

            int required =
                overload != null ? overload.RequiredCatches : 5;

            bool charged =
                overload != null && overload.IsCharged;

            chargeLabel.text = charged
                ? "WEB OVERLOAD // READY"
                : "WEB OVERLOAD " +
                  progress + " / " + required;

            chargeLabel.color =
                charged ? amber : muted;

            float pulse =
                0.75f + 0.25f *
                Mathf.Sin(Time.unscaledTime * 5f);

            for (int i = 0; i < chargePips.Count; i++)
            {
                if (i < progress)
                    chargePips[i].color =
                        charged
                            ? Color.Lerp(muted, amber, pulse)
                            : amber;
                else
                    chargePips[i].color =
                        muted * 0.20f;
            }

            if (!game.IsRunning)
            {
                threatLabel.gameObject.SetActive(false);
                gameOverLabel.gameObject.SetActive(false);
                canvasGroup.alpha = 0f;
                return;
            }
            else
            {
                gameOverLabel.gameObject.SetActive(false);
                RefreshThreatDirection();
            }
        }

        private void RefreshThreatDirection()
        {
            GameObject striker = game.ActiveStriker;

            if (striker == null || !striker.activeInHierarchy)
            {
                threatLabel.gameObject.SetActive(false);
                return;
            }

            Vector3 direction =
                striker.transform.position - head.position;

            // Horizontal bearing keeps warnings stable
            // even when the enemy changes altitude.
            direction.y = 0f;

            Vector3 forward = head.forward;
            forward.y = 0f;

            if (direction.sqrMagnitude < 0.0001f ||
                forward.sqrMagnitude < 0.0001f)
            {
                threatLabel.gameObject.SetActive(false);
                return;
            }

            float bearing = Vector3.SignedAngle(
                forward.normalized,
                direction.normalized,
                Vector3.up);

            float absAngle = Mathf.Abs(bearing);

            if (absAngle < offscreenAngle)
            {
                threatLabel.gameObject.SetActive(false);
                return;
            }

            bool right = bearing > 0f;

            threatLabel.gameObject.SetActive(true);

            if (absAngle >= rearAngle)
            {
                threatLabel.text =
                    right
                    ? "STRIKER // REAR RIGHT"
                    : "STRIKER // REAR LEFT";

                threatLabel.color = danger;
            }
            else
            {
                threatLabel.text =
                    right
                    ? "STRIKER  >>  RIGHT"
                    : "STRIKER  <<  LEFT";

                threatLabel.color = amber;
            }

            float warningPulse =
                0.7f + 0.3f *
                Mathf.Sin(Time.unscaledTime * 4f);

            Color color = threatLabel.color;
            color.a = warningPulse;
            threatLabel.color = color;
        }

        private Text MakeText(
            string name,
            string initial,
            float x,
            float y,
            float width,
            float height,
            int fontSize,
            Color color,
            TextAnchor alignment)
        {
            GameObject obj =
                new GameObject(name,
                    typeof(RectTransform),
                    typeof(Text));

            obj.transform.SetParent(
                visualRoot.transform, false);

            RectTransform rt =
                obj.GetComponent<RectTransform>();

            rt.anchorMin = rt.anchorMax =
                new Vector2(0.5f, 0.5f);

            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta =
                new Vector2(width, height);

            Text text = obj.GetComponent<Text>();

            text.font = uiFont;
            text.fontSize = fontSize;
            text.text = initial;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow =
                HorizontalWrapMode.Overflow;
            text.verticalOverflow =
                VerticalWrapMode.Overflow;

            return text;
        }

        private Image AddRect(
            string name,
            float x,
            float y,
            float width,
            float height,
            Color color)
        {
            GameObject obj =
                new GameObject(name,
                    typeof(RectTransform),
                    typeof(Image));

            obj.transform.SetParent(
                visualRoot.transform, false);

            RectTransform rt =
                obj.GetComponent<RectTransform>();

            rt.anchorMin = rt.anchorMax =
                new Vector2(0.5f, 0.5f);

            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta =
                new Vector2(width, height);

            Image image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            return image;
        }

        private void OnDestroy()
        {
            if (visualRoot != null)
                Destroy(visualRoot);
        }
    }
}
