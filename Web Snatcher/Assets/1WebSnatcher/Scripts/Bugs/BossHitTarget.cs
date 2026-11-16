
using System;
using UnityEngine;

namespace WebSnatcher
{
    [DisallowMultipleComponent]
    public sealed class BossHitTarget : MonoBehaviour
    {
        [SerializeField, Min(1)] private int hitsToDefeat = 3;

        public event Action<int> HitReceived;
        public event Action Defeated;

        public int HitCount { get; private set; }
        public bool IsDefeated { get; private set; }

        public void ReceiveHit()
        {
            if (IsDefeated) return;

            HitCount++;
            HitReceived?.Invoke(HitCount);

            Debug.Log(
                $"[WebSnatcher] BOSS HIT {HitCount}/{hitsToDefeat}",
                this);

            if (HitCount >= hitsToDefeat)
            {
                IsDefeated = true;
                Defeated?.Invoke();
                Debug.Log("[WebSnatcher] BOSS DEFEATED!", this);
            }
        }

        public void ResetBoss()
        {
            HitCount = 0;
            IsDefeated = false;
        }
    }
}
