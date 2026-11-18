using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace WebSnatcher
{
    /// <summary>Owns one endless run: health, score, Scout population, Striker lifetime, restart.</summary>
    [DisallowMultipleComponent]
    public sealed class SurvivalGameManager : MonoBehaviour
    {

        [Header("Startup")]
        [SerializeField] private bool startImmediately = false;

        [Header("Scene dependencies")]
        [SerializeField] private MRUKBugSpawner scoutSpawner;
        [SerializeField] private GameObject strikerPrefab;
        [SerializeField] private Transform strikerParent;
        [SerializeField] private SurvivalOverloadController overload;
        [SerializeField] private GazeTargeting gaze;
        [SerializeField] private WebTetherController tether;
        [SerializeField] private WebShieldController shield;

        [Header("Health and scoring")]
        [SerializeField, Min(1)] private int startingHealth = 5;
        [SerializeField, Min(1)] private int scoutPoints = 100;
        [SerializeField, Min(1)] private int strikerPoints = 500;
        [SerializeField, Min(0)] private int shieldBlockPoints = 25;
        [SerializeField, Min(1f)] private float comboTimeoutSeconds = 5f;
        [SerializeField, Range(1, 5)] private int maximumScoreMultiplier = 5;

        [Header("Difficulty - 30-second stages")]
        [SerializeField, Min(5f)] private float difficultyStageSeconds = 30f;
        [SerializeField, Range(1, 12)] private int startingScouts = 4;
        [SerializeField, Range(1, 12)] private int scoutPopulationCap = 6;
        [SerializeField, Min(1f)] private float maxSpeedMultiplier = 2.2f;

        [Header("Striker scheduling")]
        [SerializeField, Min(0f)] private float firstStrikerDelay = 20f;
        [SerializeField, Min(5f)] private float strikerRespawnAfterDefeat = 35f;
        [SerializeField, Min(5f)] private float retrySpawnSeconds = 3f;

        [Header("Game over")]
        [SerializeField] private bool restartAutomatically = true;
        [SerializeField, Min(2f)] private float restartDelay = 8f;
        [SerializeField] private bool logRunEvents = true;

        public event Action ValuesChanged;
        public event Action RunEnded;
        public int Health { get; private set; }
        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int DifficultyStage { get; private set; }
        public float ElapsedSeconds =>
            started
                ? Mathf.Max(0f,
                    (IsRunning ? Time.time : gameOverAt) - startTime)
                : 0f;
        public bool IsRunning { get; private set; }
        public GameObject ActiveStriker { get; private set; }
        public int CurrentMultiplier => Mathf.Clamp(1 + Combo / 5, 1, maximumScoreMultiplier);

        private StrikerAttackController activeAttack;
        private float startTime;
        private float lastCatchAt;
        private float nextStrikerAt;
        private float gameOverAt;
        private bool started;
        private bool restarting;

        private void Awake()
        {
            if (scoutSpawner == null) scoutSpawner = FindFirstObjectByType<MRUKBugSpawner>();
            if (overload == null) overload = FindFirstObjectByType<SurvivalOverloadController>();
            if (gaze == null) gaze = FindFirstObjectByType<GazeTargeting>();
            if (tether == null) tether = FindFirstObjectByType<WebTetherController>();
            if (shield == null) shield = FindFirstObjectByType<WebShieldController>();
            if (strikerParent == null) strikerParent = transform;
        }

        private void OnEnable()
        {
            BugTarget.AnyCatchCompleted += OnScoutCaught;
        }

        private void OnDisable()
        {
            BugTarget.AnyCatchCompleted -= OnScoutCaught;
            UnbindStriker();
        }

        private void Start()
        {
            Health = startingHealth;
            Score = 0;
            Combo = 0;
            DifficultyStage = 0;

            lastCatchAt = float.NegativeInfinity;
            started = false;
            IsRunning = false;

            if (overload != null)
                overload.Initialize(this);

            if (!startImmediately)
            {
                // Keep combat inactive during the onboarding sequence.
                if (scoutSpawner != null)
                    scoutSpawner.enabled = false;

                if (tether != null)
                    tether.enabled = false;

                if (shield != null)
                    shield.enabled = false;

                if (gaze != null)
                    gaze.SetTargetingEnabled(false);

                Notify();
                return;
            }

            BeginRun();
        }

        public void BeginRun()
        {
            if (started || IsRunning)
                return;

            startTime = Time.time;
            nextStrikerAt = Time.time + firstStrikerDelay;

            started = true;
            IsRunning = true;

            if (scoutSpawner != null)
                scoutSpawner.enabled = true;

            if (tether != null)
                tether.enabled = true;

            if (shield != null)
                shield.enabled = true;

            if (gaze != null)
                gaze.SetTargetingEnabled(true);

            ApplyDifficulty();
            Notify();

            if (logRunEvents)
                Debug.Log("[WS Survival] RUN STARTED", this);
        }

        private void Update()
        {
            if (!started) return;
#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                RestartScene();
                return;
            }
#endif
            if (!IsRunning)
            {
                if (restartAutomatically && Time.time - gameOverAt >= restartDelay) RestartScene();
                return;
            }

            if (Combo > 0 && Time.time - lastCatchAt >= comboTimeoutSeconds)
            {
                Combo = 0;
                Notify();
            }

            int stage = Mathf.FloorToInt(ElapsedSeconds / difficultyStageSeconds);
            if (stage != DifficultyStage)
            {
                DifficultyStage = stage;
                ApplyDifficulty();
                Notify();
            }

            if (ActiveStriker == null && Time.time >= nextStrikerAt)
                TrySpawnStriker();
        }

        private void OnScoutCaught(BugTarget bug)
        {
            if (!IsRunning) return;
            if (Time.time - lastCatchAt > comboTimeoutSeconds) Combo = 0;
            Combo++;
            lastCatchAt = Time.time;
            Score += scoutPoints * CurrentMultiplier;
            if (overload != null) overload.AddCatch();
            Notify();
        }

        private void ApplyDifficulty()
        {
            float speed = Mathf.Min(maxSpeedMultiplier, 1f + DifficultyStage * 0.12f);
            int population = Mathf.Min(scoutPopulationCap, startingScouts + DifficultyStage / 2);
            if (scoutSpawner != null)
            {
                scoutSpawner.SetPopulation(population);
                scoutSpawner.SetDifficultyMultiplier(speed);
            }
            if (ActiveStriker != null)
            {
                var flight = ActiveStriker.GetComponent<FlyingStrikerController>();
                if (flight != null) flight.SetDifficultyMultiplier(speed);
                if (activeAttack != null) activeAttack.SetDifficultyMultiplier(speed);
            }
        }

        private void TrySpawnStriker()
        {
            if (strikerPrefab == null)
            {
                if (logRunEvents) Debug.LogWarning("[WS Survival] Assign PF_StrikerBug prefab to Striker Prefab.", this);
                nextStrikerAt = Time.time + retrySpawnSeconds;
                return;
            }
            ActiveStriker = Instantiate(strikerPrefab, strikerParent);
            ActiveStriker.name = "WS_Active_Striker";
            // Flight controller relocates this instance to an MRUK-safe point.
            activeAttack = ActiveStriker.GetComponent<StrikerAttackController>();
            if (activeAttack != null)
            {
                activeAttack.PlayerHit += OnPlayerHit;
                activeAttack.ShotBlocked += OnShotBlocked;
            }
            ApplyDifficulty();
            if (logRunEvents) Debug.Log("[WS Survival] Striker spawned", this);
            Notify();
        }

        private void OnPlayerHit(StrikerAttackController source) => TakeDamage(1);

        private void OnShotBlocked(StrikerAttackController source)
        {
            if (!IsRunning) return;
            Score += shieldBlockPoints;
            Notify();
        }

        public void TakeDamage(int amount)
        {
            if (!IsRunning || amount <= 0) return;
            Health = Mathf.Max(0, Health - amount);
            Combo = 0;
            if (logRunEvents) Debug.Log("[WS Survival] Player hit, HP=" + Health, this);
            Notify();
            if (Health == 0) EndRun();
        }

        public void DefeatStriker(GameObject target)
        {
            if (!IsRunning || target == null || target != ActiveStriker) return;
            Score += strikerPoints;
            UnbindStriker();
            Destroy(ActiveStriker);
            ActiveStriker = null;
            nextStrikerAt = Time.time + strikerRespawnAfterDefeat;
            if (logRunEvents) Debug.Log("[WS Survival] Striker defeated +" + strikerPoints, this);
            Notify();
        }

        private void UnbindStriker()
        {
            if (activeAttack != null)
            {
                activeAttack.PlayerHit -= OnPlayerHit;
                activeAttack.ShotBlocked -= OnShotBlocked;
            }
            activeAttack = null;
        }

        private void EndRun()
        {
            IsRunning = false;
            gameOverAt = Time.time;
            if (scoutSpawner != null) scoutSpawner.enabled = false;
            if (activeAttack != null) activeAttack.enabled = false;
            if (ActiveStriker != null)
            {
                var flight = ActiveStriker.GetComponent<FlyingStrikerController>();
                if (flight != null) flight.enabled = false;
            }
            if (overload != null) overload.enabled = false;
            if (tether != null) tether.enabled = false;
            if (gaze != null) gaze.SetTargetingEnabled(false);
            if (shield != null) shield.enabled = false;
            if (logRunEvents) Debug.Log("[WS Survival] GAME OVER. Score=" + Score, this);
            RunEnded?.Invoke();
            Notify();
        }

        private void Notify() => ValuesChanged?.Invoke();

        public void RestartScene()
        {
            if (restarting) return;
            restarting = true;
            Scene current = SceneManager.GetActiveScene();
            SceneManager.LoadScene(current.buildIndex >= 0 ? current.buildIndex : 0);
        }
    }
}
