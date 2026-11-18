
using UnityEngine;
using UnityEngine.Rendering;

namespace WebSnatcher
{
    /// <summary>
    /// Lightweight Striker attack VFX for Quest.
    /// Visuals only; does not modify damage, aiming,
    /// flight timing, collision, or game state.
    ///
    /// Requires StrikerAttackController.ActiveProjectile.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StrikerAttackController))]
    public sealed class StrikerCombatVFX : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private StrikerAttackController attack;
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private Transform playerHead;

        [Header("Amber Theme")]
        [SerializeField] private Color amber =
            new Color(1f, 0.52f, 0.16f, 1f);

        [SerializeField] private Color brightAmber =
            new Color(1f, 0.85f, 0.48f, 1f);

        [Header("Muzzle Charge")]
        [SerializeField, Range(0.015f, 0.12f)]
        private float chargeRadius = 0.065f;

        [SerializeField, Range(0.05f, 2f)]
        private float chargeBuildSeconds = 0.9f;

        [SerializeField, Range(0f, 1f)]
        private float chargePulseStrength = 0.15f;

        [Header("Projectile Trail")]
        [SerializeField, Range(0.05f, 0.6f)]
        private float trailLifetime = 0.20f;

        [SerializeField, Range(0.002f, 0.04f)]
        private float trailWidth = 0.016f;

        [SerializeField, Range(0.3f, 1f)]
        private float projectileVisualScale = 0.65f;

        [Header("Impact")]
        [SerializeField, Range(0.08f, 0.5f)]
        private float impactDuration = 0.22f;

        [SerializeField, Range(0.05f, 0.5f)]
        private float impactMaxRadius = 0.20f;

        [SerializeField, Range(0.001f, 0.012f)]
        private float impactLineWidth = 0.006f;

        private const int Segments = 32;

        private Material amberMaterial;
        private Material brightMaterial;

        private LineRenderer chargeOuter;
        private LineRenderer chargeInner;
        private LineRenderer impactRing;

        private Transform chargeCore;

        private EnemyProjectile projectile;
        private TrailRenderer projectileTrail;

        private bool previousTelegraph;
        private bool eventsSubscribed;
        private bool projectileSubscribed;
        private bool built;

        private float chargeStartedAt;
        private float impactStartedAt = -100f;
        private Vector3 impactPosition;
        private Vector3 impactNormal;

        private void Awake()
        {
            ResolveReferences();
            BuildVisuals();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeAttack();
        }

        private void Start()
        {
            ResolveReferences();
            SubscribeAttack();
            ResolveProjectile();
        }

        private void ResolveReferences()
        {
            if (attack == null)
                attack = GetComponent<StrikerAttackController>();

            if (muzzlePoint == null)
            {
                Transform[] children =
                    GetComponentsInChildren<Transform>(true);

                foreach (Transform child in children)
                {
                    if (child.name == "MuzzlePoint")
                    {
                        muzzlePoint = child;
                        break;
                    }
                }
            }

            if (muzzlePoint == null)
                muzzlePoint = transform;

            if (playerHead == null)
            {
                OVRCameraRig rig =
                    FindFirstObjectByType<OVRCameraRig>();

                if (rig != null)
                    playerHead = rig.centerEyeAnchor;

                if (playerHead == null &&
                    Camera.main != null)
                    playerHead = Camera.main.transform;
            }
        }

        private void SubscribeAttack()
        {
            if (attack == null || eventsSubscribed)
                return;

            attack.ShotFired += HandleShotFired;
            eventsSubscribed = true;
        }

        private void ResolveProjectile()
        {
            if (attack == null)
                return;

            EnemyProjectile current =
                attack.ActiveProjectile;

            if (current == null)
                return;

            if (projectile == current &&
                projectileSubscribed)
                return;

            UnsubscribeProjectile();

            projectile = current;

            projectile.Resolved +=
                HandleProjectileResolved;

            projectileSubscribed = true;

            projectileTrail =
                projectile.GetComponent<TrailRenderer>();

            if (projectileTrail == null)
            {
                projectileTrail =
                    projectile.gameObject.AddComponent<
                        TrailRenderer>();
            }

            ConfigureTrail();
        }

