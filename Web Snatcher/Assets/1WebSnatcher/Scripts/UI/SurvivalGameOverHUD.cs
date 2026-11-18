
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace WebSnatcher
{
    /// <summary>
    /// Headset-relative game-over display.
    /// Reads the existing survival manager and uses
    /// FistGestureInput to restart the current scene.
    /// Does not modify combat or scoring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurvivalGameOverHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SurvivalGameManager game;
        [SerializeField] private FistGestureInput fistInput;
        [SerializeField] private Transform head;

        [Header("Placement")]
        [SerializeField] private Vector3 localOffset =
            new Vector3(0f, -0.02f, 1.4f);

        [SerializeField, Range(0.0007f, 0.002f)]
        private float canvasScale = 0.00135f;

        [Header("Timing")]
        [SerializeField, Min(0.1f)]
        private float fadeInSeconds = 0.65f;

        [SerializeField, Min(0.2f)]
        private float restartInputDelay = 1.0f;

        [Header("Theme")]
        [SerializeField] private Color amber =
            new Color(1f, 0.67f, 0.35f);

        [SerializeField] private Color brightAmber =
            new Color(1f, 0.86f, 0.61f);

        [SerializeField] private Color muted =
            new Color(0.68f, 0.48f, 0.32f);

        [SerializeField] private Color danger =
            new Color(1f, 0.34f, 0.22f);

        [Header("Persistence")]
        [SerializeField] private string bestScoreKey =
            "WebSnatcher_BestScore";

        private GameObject visualRoot;
        private CanvasGroup canvasGroup;
        private Font font;

        private Text titleText;
        private Text scoreText;
        private Text bestText;
        private Text durationText;
        private Text stageText;
        private Text promptText;
        private Text recordText;

        private bool gameSubscribed;
        private bool fistSubscribed;
        private bool showing;
        private bool restarting;

        private float shownAt;
        private int bestScore;

        private void Awake()
        {
            ResolveReferences();

            font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(
                    "Arial", 18);

            BuildInterface();

            bestScore = PlayerPrefs.GetInt(
                bestScoreKey, 0);
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        private void Start()
        {
            // Retry binding if the XR/input objects
            // initialized after this component.
            ResolveReferences();
            Subscribe();

            if (game != null &&
                !game.IsRunning &&
                game.ElapsedSeconds > 0f)
            {
                ShowGameOver();
            }
        }

        private void ResolveReferences()
        {
            if (game == null)
                game = FindFirstObjectByType<
                    SurvivalGameManager>();

            if (fistInput == null)
                fistInput = FindFirstObjectByType<
                    FistGestureInput>();

            if (head == null)
            {
                OVRCameraRig rig =
                    FindFirstObjectByType<OVRCameraRig>();

                if (rig != null)
                    head = rig.centerEyeAnchor;
            }

            if (head == null && Camera.main != null)
                head = Camera.main.transform;
        }

        private void Subscribe()
        {
            if (game != null && !gameSubscribed)
            {
                game.RunEnded += ShowGameOver;
                gameSubscribed = true;
            }

            if (fistInput != null && !fistSubscribed)
            {
                fistInput.FistClosed += HandleFistClosed;
                fistSubscribed = true;
            }
        }

        private void HandleFistClosed()
        {
            if (!showing || restarting)
                return;

            if (Time.unscaledTime - shownAt <
                restartInputDelay)
                return;

            RestartRun();
        }

        private void RestartRun()
        {
            if (restarting || game == null)
                return;

            restarting = true;

            if (canvasGroup != null)
                canvasGroup.alpha = 0f;

            Debug.Log(
                "[WS GameOver] Restart requested",
                this);

            game.RestartScene();
        }

        private void ShowGameOver()
        {
            if (showing || game == null)
                return;

            showing = true;
            shownAt = Time.unscaledTime;

            int score = game.Score;

            if (score > bestScore)
            {
                bestScore = score;

                PlayerPrefs.SetInt(
                    bestScoreKey, bestScore);

                PlayerPrefs.Save();

                recordText.text =
                    "NEW PERSONAL BEST";
            }
            else
            {
                recordText.text =
                    "SURVIVAL RECORD ARCHIVED";
            }

            scoreText.text =
                score.ToString("N0");

            bestText.text =
                bestScore.ToString("N0");

            int totalSeconds = Mathf.FloorToInt(
                game.ElapsedSeconds);

            durationText.text =
                (totalSeconds / 60).ToString("D2") +
                ":" +
                (totalSeconds % 60).ToString("D2");

            stageText.text =
                (game.DifficultyStage + 1).ToString("D2");

            if (visualRoot != null)
                visualRoot.SetActive(true);

            if (canvasGroup != null)
                canvasGroup.alpha = 0f;
        }

        private void Update()
        {
            if (!showing || restarting)
                return;

            float elapsed =
                Time.unscaledTime - shownAt;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Clamp01(
                    elapsed / fadeInSeconds);
            }

            if (promptText != null)
            {
                float pulse =
                    0.75f +
                    Mathf.Sin(
                        Time.unscaledTime * 3f) *
                    0.25f;

                promptText.color =
                    Color.Lerp(muted, amber, pulse);
            }

#if UNITY_EDITOR
            if (Keyboard.current != null &&
                Keyboard.current.enterKey.wasPressedThisFrame &&
                elapsed >= restartInputDelay)
            {
                RestartRun();
            }
#endif
        }

        private void BuildInterface()
        {
            if (head == null)
                return;

            visualRoot = new GameObject(
                "WS_GameOver_Runtime",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasGroup));

            visualRoot.transform.SetParent(head, false);

            visualRoot.transform.localPosition =
                localOffset;

            visualRoot.transform.localRotation =
                Quaternion.identity;

            visualRoot.transform.localScale =
                Vector3.one * canvasScale;

            RectTransform root =
                visualRoot.GetComponent<RectTransform>();

            root.sizeDelta =
                new Vector2(820f, 440f);

            Canvas canvas =
                visualRoot.GetComponent<Canvas>();

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 90;

            canvasGroup =
                visualRoot.GetComponent<CanvasGroup>();

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0f;

            // Minimal amber tactical accents.
            AddLine("UpperLine",
                new Vector2(0f, 165f),
                new Vector2(650f, 2f), muted);

            AddLine("LowerLine",
                new Vector2(0f, -147f),
                new Vector2(650f, 2f), muted);

            AddText("SystemLabel",
                "WS // SURVIVAL PROTOCOL",
                0f, 190f, 500f, 25f,
                17, muted);

            titleText = AddText(
                "Title",
                "SYSTEM OFFLINE",
                0f, 115f, 680f, 62f,
                47, amber);

            AddText("Subtitle",
                "SURVIVAL PROTOCOL TERMINATED",
                0f, 70f, 600f, 28f,
                17, danger);

            scoreText = AddText(
                "FinalScore",
                "000000",
                0f, -1f, 600f, 65f,
                52, brightAmber);

            AddText("ScoreLabel",
                "FINAL SCORE",
                0f, -42f, 300f, 25f,
                17, muted);

            AddText("SurvivedLabel",
                "SURVIVED",
                -235f, -86f, 180f, 25f,
                15, muted);

            AddText("BestLabel",
                "BEST SCORE",
                0f, -86f, 200f, 25f,
                15, muted);

            AddText("StageLabel",
                "THREAT LEVEL",
                235f, -86f, 180f, 25f,
                15, muted);

            durationText = AddText(
                "SurvivalTime",
                "00:00",
                -235f, -113f, 180f, 35f,
                26, amber);

            bestText = AddText(
                "BestScore",
                "0",
                0f, -113f, 200f, 35f,
                26, amber);

            stageText = AddText(
                "ThreatLevel",
                "01",
                235f, -113f, 180f, 35f,
                26, amber);

            promptText = AddText(
                "RestartPrompt",
                "CLOSE RIGHT FIST TO RESTART",
                0f, -175f, 620f, 35f,
                22, amber);

            recordText = AddText(
                "RecordStatus",
                "",
                0f, -204f, 620f, 23f,
                15, muted);

            visualRoot.SetActive(false);
        }

        private Text AddText(
            string objectName,
            string initialText,
            float x, float y,
            float width, float height,
            int fontSize, Color color)
        {
            GameObject obj = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Text));

            obj.transform.SetParent(
                visualRoot.transform, false);

            RectTransform rect =
                obj.GetComponent<RectTransform>();

            rect.anchorMin = rect.anchorMax =
                new Vector2(0.5f, 0.5f);

            rect.anchoredPosition =
                new Vector2(x, y);

            rect.sizeDelta =
                new Vector2(width, height);

            Text text = obj.GetComponent<Text>();

            text.font = font;
            text.text = initialText;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            return text;
        }

        private void AddLine(
            string objectName,
            Vector2 position,
            Vector2 dimensions,
            Color color)
        {
            GameObject obj = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image));

            obj.transform.SetParent(
                visualRoot.transform, false);

            RectTransform rect =
                obj.GetComponent<RectTransform>();

            rect.anchorMin = rect.anchorMax =
                new Vector2(0.5f, 0.5f);

            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            Image image = obj.GetComponent<Image>();

            image.color = color;
            image.raycastTarget = false;
        }

        private void OnDisable()
        {
            if (gameSubscribed && game != null)
            {
                game.RunEnded -= ShowGameOver;
                gameSubscribed = false;
            }

            if (fistSubscribed && fistInput != null)
            {
                fistInput.FistClosed -= HandleFistClosed;
                fistSubscribed = false;
            }
        }

        private void OnDestroy()
        {
            if (visualRoot != null)
                Destroy(visualRoot);
        }
    }
}
