using Oculus.Interaction.PoseDetection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WebSnatcher
{
    /// <summary>Left-hand open-palm intent. ISDK pose is the device authority;
    /// H is an editor-only testing override. No guesswork from finger pinch state.</summary>
    [DisallowMultipleComponent]
    public sealed class LeftPalmShieldInput : MonoBehaviour
    {
        [SerializeField] private ShapeRecognizerActiveState leftOpenHandPose;
        [SerializeField] private OVRHand leftOVRHand;
        [SerializeField] private bool allowEditorH = true;
        public bool IsOpen { get; private set; }
        public bool IsConfigured => leftOpenHandPose != null;

        private void Awake()
        {
            if (leftOVRHand != null) return;
            var hands = FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var hand in hands)
                if (hand != null && hand.GetHand() == OVRPlugin.Hand.HandLeft) { leftOVRHand = hand; break; }

        }

        private void Update()
        {
            bool tracked = leftOVRHand == null || (leftOVRHand.IsTracked && leftOVRHand.IsDataValid);
            bool open = tracked && leftOpenHandPose != null &&
                        leftOpenHandPose.isActiveAndEnabled && leftOpenHandPose.Active;
#if UNITY_EDITOR
            if (allowEditorH && Keyboard.current != null && Keyboard.current.hKey.isPressed)
                open = true;
#endif
            IsOpen = open;
        }
        private void OnDisable() { IsOpen = false; }
    }
}