        private void ConfigureTrail()
        {
            if (projectileTrail == null)
                return;

            projectileTrail.sharedMaterial = brightMaterial;
            projectileTrail.time = trailLifetime;
            projectileTrail.startWidth = trailWidth;
            projectileTrail.endWidth = 0f;
            projectileTrail.minVertexDistance = 0.015f;
            projectileTrail.numCapVertices = 2;
            projectileTrail.numCornerVertices = 2;
            projectileTrail.shadowCastingMode =
                ShadowCastingMode.Off;

            projectileTrail.receiveShadows = false;
            projectileTrail.emitting = false;

            projectileTrail.Clear();
        }

        private void BuildVisuals()
        {
            if (built)
                return;

            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");

            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogWarning(
                    "[WS Striker VFX] Unlit shader unavailable.",
                    this);
                return;
            }

            amberMaterial = CreateMaterial(
                shader, "WS_Striker_ChargeAmber", amber);

            brightMaterial = CreateMaterial(
                shader, "WS_Striker_BrightAmber",
                brightAmber);

            chargeOuter = MakeRing(
                "WS_Charge_Outer",
                amberMaterial,
                0.003f);

            chargeInner = MakeRing(
                "WS_Charge_Inner",
                brightMaterial,
                0.002f);

            impactRing = MakeRing(
                "WS_Impact_Ring",
                brightMaterial,
                impactLineWidth);

            GameObject core =
                GameObject.CreatePrimitive(
                    PrimitiveType.Sphere);

            core.name = "WS_Striker_ChargeCore";
            core.transform.SetParent(transform, false);

            Collider collider =
                core.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            Renderer renderer =
                core.GetComponent<Renderer>();

            if (renderer != null)
                renderer.sharedMaterial = brightMaterial;

            chargeCore = core.transform;

            HideCharge();

            if (impactRing != null)
                impactRing.enabled = false;

