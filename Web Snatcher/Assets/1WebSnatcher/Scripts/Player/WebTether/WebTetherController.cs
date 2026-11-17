using UnityEngine;
using UnityEngine.InputSystem;

namespace WebSnatcher
{
    /// <summary>
    /// Quest fist-driven web catch. Keeps the existing BugTarget and FistGestureInput contracts.
    /// Does not depend on MRUK source (prefab or device room).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class WebTetherController : MonoBehaviour
    {
        private enum State { Ready, Firing, Attached, Flying }

        [Header("Auto-discovered References")]
        [SerializeField] private GazeTargeting gaze;
        [SerializeField] private FistGestureInput fistInput;
        [SerializeField] private Transform headset;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform containmentOrb;
        [SerializeField] private ProceduralWebRenderer webVisual;
        [SerializeField] private WebAttachmentVisual webAttachment;

        [Header("Interaction")]
        [SerializeField, Min(0.03f)] private float yankDistance = 0.10f;
        [SerializeField, Range(0f, 0.10f)] private float minimumBackwardMovement = 0.025f;
        [SerializeField, Min(0.05f)] private float webShootSeconds = 0.12f;
        [SerializeField, Min(0.05f)] private float catchTravelSeconds = 0.32f;
        [SerializeField, Min(0.2f)] private float maxHoldSeconds = 3f;
        [SerializeField] private bool cancelOnOpenBeforeYank = true;

        [Header("Orb Placement & Feedback")]
        [SerializeField] private Vector3 orbLocalPosition = new Vector3(-0.28f, -0.34f, 0.66f);
        [SerializeField, Range(0.04f, 0.18f)] private float orbDiameter = 0.09f;
        [SerializeField, Range(0f, 0.5f)] private float orbPulseStrength = 0.24f;
        [SerializeField, Range(0.05f, 0.8f)] private float orbPulseSeconds = 0.25f;
        [SerializeField] private Color orbColor = new Color(0.24f, 0.20f, 0.22f, 1f);

        [Header("Editor Only")]
        [SerializeField] private bool editorKeyboardPull = true;
        [SerializeField, Min(0.01f)] private float editorPullSpeed = 0.35f;

        private State state = State.Ready;
        private BugTarget bug;
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
        private float lastLoggedConfigError;

        private void Awake()
        {
            ResolveReferences();
            if (webVisual == null)
            {
                webVisual = GetComponent<ProceduralWebRenderer>();
                if (webVisual == null) webVisual = gameObject.AddComponent<ProceduralWebRenderer>();
            }
            if (webAttachment == null)
            {
                webAttachment = GetComponentInChildren<WebAttachmentVisual>(true);
                if (webAttachment == null)
                {
                    GameObject splat = new GameObject("WS_Bug_WebSplat");
                    splat.transform.SetParent(transform, false);
                    webAttachment = splat.AddComponent<WebAttachmentVisual>();
                }
            }
            SetupOrb();
        }

        private void OnEnable()
        {
            // Calls ResolveReferences because some rigs initialize after Awake.
            ResolveReferences();
            if (fistInput != null)
            {
                fistInput.FistClosed += OnFistClosed;
                fistInput.FistOpened += OnFistOpened;
            }
            if (containmentOrb != null) containmentOrb.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (fistInput != null)
            {
                fistInput.FistClosed -= OnFistClosed;
                fistInput.FistOpened -= OnFistOpened;
            }
            CancelCatch();
            if (containmentOrb != null) containmentOrb.gameObject.SetActive(false);
            if (webAttachment != null) webAttachment.Hide();
        }

        private void ResolveReferences()
        {
            if (gaze == null) gaze = FindFirstObjectByType<GazeTargeting>();
            if (fistInput == null) fistInput = FindFirstObjectByType<FistGestureInput>();
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (headset == null) headset = rig.centerEyeAnchor;
                if (rightHand == null) rightHand = rig.rightHandAnchor;
            }
            if (headset == null && Camera.main != null) headset = Camera.main.transform;
        }

