
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class SlingshotController : MonoBehaviour
    {
        private enum SlingState
        {
            Inactive,
            Ready,
            Drawing,
            Fired
        }

        [Header("Tracking")]
        [SerializeField] private Transform headset;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private OVRHand leftOVRHand;
        [SerializeField] private OVRHand rightOVRHand;

        [Header("Slingshot")]
        [SerializeField, Min(0.05f)]
        private float minimumDraw = 0.08f;

        [SerializeField, Min(0.1f)]
        private float maximumDraw = 0.45f;

        [SerializeField, Min(0.1f)]
        private float launchSpeed = 5f;

        [SerializeField, Min(0f)]
        private float additionalSpeedPerMetre = 20f;

        [SerializeField, Min(0.1f)]
        private float maximumLaunchSpeed = 16f;

        [Header("Visuals")]
        [SerializeField] private Color webColor =
            new Color(0f, 0.95f, 1f, 1f);

        [SerializeField] private float webWidth = 0.006f;
        [SerializeField] private float projectileSize = 0.12f;

        [Header("Test Controls")]
        [SerializeField] private bool editorKeyboardSimulation = true;
        [SerializeField] private float editorPullSpeed = 0.5f;

        private SlingState state = SlingState.Inactive;

        private LineRenderer band;
        private Material visualMaterial;
        private Transform loadedOrb;

        private Vector3 launchOrigin;
        private Vector3 handToOrbOffset;
        private Vector3 simulatedPullOffset;
        private Vector3 drawPosition;

        private bool previousPinch;

        public bool IsBossPhaseActive =>
            state != SlingState.Inactive;

        private void Awake()
        {
            ResolveReferences();
            CreateVisuals();
            SetVisualsVisible(false);
        }

        private void ResolveReferences()
        {
            OVRCameraRig rig =
                FindFirstObjectByType<OVRCameraRig>();

            if (rig != null)
            {
                if (headset == null)
                    headset = rig.centerEyeAnchor;

                if (leftHand == null)
                    leftHand = rig.leftHandAnchor;

                if (rightHand == null)
                    rightHand = rig.rightHandAnchor;
            }

            if (headset == null && Camera.main != null)
                headset = Camera.main.transform;

            OVRHand[] hands =
                FindObjectsByType<OVRHand>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (OVRHand hand in hands)
            {
                if (hand.GetHand() == OVRPlugin.Hand.HandLeft &&
                    leftOVRHand == null)
                    leftOVRHand = hand;

                if (hand.GetHand() == OVRPlugin.Hand.HandRight &&
                    rightOVRHand == null)
                    rightOVRHand = hand;
            }

        }

        private void CreateVisuals()
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            visualMaterial = new Material(shader);
            visualMaterial.name = "WS_Slingshot_Runtime";

            if (visualMaterial.HasProperty("_BaseColor"))
                visualMaterial.SetColor("_BaseColor", webColor);
            else if (visualMaterial.HasProperty("_Color"))
                visualMaterial.SetColor("_Color", webColor);

            GameObject bandObject =
                new GameObject("Slingshot_Web_Band");

            bandObject.transform.SetParent(transform, false);

            band = bandObject.AddComponent<LineRenderer>();
            band.positionCount = 3;
            band.useWorldSpace = true;
            band.widthMultiplier = webWidth;
            band.numCapVertices = 4;
            band.sharedMaterial = visualMaterial;
            band.shadowCastingMode = ShadowCastingMode.Off;
            band.receiveShadows = false;

            GameObject orb = GameObject.CreatePrimitive(
                PrimitiveType.Sphere);

            orb.name = "Slingshot_Loaded_Orb";
            orb.transform.SetParent(transform, false);
            orb.transform.localScale =
                Vector3.one * projectileSize;

            Collider orbCollider = orb.GetComponent<Collider>();
            if (orbCollider != null)
                Destroy(orbCollider);

            orb.GetComponent<Renderer>().sharedMaterial =
                visualMaterial;

            loadedOrb = orb.transform;
        }

        public void ActivateBossPhase()
        {
            if (headset == null)
                ResolveReferences();

            state = SlingState.Ready;
            previousPinch = false;
            simulatedPullOffset = Vector3.zero;

            SetVisualsVisible(true);

            Debug.Log("[WebSnatcher] SLINGSHOT READY!");
        }

        public void DeactivateBossPhase()
        {
            state = SlingState.Inactive;
            previousPinch = false;
            simulatedPullOffset = Vector3.zero;
            SetVisualsVisible(false);
        }

        private void Update()
        {
#if UNITY_EDITOR
            // Test shortcut: B toggles the boss slingshot.
            if (editorKeyboardSimulation &&
                Keyboard.current != null &&
                Keyboard.current.bKey.wasPressedThisFrame)
            {
                if (IsBossPhaseActive)
                    DeactivateBossPhase();
                else
                    ActivateBossPhase();
            }
#endif

            if (state == SlingState.Inactive)
                return;

            if (headset == null ||
                leftHand == null ||
                rightHand == null)
                return;

            UpdateInteraction();
            UpdateBand();
        }

        private bool IsRightPinching()
        {
            bool pinch = false;

            if (rightOVRHand != null &&
                rightOVRHand.IsTracked &&
                rightOVRHand.IsDataValid)
            {
                pinch = rightOVRHand.GetFingerIsPinching(
                    OVRHand.HandFinger.Index);
            }

#if UNITY_EDITOR
            if (editorKeyboardSimulation &&
                Keyboard.current != null)
            {
                pinch |= Keyboard.current.gKey.isPressed;
            }
#endif

            return pinch;
        }

        private bool CanTrackHands()
        {
#if UNITY_EDITOR
            if (editorKeyboardSimulation &&
                Keyboard.current != null &&
                Keyboard.current.gKey.isPressed)
                return true;
#endif

            return leftOVRHand != null &&
                   rightOVRHand != null &&
                   leftOVRHand.IsTracked &&
                   rightOVRHand.IsTracked &&
                   leftOVRHand.IsDataValid &&
                   rightOVRHand.IsDataValid;
        }

        private Vector3 GetRestPosition()
        {
            // Orb sits halfway between tracked hands.
            return (leftHand.position + rightHand.position) * 0.5f;
        }

        private void UpdateInteraction()
        {
            bool tracked = CanTrackHands();
            bool pinching = tracked && IsRightPinching();

            if (!tracked && state == SlingState.Drawing)
            {
                CancelDraw();
                pinching = false;
            }

            if (state == SlingState.Ready ||
                state == SlingState.Fired)
            {
                loadedOrb.position = GetRestPosition();

                if (pinching && !previousPinch)
                {
                    BeginDraw();
                }
            }
            else if (state == SlingState.Drawing)
            {
                if (pinching)
                {
                    UpdateDrawPosition();
                }
                else if (previousPinch && tracked)
                {
                    ReleaseProjectile();
                }
            }

            previousPinch = pinching;
        }

        private void BeginDraw()
        {
            state = SlingState.Drawing;

            launchOrigin = GetRestPosition();

            handToOrbOffset =
                launchOrigin - rightHand.position;

            simulatedPullOffset = Vector3.zero;
            drawPosition = launchOrigin;

            Debug.Log("[WebSnatcher] SLINGSHOT GRABBED!");
        }

        private void UpdateDrawPosition()
        {
#if UNITY_EDITOR
            if (editorKeyboardSimulation &&
                Keyboard.current != null &&
                Keyboard.current.spaceKey.isPressed)
            {
                simulatedPullOffset -=
                    headset.forward *
                    editorPullSpeed * Time.deltaTime;
            }
#endif

            Vector3 desired =
                rightHand.position +
                handToOrbOffset +
                simulatedPullOffset;

            Vector3 displacement = desired - launchOrigin;

            displacement = Vector3.ClampMagnitude(
                displacement, maximumDraw);

            drawPosition = launchOrigin + displacement;
            loadedOrb.position = drawPosition;
        }

        private void ReleaseProjectile()
        {
            Vector3 draw = launchOrigin - drawPosition;

            float totalDraw = draw.magnitude;

            // Drawing backward relative to head forward
            // must contribute to charging the slingshot.
            float backwardDraw = Vector3.Dot(
                drawPosition - launchOrigin,
                -headset.forward);

            if (totalDraw < minimumDraw ||
                backwardDraw < minimumDraw * 0.5f)
            {
                CancelDraw();
                return;
            }

            float speed = Mathf.Min(
                maximumLaunchSpeed,
                launchSpeed +
                totalDraw * additionalSpeedPerMetre);

            Vector3 direction = draw.normalized;

            SpawnProjectile(
                drawPosition,
                direction * speed);

            state = SlingState.Fired;
            simulatedPullOffset = Vector3.zero;

            Debug.Log(
                $"[WebSnatcher] SLINGSHOT FIRED! " +
                $"Speed: {speed:F1} m/s");
        }

        private void SpawnProjectile(
            Vector3 position, Vector3 velocity)
        {
            GameObject projectile =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            projectile.name = "WebSlingshot_Projectile";
            projectile.transform.position = position;
            projectile.transform.localScale =
                Vector3.one * projectileSize;

            projectile.GetComponent<Renderer>()
                .sharedMaterial = visualMaterial;

            int layer = LayerMask.NameToLayer("WebProjectile");
            if (layer >= 0)
                projectile.layer = layer;

            // Add the Rigidbody before the projectile
            // behavior so its Awake() can configure it.
            Rigidbody body =
                projectile.AddComponent<Rigidbody>();

            body.useGravity = false;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;

            WebProjectile behavior =
                projectile.AddComponent<WebProjectile>();

            behavior.Launch(velocity);
        }

        private void CancelDraw()
        {
            state = SlingState.Ready;
            simulatedPullOffset = Vector3.zero;
            loadedOrb.position = GetRestPosition();
        }

        private void UpdateBand()
        {
            if (band == null || loadedOrb == null)
                return;

            Vector3 orbPosition = loadedOrb.position;

            band.SetPosition(0, leftHand.position);
            band.SetPosition(1, orbPosition);
            band.SetPosition(2, rightHand.position);
        }

        private void SetVisualsVisible(bool visible)
        {
            if (band != null)
                band.enabled = visible;

            if (loadedOrb != null)
                loadedOrb.gameObject.SetActive(visible);
        }

        private void OnDestroy()
        {
            if (visualMaterial != null)
                Destroy(visualMaterial);
        }
    }
}
