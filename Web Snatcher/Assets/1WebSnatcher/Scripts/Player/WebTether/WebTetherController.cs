
using UnityEngine;
using UnityEngine.InputSystem;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class WebTetherController : MonoBehaviour
    {
        private enum TetherState
        {
            Ready,
            Attached,
            PullingBug
        }

        [Header("Auto-resolved References")]
        [SerializeField] private GazeTargeting gaze;
        [SerializeField] private FistGestureInput fistInput;
        [SerializeField] private Transform headset;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform containmentOrb;

        [Header("Yank Configuration")]
        [SerializeField, Min(0.01f)]
        private float yankDistance = 0.10f;

        [Tooltip("Minimum movement toward the headset, in metres.")]
        [SerializeField, Range(0f, 0.10f)]
        private float minimumBackwardMovement = 0.025f;

        [SerializeField, Min(0.05f)]
        private float catchTravelTime = 0.35f;

        [SerializeField, Min(0.1f)]
        private float maximumHoldSeconds = 3f;

        [Header("Web")]
        [SerializeField] private Color webColor =
            new Color(0.1f, 0.9f, 1f, 1f);

        [SerializeField, Min(0.001f)]
        private float webWidth = 0.008f;

        [SerializeField, Range(0f, 0.05f)]
        private float webSag = 0.015f;

        [Header("Editor Testing")]
        [SerializeField] private bool allowEditorSimulation = true;
        [SerializeField] private float editorPullSpeed = 0.35f;

        private TetherState state = TetherState.Ready;
        private BugTarget attachedBug;

        private LineRenderer webLine;
        private Material webMaterial;
        private Material orbMaterial;

        private Vector3 handAtGrab;
        private Vector3 headsetAtGrab;
        private Vector3 pullDirection;
        private Vector3 simulatedHandOffset;
        private Vector3 flightStart;

        private float attachmentTime;
        private float flightElapsed;

        private void Awake()
        {
            ResolveReferences();
            CreateWeb();
            SetupOrb();
        }

        private void OnEnable()
        {
            if (fistInput != null)
            {
                fistInput.FistClosed += OnFistClosed;
                fistInput.FistOpened += OnFistOpened;
            }
        }

        private void OnDisable()
        {
            if (fistInput != null)
            {
                fistInput.FistClosed -= OnFistClosed;
                fistInput.FistOpened -= OnFistOpened;
            }

            CancelCatch();
        }

        private void ResolveReferences()
        {
            if (gaze == null)
                gaze = FindFirstObjectByType<GazeTargeting>();

            if (fistInput == null)
                fistInput = FindFirstObjectByType<FistGestureInput>();

            var rig = FindFirstObjectByType<OVRCameraRig>();

            if (rig != null)
            {
                if (headset == null)
                    headset = rig.centerEyeAnchor;

                if (rightHand == null)
                    rightHand = rig.rightHandAnchor;
            }

            if (headset == null && Camera.main != null)
                headset = Camera.main.transform;
        }

        private Material CreateUnlitMaterial(
            string materialName, Color color)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            Material result = new Material(shader);
            result.name = materialName;

            if (result.HasProperty("_BaseColor"))
                result.SetColor("_BaseColor", color);
            else if (result.HasProperty("_Color"))
                result.SetColor("_Color", color);

            return result;
        }

        private void CreateWeb()
        {
            GameObject webObject =
                new GameObject("Procedural_Web_Tether");

            webObject.transform.SetParent(transform, false);

            webLine = webObject.AddComponent<LineRenderer>();

            webLine.useWorldSpace = true;
            webLine.positionCount = 12;
            webLine.widthMultiplier = webWidth;
            webLine.numCapVertices = 4;

            webLine.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            webLine.receiveShadows = false;

            webMaterial = CreateUnlitMaterial(
                "WS_Web_Cyan", webColor);

            webLine.sharedMaterial = webMaterial;
            webLine.enabled = false;
        }

        private void SetupOrb()
        {
            if (headset == null)
                return;

            // Automatically use the existing empty ChestOrb
            // object if one exists.
            if (containmentOrb == null)
            {
                GameObject existing = GameObject.Find("ChestOrb");

                if (existing != null)
                    containmentOrb = existing.transform;
            }

            if (containmentOrb == null)
            {
                containmentOrb =
                    new GameObject("ChestOrb").transform;
            }

            // Keep the orb in a consistent seated-player
            // position relative to the headset.
            containmentOrb.SetParent(headset, false);
            containmentOrb.localPosition =
                new Vector3(0f, -0.30f, 0.48f);

            containmentOrb.localRotation = Quaternion.identity;
            containmentOrb.localScale = Vector3.one;

            if (containmentOrb.GetComponentInChildren<Renderer>())
                return;

            GameObject visual =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            visual.name = "Orb_Visual";
            visual.transform.SetParent(containmentOrb, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * 0.16f;

            Collider orbCollider = visual.GetComponent<Collider>();

            if (orbCollider != null)
                Destroy(orbCollider);

            orbMaterial = CreateUnlitMaterial(
                "WS_Orb_Blue",
                new Color(0.02f, 0.30f, 0.85f, 1f));

            visual.GetComponent<Renderer>().sharedMaterial =
                orbMaterial;
        }

        private void Update()
        {
#if UNITY_EDITOR
            UpdateEditorPull();
#endif

            switch (state)
            {
                case TetherState.Attached:
                    UpdateAttached();
                    break;

                case TetherState.PullingBug:
                    UpdateFlight();
                    break;
            }
        }

        private Vector3 GetHandPosition()
        {
            Vector3 position;

            if (rightHand != null)
            {
                position = rightHand.position;
            }
            else if (headset != null)
            {
                position =
                    headset.position +
                    headset.right * 0.22f -
                    headset.up * 0.18f +
                    headset.forward * 0.35f;
            }
            else
            {
                position = transform.position;
            }

            return position + simulatedHandOffset;
        }

        private void OnFistClosed()
        {
            if (state != TetherState.Ready ||
                gaze == null || headset == null)
                return;

            BugTarget target = gaze.CurrentTarget;

            if (target == null)
            {
                Debug.Log("[WebSnatcher] Fist missed: no target.");
                return;
            }

            if (!target.TryBeginCatch())
                return;

            attachedBug = target;

            handAtGrab = GetHandPosition();
            headsetAtGrab = headset.position;

            Vector3 towardHead =
                headsetAtGrab - handAtGrab;

            if (towardHead.sqrMagnitude < 0.0001f)
                towardHead = -headset.forward;

            pullDirection = towardHead.normalized;

            attachmentTime = Time.time;
            state = TetherState.Attached;

            webLine.enabled = true;

            Debug.Log("[WebSnatcher] Web ATTACHED!");
        }

        private void OnFistOpened()
        {
            if (state == TetherState.Attached)
                CancelCatch();

            simulatedHandOffset = Vector3.zero;
        }


        private void UpdateAttached()
        {
            if (attachedBug == null || headset == null)
            {
                CancelCatch();
                return;
            }

            Vector3 handNow = GetHandPosition();

            RenderWeb(handNow, attachedBug.AimPosition);

            // Convert both grab and current hand positions into
            // headset-relative coordinates.
            Vector3 originalRelative =
                handAtGrab - headsetAtGrab;

            Vector3 currentRelative =
                handNow - headset.position;

            Vector3 movement =
                currentRelative - originalRelative;

            // Allow diagonal and slightly sideways pulls.
            float totalMovement = movement.magnitude;

            // Still require some movement backward.
            float backwardMovement =
                Vector3.Dot(movement, pullDirection);

            if (totalMovement >= yankDistance &&
                backwardMovement >= minimumBackwardMovement)
            {
                BeginYank();
                return;
            }

            if (Time.time - attachmentTime >
                maximumHoldSeconds)
            {
                CancelCatch();
            }
        }


        private void BeginYank()
        {
            if (attachedBug == null ||
                containmentOrb == null ||
                !attachedBug.Yank())
            {
                CancelCatch();
                return;
            }

            state = TetherState.PullingBug;
            flightStart = attachedBug.transform.position;
            flightElapsed = 0f;

            Debug.Log("[WebSnatcher] Bug YANKED!");
        }

        private void UpdateFlight()
        {
            if (attachedBug == null || containmentOrb == null)
            {
                CancelCatch();
                return;
            }

            flightElapsed += Time.deltaTime;

            float t = Mathf.Clamp01(
                flightElapsed / catchTravelTime);

            float smooth = t * t * (3f - 2f * t);

            attachedBug.transform.position = Vector3.Lerp(
                flightStart,
                containmentOrb.position,
                smooth);

            RenderWeb(
                GetHandPosition(),
                attachedBug.AimPosition);

            if (t >= 1f)
                FinishCatch();
        }

        private void FinishCatch()
        {
            BugTarget bug = attachedBug;
            attachedBug = null;

            state = TetherState.Ready;
            webLine.enabled = false;
            simulatedHandOffset = Vector3.zero;

            if (bug != null && bug.CompleteCatch())
            {
                Debug.Log("[WebSnatcher] Bug CAUGHT!");
                bug.gameObject.SetActive(false);
            }
        }

        private void CancelCatch()
        {
            if (attachedBug != null)
                attachedBug.CancelCatch();

            attachedBug = null;
            state = TetherState.Ready;
            simulatedHandOffset = Vector3.zero;

            if (webLine != null)
                webLine.enabled = false;
        }

        private void RenderWeb(Vector3 start, Vector3 end)
        {
            if (webLine == null)
                return;

            const int segments = 12;

            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)(segments - 1);

                Vector3 point =
                    Vector3.Lerp(start, end, t);

                // Simple procedural elastic curvature.
                point += Vector3.down *
                    (Mathf.Sin(t * Mathf.PI) * webSag);

                webLine.SetPosition(i, point);
            }
        }

#if UNITY_EDITOR
        private void UpdateEditorPull()
        {
            if (!allowEditorSimulation ||
                state != TetherState.Attached ||
                Keyboard.current == null)
                return;

            if (Keyboard.current.spaceKey.isPressed)
            {
                simulatedHandOffset +=
                    pullDirection *
                    editorPullSpeed * Time.deltaTime;
            }
        }
#endif

        private void OnDestroy()
        {
            if (webMaterial != null)
                Destroy(webMaterial);

            if (orbMaterial != null)
                Destroy(orbMaterial);
        }
    }
}