        private void SetupOrb()
        {
            if (headset == null) return;
            if (containmentOrb == null)
            {
                GameObject existing = GameObject.Find("ChestOrb");
                if (existing != null) containmentOrb = existing.transform;
                else containmentOrb = new GameObject("ChestOrb").transform;
            }
            containmentOrb.SetParent(headset, false);
            containmentOrb.localPosition = orbLocalPosition;
            containmentOrb.localRotation = Quaternion.identity;
            containmentOrb.localScale = Vector3.one;

            orbRenderer = containmentOrb.GetComponentInChildren<Renderer>(true);
            if (orbRenderer == null)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Orb_Visual";
                sphere.transform.SetParent(containmentOrb, false);
                orbRenderer = sphere.GetComponent<Renderer>();
                Collider c = sphere.GetComponent<Collider>();
                if (c != null) Destroy(c);
            }
            orbVisual = orbRenderer.transform;
            orbVisual.localPosition = Vector3.zero;
            orbVisual.localRotation = Quaternion.identity;
            orbVisual.localScale = Vector3.one * orbDiameter;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return;
            orbMaterial = new Material(shader) { name = "WS_Orb_Prototype" };
            ownsOrbMaterial = true;
            if (orbMaterial.HasProperty("_BaseColor")) orbMaterial.SetColor("_BaseColor", orbColor);
            else if (orbMaterial.HasProperty("_Color")) orbMaterial.SetColor("_Color", orbColor);
            orbRenderer.sharedMaterial = orbMaterial;
        }

        private Vector3 HandPosition()
        {
            Vector3 point;
            if (rightHand != null) point = rightHand.position;
            else if (headset != null)
                point = headset.position + headset.right * 0.2f - headset.up * 0.18f + headset.forward * 0.33f;
            else point = transform.position;
            return point + editorHandOffset;
        }

        private void OnFistClosed()
        {
            if (state != State.Ready || gaze == null || headset == null) return;
            BugTarget target = gaze.CurrentTarget;
            if (target == null || !target.TryBeginCatch()) return;

            bug = target;
            Vector3 hand = HandPosition();
            startHandRelative = hand - headset.position;
            pullBackwardWorld = headset.position - hand;
            if (pullBackwardWorld.sqrMagnitude < 0.0001f) pullBackwardWorld = -headset.forward;
            pullBackwardWorld.Normalize();
            editorHandOffset = Vector3.zero;
            beganAt = Time.time;
            state = State.Firing;
            Debug.Log("[WebSnatcher] Web fired", this);
        }

        private void OnFistOpened()
        {
            if (cancelOnOpenBeforeYank && (state == State.Attached || state == State.Firing))
                CancelCatch();
        }

        private void Update()
        {
            if (state == State.Ready) { AnimateOrb(); return; }
            if (bug == null || headset == null) { CancelCatch(); return; }

#if UNITY_EDITOR
            // F is read ONLY by FistGestureInput. Space simulates the 10cm pull.
            if (editorKeyboardPull && fistInput != null && fistInput.IsFistClosed &&
                Keyboard.current != null && Keyboard.current.spaceKey.isPressed &&
                (state == State.Firing || state == State.Attached))
            {
                editorHandOffset += pullBackwardWorld * editorPullSpeed * Time.deltaTime;
            }
#endif
            Vector3 handNow = HandPosition();
            if (state == State.Firing)
            {
                float travel = Mathf.Clamp01((Time.time - beganAt) / webShootSeconds);
                webVisual.Draw(handNow, bug.AimPosition, travel, 0f, Time.time);
                if (travel >= 1f)
                {
                    state = State.Attached;
                    webVisual.PlayImpact(bug.AimPosition,
                        headset.position - bug.AimPosition);
                    if (webAttachment != null)
                        webAttachment.Show(bug.AimPosition,
                            headset.position - bug.AimPosition, bug.TargetRadius);
                }
            }
            else if (state == State.Attached)
            {
                // Headset-relative movement avoids accidental yanks from head translation.
                Vector3 movement = (handNow - headset.position) - startHandRelative;
                float total = movement.magnitude;
                float backward = Vector3.Dot(movement, pullBackwardWorld);
                float tension = Mathf.Clamp01(total / yankDistance);
                webVisual.Draw(handNow, bug.AimPosition, 1f, tension, Time.time);
                if (webAttachment != null)
                    webAttachment.UpdatePose(bug.AimPosition,
                        headset.position - bug.AimPosition);
                if (total >= yankDistance && backward >= minimumBackwardMovement)
                {
                    if (bug.Yank())
                    {
                        bugStart = bug.transform.position;
                        flightTime = 0f;
                        state = State.Flying;
                        // Splat remains for a short part of the flight, then clears.
                        if (webAttachment != null) webAttachment.Hide();
                        Debug.Log("[WebSnatcher] Bug yanked", this);
                    }
                    else CancelCatch();
                }
                else if (Time.time - beganAt > maxHoldSeconds)
                {
                    CancelCatch();
                }
            }
            else if (state == State.Flying)
            {
                if (containmentOrb == null) { CancelCatch(); return; }
                flightTime += Time.deltaTime;
                float t = Mathf.Clamp01(flightTime / catchTravelSeconds);
                // Fast initial pull, gentle arrival.
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                Vector3 end = containmentOrb.position;
                bug.transform.position = Vector3.Lerp(bugStart, end, eased);
                webVisual.Draw(handNow, bug.AimPosition, 1f, 1f - t * 0.7f, Time.time);
                if (t >= 1f) CompleteCatch();
            }
            AnimateOrb();
        }

        private void CompleteCatch()
        {
            BugTarget completed = bug;
            bug = null;
            state = State.Ready;
            editorHandOffset = Vector3.zero;
            webVisual.HideWeb();
            if (webAttachment != null) webAttachment.Hide();
            pulseStart = Time.time;
            if (headset != null && containmentOrb != null)
                webVisual.PlayImpact(containmentOrb.position,
                    headset.position - containmentOrb.position, orbPulseSeconds);

            if (completed != null && completed.CompleteCatch())
            {
                Debug.Log("[WebSnatcher] Bug caught", this);
                // Temporary: pool/spawner will take ownership of despawn later.
                completed.gameObject.SetActive(false);
            }
        }

        private void CancelCatch()
        {
            if (bug != null)
            {
                // Interrupted flight restores original position to avoid half-moved bugs.
                if (state == State.Flying) bug.transform.position = bugStart;
                bug.CancelCatch();
            }
            bug = null;
            state = State.Ready;
            editorHandOffset = Vector3.zero;
            if (webVisual != null) webVisual.HideWeb();
            if (webAttachment != null) webAttachment.Hide();
        }

        private void AnimateOrb()
        {
            if (orbVisual == null) return;
            float progress = (Time.time - pulseStart) / orbPulseSeconds;
            float multiplier = progress >= 0f && progress <= 1f
                ? 1f + Mathf.Sin(progress * Mathf.PI) * orbPulseStrength
                : 1f;
            orbVisual.localScale = Vector3.one * (orbDiameter * multiplier);
        }

        private void OnDestroy()
        {
            if (ownsOrbMaterial && orbMaterial != null) Destroy(orbMaterial);
        }
    }
}
