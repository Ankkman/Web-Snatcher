using System;
using UnityEngine;

namespace WebSnatcher
{
    /// <summary>
    /// One reusable, kinematic enemy bolt. Does not own health, score or game state.
    /// Its owning attack component subscribes to the outcome.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyProjectile : MonoBehaviour
    {
        public enum Outcome { Blocked, HitPlayer, Missed }
        public event Action<EnemyProjectile, Outcome> Resolved;

        [SerializeField, Min(0.01f)] private float radius = 0.07f;
        [SerializeField, Min(0.05f)] private float playerHitRadius = 0.22f;
        [SerializeField, Min(0.1f)] private float maximumLifetime = 7f;
        [SerializeField] private bool trackTargetDuringFlight = false;
        [SerializeField, Range(0f, 1f)] private float targetFollowStrength = 0.15f;

        private Transform player;
        private WebShieldController shield;
        private Vector3 destination;
        private Vector3 velocity;
        private float launchTime;
        private bool armed;

        public bool IsFlying => armed;
        public float Radius => radius;

        public void Configure(Transform playerHead, WebShieldController webShield,
            float collisionRadius, float headHitRadius)
        {
            player = playerHead;
            shield = webShield;
            radius = Mathf.Max(0.01f, collisionRadius);
            playerHitRadius = Mathf.Max(0.05f, headHitRadius);
            transform.localScale = Vector3.one * radius * 2f;
        }

        public void Fire(Vector3 origin, Vector3 aimPosition, float speed)
        {
            transform.position = origin;
            destination = aimPosition;
            Vector3 direction = aimPosition - origin;
            if (direction.sqrMagnitude < 0.0001f)
                direction = transform.forward;
            velocity = direction.normalized * Mathf.Max(0.1f, speed);
            launchTime = Time.time;
            armed = true;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!armed) return;

            if (Time.time - launchTime >= maximumLifetime)
            {
                Resolve(Outcome.Missed);
                return;
            }

            if (trackTargetDuringFlight && player != null)
                destination = Vector3.Lerp(destination, player.position,
                    targetFollowStrength * Time.deltaTime);

            Vector3 from = transform.position;
            Vector3 to = from + velocity * Time.deltaTime;

            // Check the shield first. It has a swept-segment collision test.
            if (shield != null && shield.TryBlockSegment(from, to, radius))
            {
                transform.position = to;
                Resolve(Outcome.Blocked);
                return;
            }

            // Segment-versus-sphere hit: robust against bolts skipping the head.
            if (player != null && SegmentIntersectsSphere(from, to,
                    player.position, radius + playerHitRadius))
            {
                transform.position = to;
                Resolve(Outcome.HitPlayer);
                return;
            }

            transform.position = to;

            // A miss cannot fly forever. Check if the shot passed the aimed point.
            if (Vector3.Dot(destination - to, velocity) <= 0f)
                Resolve(Outcome.Missed);
        }

        private static bool SegmentIntersectsSphere(Vector3 a, Vector3 b,
            Vector3 center, float combinedRadius)
        {
            Vector3 ab = b - a;
            float denominator = ab.sqrMagnitude;
            float t = denominator > 0.0000001f
                ? Mathf.Clamp01(Vector3.Dot(center - a, ab) / denominator)
                : 0f;
            Vector3 closest = a + ab * t;
            return (closest - center).sqrMagnitude <= combinedRadius * combinedRadius;
        }

        public void Cancel()
        {
            armed = false;
            gameObject.SetActive(false);
        }

        private void Resolve(Outcome result)
        {
            if (!armed) return;
            armed = false;
            gameObject.SetActive(false);
            Resolved?.Invoke(this, result);
        }

        private void OnDisable() { armed = false; }
    }
}