            built = true;
        }

        private Material CreateMaterial(
            Shader shader,
            string materialName,
            Color color)
        {
            Material material = new Material(shader);
            material.name = materialName;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            return material;
        }

        private LineRenderer MakeRing(
            string objectName,
            Material material,
            float width)
        {
            GameObject go = new GameObject(objectName);

            go.transform.SetParent(transform, false);

            LineRenderer line =
                go.AddComponent<LineRenderer>();

            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = Segments + 1;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode =
                ShadowCastingMode.Off;

            line.receiveShadows = false;
            line.enabled = false;

            return line;
        }

        private void LateUpdate()
        {
            if (!built || attack == null)
                return;

            if (projectile == null)
                ResolveProjectile();

            bool telegraph = attack.IsTelegraphing;

            if (telegraph && !previousTelegraph)
                chargeStartedAt = Time.time;

            previousTelegraph = telegraph;

            if (telegraph)
                UpdateCharge();
            else
                HideCharge();

            UpdateImpact();
        }

        private void UpdateCharge()
        {
            if (muzzlePoint == null ||
                chargeOuter == null ||
                chargeInner == null)
                return;

            float progress = Mathf.Clamp01(
                (Time.time - chargeStartedAt) /
                Mathf.Max(0.05f, chargeBuildSeconds));

            float pulse =
                1f + Mathf.Sin(Time.time * 17f) *
                chargePulseStrength;

            float outerRadius = Mathf.Lerp(
                chargeRadius,
                chargeRadius * 0.45f,
                progress) * pulse;

            float innerRadius = outerRadius * 0.55f;

            Vector3 normal =
                playerHead != null
                ? playerHead.position -
                  muzzlePoint.position
                : muzzlePoint.forward;

            if (normal.sqrMagnitude < 0.00001f)
                normal = transform.forward;

            Quaternion rotation = Quaternion.LookRotation(
                normal.normalized);

            Vector3 position = muzzlePoint.position;

            DrawRing(
                chargeOuter,
                position,
                rotation,
                outerRadius);

            DrawRing(
                chargeInner,
                position,
                rotation,
                innerRadius);

            chargeOuter.enabled = true;
            chargeInner.enabled = true;

            if (chargeCore != null)
            {
                chargeCore.gameObject.SetActive(true);
                chargeCore.position = position;

                float size =
                    Mathf.Lerp(0.018f, 0.050f, progress);

                chargeCore.localScale =
                    Vector3.one * size * pulse;
            }
        }

        private void HideCharge()
        {
            if (chargeOuter != null)
                chargeOuter.enabled = false;

            if (chargeInner != null)
                chargeInner.enabled = false;

            if (chargeCore != null)
                chargeCore.gameObject.SetActive(false);
        }

        private void HandleShotFired(
            StrikerAttackController source)
        {
            ResolveProjectile();

            if (projectile == null)
                return;

            // Scale only the mesh appearance.
            // EnemyProjectile retains its collision radius.
            Renderer renderer =
                projectile.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.transform.localScale =
                    Vector3.one *
                    projectile.Radius *
                    2f *
                    projectileVisualScale;
            }

            if (projectileTrail != null)
            {
                projectileTrail.Clear();
                projectileTrail.emitting = true;
            }

            HideCharge();
        }

        private void HandleProjectileResolved(
            EnemyProjectile bolt,
            EnemyProjectile.Outcome result)
        {
            if (projectileTrail != null)
            {
                projectileTrail.emitting = false;
                projectileTrail.Clear();
            }

            if (result == EnemyProjectile.Outcome.Missed)
                return;

            impactPosition = bolt.transform.position;

            impactNormal =
                playerHead != null
                ? playerHead.position - impactPosition
                : -transform.forward;

            if (impactNormal.sqrMagnitude < 0.00001f)
                impactNormal = Vector3.forward;

            // Prevent an impact ring from appearing
            // directly over the headset lenses.
            if (result == EnemyProjectile.Outcome.HitPlayer &&
                playerHead != null)
            {
                Vector3 away =
                    impactPosition - playerHead.position;

                if (away.sqrMagnitude < 0.00001f)
                    away = playerHead.forward;

                if (away.magnitude < 0.45f)
                {
                    impactPosition =
                        playerHead.position +
                        away.normalized * 0.45f;
                }
            }

            impactStartedAt = Time.time;

            if (impactRing != null)
                impactRing.enabled = true;
        }

        private void UpdateImpact()
        {
            if (impactRing == null ||
                !impactRing.enabled)
                return;

            float progress = Mathf.Clamp01(
                (Time.time - impactStartedAt) /
                Mathf.Max(0.01f, impactDuration));

            if (progress >= 1f)
            {
                impactRing.enabled = false;
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(
                impactNormal.normalized);

            float radius = Mathf.Lerp(
                0.015f,
                impactMaxRadius,
                progress);

            impactRing.widthMultiplier =
                impactLineWidth *
                Mathf.Lerp(1.8f, 0.2f, progress);

            DrawRing(
                impactRing,
                impactPosition,
                rotation,
                radius);
        }

        private static void DrawRing(
            LineRenderer line,
            Vector3 center,
            Quaternion rotation,
            float radius)
        {
            if (line == null)
                return;

            for (int i = 0; i <= Segments; i++)
            {
                float angle =
                    i * Mathf.PI * 2f / Segments;

                Vector3 local = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f);

                line.SetPosition(
                    i,
                    center + rotation * local);
            }
        }

        private void UnsubscribeProjectile()
        {
            if (projectile != null &&
                projectileSubscribed)
            {
                projectile.Resolved -=
                    HandleProjectileResolved;
            }

            projectileSubscribed = false;
            projectile = null;
            projectileTrail = null;
        }

        private void OnDisable()
        {
            if (attack != null && eventsSubscribed)
            {
                attack.ShotFired -= HandleShotFired;
                eventsSubscribed = false;
            }

            if (projectileTrail != null)
            {
                projectileTrail.emitting = false;
                projectileTrail.Clear();
            }

            UnsubscribeProjectile();
            HideCharge();

            if (impactRing != null)
                impactRing.enabled = false;

            previousTelegraph = false;
        }

        private void OnDestroy()
        {
            if (amberMaterial != null)
                Destroy(amberMaterial);

            if (brightMaterial != null)
                Destroy(brightMaterial);
        }
    }
}
