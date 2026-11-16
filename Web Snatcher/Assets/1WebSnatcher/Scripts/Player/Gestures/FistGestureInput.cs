using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Oculus.Interaction.PoseDetection;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class FistGestureInput : MonoBehaviour
    {
        [Header("Right Hand Tracking")]
        [SerializeField]
        private ShapeRecognizerActiveState fistRecognizer;

        [Tooltip("Optional OVRHand for tracking validity.")]
        [SerializeField]
        private OVRHand rightOVRHand;

        [Header("Editor Testing")]
        [SerializeField]
        private bool enableKeyboardSimulation = true;

        [Header("Debug")]
        [SerializeField]
        private bool logStateChanges = true;

        [SerializeField]
        private bool handTracked;

        [SerializeField]
        private bool recognizerActive;

        public event Action FistClosed;
        public event Action FistOpened;

        public bool IsFistClosed { get; private set; }

        public bool IsHandTracked => handTracked;

        private void Awake()
        {
            // Only auto-discover the right OVRHand.
            // Don't auto-select a ShapeRecognizer:
            // there may be many poses in the scene.
            if (rightOVRHand == null)
            {
                OVRHand[] hands =
                    FindObjectsByType<OVRHand>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None);

                foreach (OVRHand hand in hands)
                {
                    if (hand.GetHand() == OVRPlugin.Hand.HandRight)
                    {
                        rightOVRHand = hand;
                        break;
                    }
                }
            }

            if (fistRecognizer == null)
            {
                Debug.LogWarning(
                    "[WebSnatcher] Assign the right-fist " +
                    "ShapeRecognizerActiveState in Inspector.",
                    this);
            }
        }

        private void Update()
        {
            // If no OVRHand is provided, let the ISDK
            // recognizer determine tracking validity.
            handTracked = rightOVRHand == null ||
                (rightOVRHand.IsTracked &&
                 rightOVRHand.IsDataValid);

            recognizerActive =
                fistRecognizer != null &&
                fistRecognizer.isActiveAndEnabled &&
                fistRecognizer.Active;

            bool closed =
                handTracked && recognizerActive;

#if UNITY_EDITOR
            if (enableKeyboardSimulation &&
                Keyboard.current != null &&
                Keyboard.current.fKey.isPressed)
            {
                closed = true;
            }
#endif

            if (closed == IsFistClosed)
                return;

            IsFistClosed = closed;

            if (logStateChanges)
            {
                Debug.Log(
                    closed
                        ? "[WebSnatcher] FIST CLOSED"
                        : "[WebSnatcher] FIST OPENED",
                    this);
            }

            if (closed)
                FistClosed?.Invoke();
            else
                FistOpened?.Invoke();
        }

        private void OnDisable()
        {
            if (!IsFistClosed)
                return;

            IsFistClosed = false;
            FistOpened?.Invoke();
        }
    }
}
