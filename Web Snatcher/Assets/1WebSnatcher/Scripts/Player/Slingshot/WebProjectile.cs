
using UnityEngine;

namespace WebSnatcher
{
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class WebProjectile : MonoBehaviour
    {
        [SerializeField, Min(0.5f)]
        private float lifetime = 5f;

        private Rigidbody body;
        private bool consumed;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();

            body.useGravity = false;
            body.isKinematic = false;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void Launch(Vector3 velocity)
        {
            consumed = false;
            body.linearVelocity = velocity;
            Destroy(gameObject, lifetime);
        }

        private void OnCollisionEnter(Collision collision)
        {
            HandleImpact(collision.collider);
        }

        private void OnTriggerEnter(Collider other)
        {
            HandleImpact(other);
        }


        private void HandleImpact(Collider hit)
        {
            if (consumed || hit == null)
                return;

            BossHitTarget boss =
                hit.GetComponentInParent<BossHitTarget>();

            // Ignore impacts that are not boss targets.
            if (boss == null || boss.IsDefeated)
                return;

            consumed = true;

            boss.ReceiveHit();

            Destroy(gameObject);
        }

    }
}
