
using System;
using UnityEngine;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class WebSnatcherAudio : MonoBehaviour
    {
        [Header("Game References")]
        [SerializeField] private SurvivalGameManager game;
        [SerializeField] private SurvivalOverloadController overload;
        [SerializeField] private WebShieldController shield;
        [SerializeField] private WebTetherController tether;

        [Header("Sound Settings")]
        [SerializeField] private bool audioEnabled = true;
        [SerializeField, Range(0f, 1f)]
        private float masterVolume = 0.65f;
        [SerializeField, Range(0f, 1f)]
        private float combatVolume = 0.85f;
        [SerializeField, Range(0f, 1f)]
        private float uiVolume = 0.55f;

        [Header("Synthesis")]
        [SerializeField, Range(11025, 44100)]
        private int sampleRate = 22050;

        [Header("Debug")]
        [SerializeField] private bool debugKeyboardSounds = true;

        private enum Sfx
        {
            Boot, Start, WebFire, Catch, ShieldOn,
            ShieldBlock, Warning, EnemyShot, Damage,
            Charged, OverloadFire, OverloadHit,
            OverloadMiss, GameOver
        }

        private AudioClip[] clips;
        private AudioSource source;
        private StrikerAttackController strikerAttack;

        private GameObject observedStriker;
        private bool lastShieldActive;
        private bool lastRunActive;
        private bool lastCharged;
        private int previousHealth;
        private bool initialized;

        private bool lastTelegraphing;

        private void Awake()
        {
            ResolveReferences();

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;

            GenerateClips();
        }

        private void Start()
        {
            ResolveReferences();
            BindEvents();

            if (game != null)
            {
                previousHealth = game.Health;
                lastRunActive = game.IsRunning;
            }

            if (overload != null)
                lastCharged = overload.IsCharged;

            Play(Sfx.Boot, true);
            initialized = true;
        }

        private void ResolveReferences()
        {
            if (game == null)
                game = FindFirstObjectByType<
                    SurvivalGameManager>();

            if (overload == null)
                overload = FindFirstObjectByType<
                    SurvivalOverloadController>();

            if (shield == null)
                shield = FindFirstObjectByType<
                    WebShieldController>();

            if (tether == null)
                tether = FindFirstObjectByType<
                    WebTetherController>();
        }

        private void BindEvents()
        {
            BugTarget.AnyCatchCompleted -= HandleCatch;
            BugTarget.AnyCatchCompleted += HandleCatch;

            if (game != null)
            {
                game.ValuesChanged -= HandleValues;
                game.ValuesChanged += HandleValues;

                game.RunEnded -= HandleGameOver;
                game.RunEnded += HandleGameOver;
            }

            if (overload != null)
            {
                overload.ChargeChanged -= HandleCharge;
                overload.ChargeChanged += HandleCharge;

                overload.ShotLaunched -= HandleOverloadFire;
                overload.ShotLaunched += HandleOverloadFire;

                overload.ShotHit -= HandleOverloadHit;
                overload.ShotHit += HandleOverloadHit;

                overload.ShotMissed -= HandleOverloadMiss;
                overload.ShotMissed += HandleOverloadMiss;
            }

            if (tether != null)
            {
                tether.WebFired -= HandleWebFire;
                tether.WebFired += HandleWebFire;
            }
        }

        private void Update()
        {
            if (!initialized) return;

            if (game != null)
            {
                if (game.IsRunning && !lastRunActive)
                    Play(Sfx.Start, true);

                lastRunActive = game.IsRunning;

                BindStriker(game.ActiveStriker);

                if (strikerAttack != null)
                {
                    bool warning = strikerAttack.IsTelegraphing;

                    if (warning && !lastTelegraphing)
                        Play(Sfx.Warning);

                    lastTelegraphing = warning;
                }
            }

            if (shield != null)
            {
                bool active = shield.IsShieldActive;

                if (active && !lastShieldActive &&
                    game != null && game.IsRunning)
                    Play(Sfx.ShieldOn);

                lastShieldActive = active;
            }

#if UNITY_EDITOR
            // Optional quick sound preview in Play Mode.
            if (debugKeyboardSounds &&
                UnityEngine.InputSystem.Keyboard.current != null)
            {
                var keyboard =
                    UnityEngine.InputSystem.Keyboard.current;

                if (keyboard.digit1Key.wasPressedThisFrame)
                    Play(Sfx.WebFire);

                if (keyboard.digit2Key.wasPressedThisFrame)
                    Play(Sfx.ShieldBlock);

                if (keyboard.digit3Key.wasPressedThisFrame)
                    Play(Sfx.Warning);

                if (keyboard.digit4Key.wasPressedThisFrame)
                    Play(Sfx.OverloadFire);

                if (keyboard.digit5Key.wasPressedThisFrame)
                    Play(Sfx.OverloadHit);
            }
#endif
        }

        private void BindStriker(GameObject active)
        {
            if (observedStriker == active)
                return;

            UnbindStriker();
            observedStriker = active;

            if (active == null) return;

            strikerAttack =
                active.GetComponent<StrikerAttackController>();

            if (strikerAttack == null) return;

            strikerAttack.ShotFired += HandleEnemyShot;
            strikerAttack.ShotBlocked += HandleShieldBlock;
            strikerAttack.PlayerHit += HandlePlayerHit;
        }

        private void UnbindStriker()
        {
            if (strikerAttack != null)
            {
                strikerAttack.ShotFired -= HandleEnemyShot;
                strikerAttack.ShotBlocked -= HandleShieldBlock;
                strikerAttack.PlayerHit -= HandlePlayerHit;
            }

            strikerAttack = null;
            observedStriker = null;
            lastTelegraphing = false;
        }

        private void HandleCatch(BugTarget bug)
        {
            if (game != null && game.IsRunning)
                Play(Sfx.Catch);
        }

        private void HandleWebFire()
        {
            if (game != null && game.IsRunning)
                Play(Sfx.WebFire);
        }

        private void HandleValues()
        {
            if (game == null) return;
            previousHealth = game.Health;
        }

        private void HandleCharge()
        {
            if (overload == null) return;

            bool charged = overload.IsCharged;

            if (charged && !lastCharged)
                Play(Sfx.Charged, true);

            lastCharged = charged;
        }

        private void HandleOverloadFire(Vector3 p, Vector3 d)
        {
            Play(Sfx.OverloadFire);
        }

        private void HandleOverloadHit(Vector3 p)
        {
            Play(Sfx.OverloadHit);
        }

        private void HandleOverloadMiss(Vector3 p)
        {
            Play(Sfx.OverloadMiss);
        }

        private void HandleEnemyShot(StrikerAttackController s)
        {
            Play(Sfx.EnemyShot);
        }

        private void HandleShieldBlock(StrikerAttackController s)
        {
            Play(Sfx.ShieldBlock);
        }

        private void HandlePlayerHit(StrikerAttackController s)
        {
            Play(Sfx.Damage);
        }

        private void HandleGameOver()
        {
            Play(Sfx.GameOver, true);
        }

        private void Play(Sfx id, bool ui = false)
        {
            if (!audioEnabled || source == null ||
                clips == null)
                return;

            AudioClip clip = clips[(int)id];

            if (clip == null) return;

            float categoryVolume =
                ui ? uiVolume : combatVolume;

            source.PlayOneShot(
                clip,
                masterVolume * categoryVolume);
        }

        private void GenerateClips()
        {
            clips = new AudioClip[
                Enum.GetValues(typeof(Sfx)).Length];

            for (int i = 0; i < clips.Length; i++)
            {
                Sfx id = (Sfx)i;
                float duration = Duration(id);
                int count = Mathf.CeilToInt(
                    duration * sampleRate);

                float[] samples = new float[count];
                float smoothNoise = 0f;

                for (int n = 0; n < count; n++)
                {
                    float t = n / (float)sampleRate;
                    float u = t / duration;

                    float noise = Noise(n, i);
                    smoothNoise = Mathf.Lerp(
                        smoothNoise, noise, 0.24f);

                    float value = Synthesize(
                        id, t, u, noise, smoothNoise);

                    // Remove abrupt starts/ends.
                    float attack = Mathf.Clamp01(t / 0.006f);
                    float release = Mathf.Clamp01(
                        (duration - t) / 0.015f);

                    samples[n] = Mathf.Clamp(
                        value * attack * release * 0.65f,
                        -0.9f, 0.9f);
                }

                AudioClip clip = AudioClip.Create(
                    "WS_Generated_" + id,
                    count, 1, sampleRate, false);

                clip.SetData(samples, 0);
                clips[i] = clip;
            }
        }

        private static float Duration(Sfx id)
        {
            switch (id)
            {
                case Sfx.Boot: return 0.75f;
                case Sfx.Start: return 0.48f;
                case Sfx.WebFire: return 0.19f;
                case Sfx.Catch: return 0.25f;
                case Sfx.ShieldOn: return 0.15f;
                case Sfx.ShieldBlock: return 0.28f;
                case Sfx.Warning: return 0.55f;
                case Sfx.EnemyShot: return 0.24f;
                case Sfx.Damage: return 0.35f;
                case Sfx.Charged: return 0.65f;
                case Sfx.OverloadFire: return 0.42f;
                case Sfx.OverloadHit: return 0.55f;
                case Sfx.OverloadMiss: return 0.30f;
                case Sfx.GameOver: return 1.2f;
                default: return 0.3f;
            }
        }

        private static float Noise(int n, int seed)
        {
            unchecked
            {
                uint x = (uint)(n + seed * 7919 + 1);
                x ^= x >> 16;
                x *= 0x7feb352d;
                x ^= x >> 15;
                x *= 0x846ca68b;
                x ^= x >> 16;
                return (x / (float)uint.MaxValue) * 2f - 1f;
            }
        }

        private static float Wave(float frequency, float t)
        {
            return Mathf.Sin(
                2f * Mathf.PI * frequency * t);
        }

        private static float Sweep(
            float startFrequency,
            float endFrequency,
            float t,
            float duration)
        {
            // Integrated linear sweep avoids harsh
            // discontinuities from changing pitch.
            float k =
                (endFrequency - startFrequency) /
                Mathf.Max(0.001f, duration);

            return Mathf.Sin(
                2f * Mathf.PI *
                (startFrequency * t + 0.5f * k * t * t));
        }

        private static float Decay(float u, float power)
        {
            return Mathf.Pow(1f - Mathf.Clamp01(u), power);
        }

        private static float Synthesize(
            Sfx id,
            float t,
            float u,
            float noise,
            float filteredNoise)
        {
            switch (id)
            {
                case Sfx.Boot:
                    return (Wave(440f, t) +
                            0.5f * Wave(660f, t)) *
                           Decay(u, 1.2f) * 0.42f;

                case Sfx.Start:
                    return Sweep(
                        380f, 1100f, t, 0.48f) *
                        Decay(u, 0.4f) * 0.55f;

                case Sfx.WebFire:
                    return (
                        Sweep(1500f, 220f, t, 0.19f) *
                        0.38f +
                        filteredNoise * 0.55f
                    ) * Decay(u, 2.8f);

                case Sfx.Catch:
                    return (
                        Sweep(280f, 900f, t, 0.25f) *
                        0.65f +
                        Wave(1150f, t) * 0.15f
                    ) * Decay(u, 1.8f);

                case Sfx.ShieldOn:
                    return Sweep(
                        600f, 1350f, t, 0.15f) *
                        Decay(u, 1.8f) * 0.42f;

                case Sfx.ShieldBlock:
                    return (
                        filteredNoise * 0.45f +
                        Sweep(950f, 160f, t, 0.28f) *
                        0.65f
                    ) * Decay(u, 3f);

                case Sfx.Warning:
                    return (
                        Wave(720f, t) * 0.45f +
                        Wave(1080f, t) * 0.17f
                    ) * (0.45f + 0.55f *
                        Mathf.Sin(2f * Mathf.PI * 7f * t) *
                        Mathf.Sin(2f * Mathf.PI * 7f * t))
                        * Decay(u, 0.4f);

                case Sfx.EnemyShot:
                    return (
                        Sweep(1250f, 190f, t, 0.24f) *
                        0.7f +
                        filteredNoise * 0.24f
                    ) * Decay(u, 2.2f);

                case Sfx.Damage:
                    return (
                        Sweep(200f, 65f, t, 0.35f) *
                        0.72f +
                        filteredNoise * 0.3f
                    ) * Decay(u, 2.0f);

                case Sfx.Charged:
                    return (
                        Sweep(420f, 1260f, t, 0.65f) *
                        0.48f +
                        Wave(840f, t) * 0.18f
                    ) * Decay(u, 0.65f);

                case Sfx.OverloadFire:
                    return (
                        Sweep(180f, 65f, t, 0.42f) *
                        0.72f +
                        Sweep(1600f, 350f, t, 0.42f) *
                        0.35f +
                        filteredNoise * 0.28f
                    ) * Decay(u, 1.5f);

                case Sfx.OverloadHit:
                    return (
                        Sweep(140f, 45f, t, 0.55f) *
                        0.65f +
                        filteredNoise * 0.62f
                    ) * Decay(u, 2.2f);

                case Sfx.OverloadMiss:
                    return (
                        Sweep(840f, 130f, t, 0.3f) *
                        0.37f +
                        filteredNoise * 0.24f
                    ) * Decay(u, 1.6f);

                case Sfx.GameOver:
                    float first = Sweep(
                        480f, 200f, t, 1.2f);
                    float second = Wave(95f, t);

                    return (
                        first * 0.44f +
                        second * 0.23f
                    ) * Decay(u, 0.7f);
            }

            return 0f;
        }

        private void OnDisable()
        {
            BugTarget.AnyCatchCompleted -= HandleCatch;

            if (game != null)
            {
                game.ValuesChanged -= HandleValues;
                game.RunEnded -= HandleGameOver;
            }

            if (overload != null)
            {
                overload.ChargeChanged -= HandleCharge;
                overload.ShotLaunched -= HandleOverloadFire;
                overload.ShotHit -= HandleOverloadHit;
                overload.ShotMissed -= HandleOverloadMiss;
            }

            if (tether != null)
                tether.WebFired -= HandleWebFire;

            UnbindStriker();
        }

        private void OnDestroy()
        {
            if (clips == null) return;

            foreach (AudioClip clip in clips)
            {
                if (clip != null)
                    Destroy(clip);
            }
        }
    }
}
