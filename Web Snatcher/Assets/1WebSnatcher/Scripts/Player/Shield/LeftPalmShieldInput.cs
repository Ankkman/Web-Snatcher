using Oculus.Interaction.PoseDetection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WebSnatcher
{
    /// <summary>Left open-hand input, with deterministic H-key simulation in Editor.
    /// In Editor, ISDK synthetic hands are ignored by default so an idle simulated
    /// open hand does not automatically deploy the shield.</summary>
    [DisallowMultipleComponent]
    public sealed class LeftPalmShieldInput : MonoBehaviour
    {
        [SerializeField] private ShapeRecognizerActiveState leftOpenHandPose;
        [SerializeField] private OVRHand leftOVRHand;
        [Header("Editor")]
        [SerializeField] private bool allowEditorH = true;
        [SerializeField] private bool useISDKPoseInEditor = false;

        public bool IsOpen { get; private set; }
        public bool IsConfigured => leftOpenHandPose != null;

        private void Awake()
        {
            if (leftOVRHand != null) return;
            var hands = FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var hand in hands)
            {
                if (hand != null && hand.GetHand() == OVRPlugin.Hand.HandLeft)
                {
                    leftOVRHand = hand;
                    break;
                }
            }

        }

        private void Update()
        {
            bool tracked = leftOVRHand == null || (leftOVRHand.IsTracked && leftOVRHand.IsDataValid);
            bool poseOpen = tracked && leftOpenHandPose != null &&
                            leftOpenHandPose.isActiveAndEnabled && leftOpenHandPose.Active;
#if UNITY_EDITOR
            // XR Simulator may report its idle synthetic hand as naturally open.
            // Prefer explicit H testing unless this switch is intentionally enabled.
            IsOpen = (useISDKPoseInEditor && poseOpen) ||
                     (allowEditorH && Keyboard.current != null && Keyboard.current.hKey.isPressed);
#else
            IsOpen = poseOpen;
#endif
        }

        private void OnDisable() { IsOpen = false; }
    }
}
