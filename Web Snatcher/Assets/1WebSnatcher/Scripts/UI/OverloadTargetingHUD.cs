
using UnityEngine;
using UnityEngine.UI;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class OverloadTargetingHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SurvivalGameManager game;
        [SerializeField] private SurvivalOverloadController overload;
        [SerializeField] private Transform head;

        [Header("Placement")]
        [SerializeField] private float hudDistance = 1.4f;
        [SerializeField] private float canvasScale = 0.001f;

        [Header("Target Brackets")]
        [SerializeField] private float bracketSize = 86f;
        [SerializeField] private float bracketLength = 16f;
        [SerializeField] private float bracketThickness = 2f;
        [SerializeField] private float verticalOffset = 0f;

        [Header("Colors")]
        [SerializeField] private Color amber =
            new Color(1f, 0.62f, 0.27f, 1f);

        [SerializeField] private Color brightAmber =
            new Color(1f, 0.88f, 0.56f, 1f);

        [SerializeField] private Color missRed =
            new Color(1f, 0.28f, 0.18f, 1f);

        [Header("Feedback")]
        [SerializeField] private float missFlashSeconds = 0.55f;
        [SerializeField] private float pulseSpeed = 5f;

        private RectTransform root;
        private CanvasGroup group;
        private RectTransform brackets;
        private Image[] strokes;
        private Text status;
        private Text hint;

        private float missAt = -100f;
        private bool subscribed;

        private const float CanvasWidth = 1080f;
        private const float CanvasHeight = 620f;

        private void Awake()
        {
            ResolveReferences();
            Build();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        private void Start()
        {
            ResolveReferences();
            Subscribe();
        }

        private void ResolveReferences()
        {
            if (game == null)
                game = FindFirstObjectByType<
                    SurvivalGameManager>();

            if (overload == null)
                overload = FindFirstObjectByType<
                    SurvivalOverloadController>();

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
            if (overload == null || subscribed)
                return;

            overload.ShotMissed += OnMissed;
            subscribed = true;
        }

        private void OnMissed(Vector3 position)
        {
            missAt = Time.unscaledTime;
        }

        private void Build()
        {
            GameObject obj = new GameObject(
                "WS_OverloadTargeting_Runtime",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasGroup));

            obj.transform.SetParent(transform, false);

            root = obj.GetComponent<RectTransform>();
            root.sizeDelta =
                new Vector2(CanvasWidth, CanvasHeight);

            Canvas canvas = obj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 60;

            group = obj.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            GameObject bracketObj = new GameObject(
                "TargetBrackets", typeof(RectTransform));

            bracketObj.transform.SetParent(root, false);

            brackets =
                bracketObj.GetComponent<RectTransform>();

            brackets.anchorMin =
                brackets.anchorMax =
                new Vector2(0.5f, 0.5f);

            brackets.sizeDelta =
                new Vector2(bracketSize, bracketSize);

            strokes = new Image[8];

            for (int corner = 0; corner < 4; corner++)
            {
                float x = corner % 2 == 0 ? -1f : 1f;
                float y = corner < 2 ? 1f : -1f;

                strokes[corner * 2] = MakeStroke(
                    "Horizontal_" + corner,
                    new Vector2(
                        x * (bracketSize - bracketLength) * 0.5f,
                        y * bracketSize * 0.5f),
                    new Vector2(
                        bracketLength, bracketThickness));

                strokes[corner * 2 + 1] = MakeStroke(
                    "Vertical_" + corner,
                    new Vector2(
                        x * bracketSize * 0.5f,
                        y * (bracketSize - bracketLength) * 0.5f),
                    new Vector2(
                        bracketThickness, bracketLength));
            }

            Font font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(
                    "Arial", 18);

            status = MakeText(
                "TargetStatus", font, 19,
                new Vector2(0f, -bracketSize * 0.5f - 30f),
                new Vector2(310f, 30f));

            hint = MakeText(
                "TargetHint", font, 14,
                new Vector2(0f, -bracketSize * 0.5f - 52f),
                new Vector2(310f, 24f));

            group.alpha = 0f;
        }

        private Image MakeStroke(
            string name,
            Vector2 position,
            Vector2 size)
        {
            GameObject obj = new GameObject(
                name, typeof(RectTransform),
                typeof(Image));

            obj.transform.SetParent(brackets, false);

            RectTransform rect =
                obj.GetComponent<RectTransform>();

            rect.anchorMin = rect.anchorMax =
                new Vector2(0.5f, 0.5f);

            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = obj.GetComponent<Image>();
            image.color = amber;
            image.raycastTarget = false;

            return image;
        }

        private Text MakeText(
            string name,
            Font font,
            int size,
            Vector2 position,
            Vector2 dimensions)
        {
            GameObject obj = new GameObject(
                name, typeof(RectTransform),
                typeof(Text));

            obj.transform.SetParent(brackets, false);

            RectTransform rect =
                obj.GetComponent<RectTransform>();

            rect.anchorMin = rect.anchorMax =
                new Vector2(0.5f, 0.5f);

            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            Text text = obj.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.color = amber;

            return text;
        }

        private void LateUpdate()
        {
            if (game == null || overload == null ||
                head == null || root == null)
            {
                group.alpha = 0f;
                return;
            }

            root.SetParent(head, false);
            root.localPosition =
                new Vector3(0f, 0f, hudDistance);
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one * canvasScale;

            if (!game.IsRunning)
            {
                group.alpha = 0f;
                return;
            }

            float sinceMiss =
                Time.unscaledTime - missAt;

            if (sinceMiss >= 0f &&
                sinceMiss < missFlashSeconds)
            {
                group.alpha = 1f;
                brackets.anchoredPosition =
                    new Vector2(0f, verticalOffset);

                SetAppearance(missRed);
                status.text = "SHOT MISSED";
                hint.text = "RECHARGE REQUIRED";
                return;
            }

            if (!overload.IsCharged || overload.IsFiring)
            {
                group.alpha = 0f;
                return;
            }

            group.alpha = 1f;

            GameObject striker = game.ActiveStriker;

            if (striker == null ||
                !striker.activeInHierarchy)
            {
                SetSeekState("SEEK STRIKER");
                return;
            }

            Vector3 toTarget =
                striker.transform.position - head.position;

            float distance = toTarget.magnitude;

            if (distance < 0.1f ||
                distance > overload.MaximumAimDistance)
            {
                SetSeekState("OUT OF RANGE");
                return;
            }

            float angle = Vector3.Angle(
                head.forward, toTarget);

            if (angle > overload.AimAngle)
            {
                SetSeekState("ALIGN TARGET");
                return;
            }

            if (overload.IsScoutTargeting)
            {
                SetSeekState("SCOUT PRIORITY");
                return;
            }

            // Project the moving Striker onto the
            // headset-aligned HUD plane.
            Vector3 local =
                head.InverseTransformPoint(
                    striker.transform.position);

            if (local.z <= 0.01f)
            {
                SetSeekState("ALIGN TARGET");
                return;
            }

            float x = local.x / local.z *
                hudDistance / canvasScale;

            float y = local.y / local.z *
                hudDistance / canvasScale;

            // Avoid pushing labels beyond the
            // peripheral limits of the HUD.
            x = Mathf.Clamp(x, -410f, 410f);
            y = Mathf.Clamp(y, -205f, 205f);

            brackets.anchoredPosition =
                new Vector2(x, y + verticalOffset);

            float pulse =
                0.82f + 0.18f *
                Mathf.Sin(Time.unscaledTime * pulseSpeed);

            Color color = brightAmber;
            color.a = pulse;

        SetAppearance(color, true);

            status.text = "TARGET ACQUIRED";
            hint.text = "CLOSE FIST TO FIRE";
        }

        private void SetSeekState(string message)
        {
            brackets.anchoredPosition =
                new Vector2(0f, verticalOffset);

            SetAppearance(amber);

            status.text = message;
            hint.text = "WEB OVERLOAD // READY";
        }

        private void SetAppearance(
            Color color,
            bool showBrackets = false)
        {
            foreach (Image image in strokes)
            {
                image.color = color;
                image.enabled = showBrackets;
            }

            status.color = color;
            hint.color = color;
        }

        private void OnDisable()
        {
            if (subscribed && overload != null)
            {
                overload.ShotMissed -= OnMissed;
                subscribed = false;
            }

            if (group != null)
                group.alpha = 0f;
        }

        private void OnDestroy()
        {
            if (root != null)
                Destroy(root.gameObject);
        }
    }
}
