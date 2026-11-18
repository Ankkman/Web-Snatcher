
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WebSnatcher
{
    /// <summary>
    /// Scout Bug Web Snatch V2.
    ///
    /// Right fist fires web at the gaze-selected Scout.
    /// A backward hand yank captures the Scout.
    ///
    /// Web attachment follows a dedicated point on the
    /// mechanical beetle model when BugWebAnchor exists.
    ///
    /// Compatible with:
    /// - BugTarget
    /// - BugWebAnchor
    /// - GazeTargeting
    /// - FistGestureInput
    /// - ProceduralWebRenderer
    /// - WebAttachmentVisual
    /// - MRUKBugSpawner
    /// - SurvivalGameManager
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class WebTetherController : MonoBehaviour
    {
        private enum State
        {
            Ready,
            Firing,
            Attached,
            Flying
        }

        [Header("Auto-discovered References")]
        [SerializeField] private GazeTargeting gaze;
        [SerializeField] private FistGestureInput fistInput;
        [SerializeField] private Transform headset;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform containmentOrb;
        [SerializeField] private ProceduralWebRenderer webVisual;
        [SerializeField] private WebAttachmentVisual webAttachment;

        [Header("Interaction")]
        [SerializeField, Min(0.03f)]
        private float yankDistance = 0.10f;

        [SerializeField, Range(0f, 0.10f)]
        private float minimumBackwardMovement = 0.025f;

        [SerializeField, Min(0.05f)]
        private float webShootSeconds = 0.12f;

        [SerializeField, Min(0.05f)]
        private float catchTravelSeconds = 0.32f;

        [SerializeField, Min(0.2f)]
        private float maxHoldSeconds = 3f;

        [SerializeField]
        private bool cancelOnOpenBeforeYank = true;

        [Header("Scout Attachment V2")]
        [SerializeField, Range(0.015f, 0.12f)]
        private float maxBodySplatRadius = 0.055f;

        [SerializeField]
        private bool useBodyAttachment = true;

        [Header("Orb Placement & Feedback")]
        [SerializeField]
        private Vector3 orbLocalPosition =
            new Vector3(0.10f, -0.30f, 0.60f);

        [SerializeField, Range(0.04f, 0.18f)]
        private float orbDiameter = 0.062f;

        [SerializeField, Range(0f, 0.5f)]
        private float orbPulseStrength = 0.24f;

        [SerializeField, Range(0.05f, 0.8f)]
        private float orbPulseSeconds = 0.25f;

        [SerializeField]
        private Color orbColor =
            new Color(0.24f, 0.20f, 0.22f, 1f);

        [Header("Editor Only")]
        [SerializeField]
        private bool editorKeyboardPull = true;

        [SerializeField, Min(0.01f)]
        private float editorPullSpeed = 0.35f;

        [Header("Debug")]
        [SerializeField]
        private bool logCatchEvents = true;

        private State state = State.Ready;

        private BugTarget bug;
        private BugWebAnchor activeWebAnchor;

        private Vector3 startHandRelative;
        private Vector3 pullBackwardWorld;
        private Vector3 editorHandOffset;
        private Vector3 bugStart;

        private float beganAt;
        private float flightTime;
        private float pulseStart = -100f;

        private Renderer orbRenderer;
        private Transform orbVisual;
        private Material orbMaterial;
        private bool ownsOrbMaterial;

        private FistGestureInput subscribedInput;

        public event Action WebFired;

        private void Awake()
        {
            ResolveReferences();

            if (webVisual == null)
            {
                webVisual =
                    GetComponent<ProceduralWebRenderer>();

                if (webVisual == null)
                {
                    webVisual = gameObject.AddComponent<
                        ProceduralWebRenderer>();
                }
            }

            if (webAttachment == null)
            {
                webAttachment =
                    GetComponentInChildren<
                        WebAttachmentVisual>(true);

                if (webAttachment == null)
                {
                    GameObject splat =
                        new GameObject("WS_Bug_WebSplat");

                    splat.transform.SetParent(
                        transform, false);

                    webAttachment =
                        splat.AddComponent<
                            WebAttachmentVisual>();
                }
            }

            SetupOrb();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeInput();

            if (containmentOrb != null)
                containmentOrb.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            UnsubscribeInput();
            CancelCatch();

            if (containmentOrb != null)
                containmentOrb.gameObject.SetActive(false);

            if (webAttachment != null)
                webAttachment.Hide();
        }

        private void ResolveReferences()
        {
            if (gaze == null)
                gaze =
                    FindFirstObjectByType<GazeTargeting>();

            if (fistInput == null)
                fistInput =
                    FindFirstObjectByType<FistGestureInput>();

            OVRCameraRig rig =
                FindFirstObjectByType<OVRCameraRig>();

            if (rig != null)
            {
                if (headset == null)
                    headset = rig.centerEyeAnchor;

                if (rightHand == null)
                    rightHand = rig.rightHandAnchor;
            }

            if (headset == null &&
                Camera.main != null)
            {
                headset = Camera.main.transform;
            }
        }

        private void SubscribeInput()
        {
            if (fistInput == null ||
                subscribedInput == fistInput)
                return;

            UnsubscribeInput();

            fistInput.FistClosed += OnFistClosed;
            fistInput.FistOpened += OnFistOpened;

            subscribedInput = fistInput;
        }

        private void UnsubscribeInput()
        {
            if (subscribedInput == null)
                return;

            subscribedInput.FistClosed -= OnFistClosed;
            subscribedInput.FistOpened -= OnFistOpened;

            subscribedInput = null;
        }

        private void SetupOrb()
        {
            if (headset == null)
                return;

            if (containmentOrb == null)
            {
                GameObject existing =
                    GameObject.Find("ChestOrb");

                if (existing != null)
                    containmentOrb = existing.transform;
                else
                    containmentOrb =
                        new GameObject("ChestOrb").transform;
            }

            containmentOrb.SetParent(headset, false);

            containmentOrb.localPosition =
                orbLocalPosition;

            containmentOrb.localRotation =
                Quaternion.identity;

            containmentOrb.localScale =
                Vector3.one;

            orbRenderer =
                containmentOrb.GetComponentInChildren<
                    Renderer>(true);

            if (orbRenderer == null)
            {
                GameObject sphere =
                    GameObject.CreatePrimitive(
                        PrimitiveType.Sphere);

                sphere.name = "Orb_Visual";

                sphere.transform.SetParent(
                    containmentOrb, false);

                orbRenderer =
                    sphere.GetComponent<Renderer>();

                Collider collider =
                    sphere.GetComponent<Collider>();

                if (collider != null)
                    Destroy(collider);
            }

            orbVisual = orbRenderer.transform;

            orbVisual.localPosition = Vector3.zero;
            orbVisual.localRotation =
                Quaternion.identity;

            orbVisual.localScale =
                Vector3.one * orbDiameter;

            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                return;

            orbMaterial = new Material(shader)
            {
                name = "WS_Orb_Prototype"
            };

            ownsOrbMaterial = true;

            if (orbMaterial.HasProperty("_BaseColor"))
            {
                orbMaterial.SetColor(
                    "_BaseColor", orbColor);
            }
            else if (orbMaterial.HasProperty("_Color"))
            {
                orbMaterial.SetColor(
                    "_Color", orbColor);
            }

            orbRenderer.sharedMaterial = orbMaterial;
        }

        private Vector3 HandPosition()
        {
            Vector3 point;

            if (rightHand != null)
            {
                point = rightHand.position;
            }
            else if (headset != null)
            {
                point =
                    headset.position +
                    headset.right * 0.2f -
                    headset.up * 0.18f +
                    headset.forward * 0.33f;
            }
            else
            {
                point = transform.position;
            }

            return point + editorHandOffset;
        }

        /// <summary>
        /// Gets the live attachment position on the
        /// mechanical Scout model.
        /// </summary>
        private Vector3 CurrentWebAttachmentPoint()
        {
            if (bug == null)
                return transform.position;

            if (useBodyAttachment &&
                activeWebAnchor != null)
            {
                return activeWebAnchor.Position;
            }

            return bug.AimPosition;
        }

        private Vector3 AttachmentNormal(
            Vector3 attachmentPosition)
        {
            if (headset == null)
                return Vector3.up;

            Vector3 normal =
                headset.position - attachmentPosition;

            if (normal.sqrMagnitude < 0.000001f)
                return Vector3.up;

            return normal.normalized;
        }

        private void OnFistClosed()
        {
            if (state != State.Ready ||
                gaze == null ||
                headset == null)
                return;

            BugTarget target = gaze.CurrentTarget;

            if (target == null ||
                !target.TryBeginCatch())
                return;

            bug = target;

            activeWebAnchor = useBodyAttachment
                ? target.GetComponent<BugWebAnchor>()
                : null;

            // Important: clear the simulated hand offset
            // before capturing the starting hand position.
            editorHandOffset = Vector3.zero;

            Vector3 hand = HandPosition();

            startHandRelative =
                hand - headset.position;

            pullBackwardWorld =
                headset.position - hand;

            if (pullBackwardWorld.sqrMagnitude <
                0.0001f)
            {
                pullBackwardWorld = -headset.forward;
            }

            pullBackwardWorld.Normalize();

            beganAt = Time.time;
            state = State.Firing;

            WebFired?.Invoke();

            if (logCatchEvents)
            {
                Debug.Log(
                    "[WebSnatcher] Web fired at " +
                    target.name,
                    this);
            }
        }

        private void OnFistOpened()
        {
            if (!cancelOnOpenBeforeYank)
                return;

            if (state == State.Attached ||
                state == State.Firing)
            {
                CancelCatch();
            }
        }

        private void Update()
        {
            if (state == State.Ready)
            {
                AnimateOrb();
                return;
            }

            if (bug == null || headset == null)
            {
                CancelCatch();
                return;
            }

#if UNITY_EDITOR
            // F is handled by FistGestureInput.
            // Space simulates the backward yank.
            if (editorKeyboardPull &&
                fistInput != null &&
                fistInput.IsFistClosed &&
                Keyboard.current != null &&
                Keyboard.current.spaceKey.isPressed &&
                (state == State.Firing ||
                 state == State.Attached))
            {
                editorHandOffset +=
                    pullBackwardWorld *
                    editorPullSpeed *
                    Time.deltaTime;
            }
#endif

            Vector3 handNow = HandPosition();

            Vector3 attachmentPosition =
                CurrentWebAttachmentPoint();

            if (state == State.Firing)
            {
                float travel = Mathf.Clamp01(
                    (Time.time - beganAt) /
                    webShootSeconds);

                // Web endpoint follows the actual bug body.
                webVisual.Draw(
                    handNow,
                    attachmentPosition,
                    travel,
                    0f,
                    Time.time);

                if (travel >= 1f)
                {
                    state = State.Attached;

                    Vector3 normal =
                        AttachmentNormal(
                            attachmentPosition);

                    webVisual.PlayImpact(
                        attachmentPosition,
                        normal);

                    if (webAttachment != null)
                    {
                        float splatRadius =
                            Mathf.Min(
                                bug.TargetRadius,
                                maxBodySplatRadius);

                        webAttachment.Show(
                            attachmentPosition,
                            normal,
                            splatRadius);
                    }
                }
            }
            else if (state == State.Attached)
            {
                // Headset-relative movement prevents
                // accidental yanks caused by head motion.
                Vector3 movement =
                    (handNow - headset.position) -
                    startHandRelative;

                float total =
                    movement.magnitude;

                float backward =
                    Vector3.Dot(
                        movement,
                        pullBackwardWorld);

                float tension =
                    Mathf.Clamp01(
                        total / yankDistance);

                // Keep strand attached to the live
                // body point while the Scout moves.
                webVisual.Draw(
                    handNow,
                    attachmentPosition,
                    1f,
                    tension,
                    Time.time);

                if (webAttachment != null)
                {
                    webAttachment.UpdatePose(
                        attachmentPosition,
                        AttachmentNormal(
                            attachmentPosition));
                }

                if (total >= yankDistance &&
                    backward >= minimumBackwardMovement)
                {
                    if (bug.Yank())
                    {
                        bugStart =
                            bug.transform.position;

                        flightTime = 0f;
                        state = State.Flying;

                        // Hide the splat during the pull,
                        // but keep the web attached.
                        if (webAttachment != null)
                            webAttachment.Hide();

                        if (logCatchEvents)
                        {
                            Debug.Log(
                                "[WebSnatcher] Bug yanked",
                                this);
                        }
                    }
                    else
                    {
                        CancelCatch();
                    }
                }
                else if (
                    Time.time - beganAt >
                    maxHoldSeconds)
                {
                    CancelCatch();
                }
            }
            else if (state == State.Flying)
            {
                if (containmentOrb == null)
                {
                    CancelCatch();
                    return;
                }

                flightTime += Time.deltaTime;

                float t = Mathf.Clamp01(
                    flightTime /
                    catchTravelSeconds);

                // Fast initial pull and smooth arrival.
                float eased =
                    1f - Mathf.Pow(1f - t, 3f);

                Vector3 end =
                    containmentOrb.position;

                bug.transform.position =
                    Vector3.Lerp(
                        bugStart,
                        end,
                        eased);

                // Re-read AFTER moving the bug.
                // This prevents a one-frame lag in
                // the web's attachment position.
                attachmentPosition =
                    CurrentWebAttachmentPoint();

                webVisual.Draw(
                    handNow,
                    attachmentPosition,
                    1f,
                    1f - t * 0.7f,
                    Time.time);

                if (t >= 1f)
                    CompleteCatch();
            }

            AnimateOrb();
        }

        private void CompleteCatch()
        {
            BugTarget completed = bug;

            bug = null;
            activeWebAnchor = null;

            state = State.Ready;
            editorHandOffset = Vector3.zero;

            if (webVisual != null)
                webVisual.HideWeb();

            if (webAttachment != null)
                webAttachment.Hide();

            pulseStart = Time.time;

            ContainmentOrbVisualV2 orbVFX =
                containmentOrb != null
                    ? containmentOrb.GetComponentInChildren<
                        ContainmentOrbVisualV2>(true)
                    : null;

            if (orbVFX != null)
                orbVFX.PlayCapturePulse();

            if (headset != null &&
                containmentOrb != null &&
                webVisual != null)
            {
                webVisual.PlayImpact(
                    containmentOrb.position,
                    headset.position -
                        containmentOrb.position,
                    orbPulseSeconds);
            }

            if (completed != null &&
                completed.CompleteCatch())
            {
                if (logCatchEvents)
                {
                    Debug.Log(
                        "[WebSnatcher] Bug caught",
                        this);
                }

                // Preserve the current spawner's
                // existing despawn behavior.
                completed.gameObject.SetActive(false);
            }
        }

        private void CancelCatch()
        {
            if (bug != null)
            {
                // Restore the original position when
                // a capture flight is interrupted.
                if (state == State.Flying)
                {
                    bug.transform.position = bugStart;
                }

                bug.CancelCatch();
            }

            bug = null;
            activeWebAnchor = null;

            state = State.Ready;
            editorHandOffset = Vector3.zero;

            if (webVisual != null)
                webVisual.HideWeb();

            if (webAttachment != null)
                webAttachment.Hide();
        }

        private void AnimateOrb()
        {
            if (orbVisual == null)
                return;

            float progress =
                (Time.time - pulseStart) /
                orbPulseSeconds;

            float multiplier =
                progress >= 0f &&
                progress <= 1f
                ? 1f +
                  Mathf.Sin(progress * Mathf.PI) *
                  orbPulseStrength
                : 1f;

            orbVisual.localScale =
                Vector3.one *
                (orbDiameter * multiplier);
        }

        private void OnDestroy()
        {
            UnsubscribeInput();

            if (ownsOrbMaterial &&
                orbMaterial != null)
            {
                Destroy(orbMaterial);
            }
        }
    }
}
