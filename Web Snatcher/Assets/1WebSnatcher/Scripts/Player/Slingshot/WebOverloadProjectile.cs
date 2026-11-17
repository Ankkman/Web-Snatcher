using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Swept boss-only projectile: no Rigidbody, no collisions with MRUK room geometry.
    /// One component per live shot; finishes as a compact procedural impact web.
    /// </summary>
    public sealed class WebOverloadProjectile : MonoBehaviour
    {
        [SerializeField] private float projectileRadius = 0.065f;
        [SerializeField] private float maxFlightSeconds = 3f;
        [SerializeField] private float impactSeconds = 0.24f;

        private Vector3 velocity;
        private BossHitTarget intendedBoss;
        private float charge;
        private float age;
        private float impactAge;
        private bool impacted;
        private Vector3 impactCenter;
        private Quaternion impactFacing;
        private Transform visualCore;
        private LineRenderer trail;
        private LineRenderer[] spokes;
        private Material silkMaterial;
        private Material flareMaterial;
        private readonly Vector3[] trailPoints = new Vector3[2];

        public static WebOverloadProjectile LaunchNew(
            Vector3 origin, Vector3 initialVelocity,
            BossHitTarget boss, float charge01, Color silk, Color flare)
        {
            GameObject go = new GameObject("WS_WebOverload_Bomb");
            go.transform.position = origin;
            WebOverloadProjectile shot = go.AddComponent<WebOverloadProjectile>();
            shot.Initialize(initialVelocity, boss, charge01, silk, flare);
            return shot;
        }

        private void Initialize(Vector3 initialVelocity, BossHitTarget boss, float charge01, Color silk, Color flare)
        {
            velocity = initialVelocity;
            intendedBoss = boss;
            charge = Mathf.Clamp01(charge01);
            silkMaterial = MakeMaterial("WS_BombSilk", silk);
            flareMaterial = MakeMaterial("WS_BombFlare", flare);

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "WebBomb_Core";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = Vector3.one * (0.07f + 0.035f * charge);
            Collider col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
            sphere.GetComponent<Renderer>().sharedMaterial = silkMaterial;
            visualCore = sphere.transform;

            trail = MakeLine("Bomb_MotionStreak", silkMaterial, 0.007f + 0.004f * charge, 2);
            spokes = new LineRenderer[10];
            for (int i = 0; i < spokes.Length; i++)
            {
                spokes[i] = MakeLine("WebBurst_Strut_" + i, i % 3 == 0 ? flareMaterial : silkMaterial, 0.0025f, 3);
                spokes[i].enabled = false;
            }
        }

        private static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            Material result = new Material(shader) { name = name };
            if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", color);
            else if (result.HasProperty("_Color")) result.SetColor("_Color", color);
            return result;
        }

        private LineRenderer MakeLine(string name, Material material, float width, int count)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            LineRenderer line = obj.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = count;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void Update()
        {
            if (impacted)
            {
                impactAge += Time.deltaTime;
                DrawImpact();
                if (impactAge >= impactSeconds) Destroy(gameObject);
                return;
            }

            age += Time.deltaTime;
            Vector3 start = transform.position;
            Vector3 movement = velocity * Time.deltaTime;
            float distance = movement.magnitude;

            // Only boss colliders are relevant; simulated room colliders cannot block the shot.
            int bossLayer = LayerMask.NameToLayer("Boss");
            int layerMask = bossLayer >= 0 ? 1 << bossLayer : 0;
            if (distance > 0.00001f && layerMask != 0 &&
                Physics.SphereCast(start, projectileRadius, movement / distance,
                    out RaycastHit hit, distance, layerMask, QueryTriggerInteraction.Collide))
            {
                BossHitTarget target = hit.collider.GetComponentInParent<BossHitTarget>();
                if (target != null && !target.IsDefeated &&
                    (intendedBoss == null || target == intendedBoss))
                {
                    target.ReceiveHit();
                    BeginImpact(hit.point, hit.normal);
                    return;
                }
            }

            transform.position = start + movement;
            trailPoints[0] = transform.position - velocity.normalized * (0.17f + 0.12f * charge);
            trailPoints[1] = transform.position;
            trail.SetPositions(trailPoints);
            if (visualCore != null)
                visualCore.Rotate(0f, 600f * Time.deltaTime, 300f * Time.deltaTime, Space.Self);

            if (age > maxFlightSeconds) Destroy(gameObject);
        }

        private void BeginImpact(Vector3 point, Vector3 normal)
        {
            impacted = true;
            impactCenter = point;
            impactAge = 0;
            impactFacing = Quaternion.LookRotation(normal.sqrMagnitude > 0.01f ? normal : -velocity.normalized);
            if (visualCore != null) visualCore.gameObject.SetActive(false);
            if (trail != null) trail.enabled = false;
            for (int i = 0; i < spokes.Length; i++) spokes[i].enabled = true;
            DrawImpact();
        }

        private void DrawImpact()
        {
            float t = Mathf.Clamp01(impactAge / impactSeconds);
            float radius = (0.05f + 0.26f * charge) * Mathf.Sin(t * Mathf.PI * 0.5f);
            for (int i = 0; i < spokes.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / spokes.Length;
                Vector3 radial = impactFacing * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                Vector3 bend = impactFacing * new Vector3(Mathf.Cos(angle + 0.35f), Mathf.Sin(angle + 0.35f), 0);
                LineRenderer l = spokes[i];
                l.widthMultiplier = 0.0035f * (1f - t);
                l.SetPosition(0, impactCenter + impactFacing * Vector3.forward * 0.012f);
                l.SetPosition(1, impactCenter + radial * radius * 0.5f + bend * radius * 0.12f);
                l.SetPosition(2, impactCenter + radial * radius);
            }
        }

        private void OnDestroy()
        {
            if (silkMaterial != null) Destroy(silkMaterial);
            if (flareMaterial != null) Destroy(flareMaterial);
        }
    }
}
