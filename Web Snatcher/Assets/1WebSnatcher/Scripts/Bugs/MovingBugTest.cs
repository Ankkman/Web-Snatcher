using UnityEngine;

namespace WebSnatcher
{
    /// <summary>
    /// Temporary editor/headset testing motion. Remove from actual spawned bug prefabs.
    /// No Rigidbody required. Can move along a wall or in free space.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MovingBugTest : MonoBehaviour
    {
        [SerializeField] private Vector3 localTravelAxis = Vector3.right;
        [SerializeField, Min(0f)] private float travelAmplitude = 0.20f;
        [SerializeField, Min(0.05f)] private float cyclesPerSecond = 0.45f;
        [SerializeField] private float phaseOffsetDegrees;
        private Vector3 originLocalPosition;

        private void OnEnable() { originLocalPosition = transform.localPosition; }

        private void Update()
        {
            Vector3 axis = localTravelAxis.sqrMagnitude > 0.0001f
                ? localTravelAxis.normalized : Vector3.right;
            float radians = Time.time * cyclesPerSecond * Mathf.PI * 2f +
                phaseOffsetDegrees * Mathf.Deg2Rad;
            transform.localPosition = originLocalPosition +
                axis * (Mathf.Sin(radians) * travelAmplitude);
        }
    }
}
