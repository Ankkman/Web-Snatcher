
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WebSnatcher
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WS_HUDScaleController : MonoBehaviour
    {
        [Serializable]
        public class HUDEntry
        {
            [Header("Identification")]
            public string hudName;

            [Tooltip("Runtime object name to search for.")]
            public string objectName;

            [Header("Scale")]
            [Range(0.25f, 4f)]
            public float scaleMultiplier = 1f;

            [Header("Position Offset (meters)")]
            public Vector3 positionOffset = Vector3.zero;

            [Header("Runtime Information")]
            public Transform runtimeTarget;

            [HideInInspector] public Vector3 baseScale;
            [HideInInspector] public Vector3 basePosition;
            [HideInInspector] public bool captured;
        }

        [Header("HUD Overrides")]
        public List<HUDEntry> huds = new List<HUDEntry>()
        {
            new HUDEntry()
            {
                hudName = "Onboarding HUD",
                objectName = "WS_Onboarding_TacticalHUD",
                scaleMultiplier = 1f
            },
            new HUDEntry()
            {
                hudName = "Survival HUD",
                objectName = "WS_TacticalHUD_Runtime",
                scaleMultiplier = 1f
            },
            new HUDEntry()
            {
                hudName = "Overload Targeting",
                objectName = "WS_OverloadTargeting_Runtime",
                scaleMultiplier = 1f
            },
            new HUDEntry()
            {
                hudName = "Game Over HUD",
                objectName = "WS_GameOver_Runtime",
                scaleMultiplier = 1f
            }
        };

        [Header("Settings")]
        public bool applyContinuously = true;

        [Tooltip("Search interval in seconds.")]
        [Min(0.05f)]
        public float searchInterval = 0.5f;

        private float nextSearch;

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            if (Time.unscaledTime >= nextSearch)
            {
                nextSearch =
                    Time.unscaledTime + searchInterval;

                FindHUDs();
            }

            if (applyContinuously)
                ApplyOverrides();
        }

        public void FindHUDs()
        {
            foreach (HUDEntry entry in huds)
            {
                if (entry.runtimeTarget != null)
                    continue;

                GameObject target =
                    FindSceneObject(entry.objectName);

                if (target == null)
                    continue;

                entry.runtimeTarget = target.transform;
                entry.baseScale = target.transform.localScale;
                entry.basePosition =
                    target.transform.localPosition;

                entry.captured = true;

                Debug.Log(
                    "[WS HUD] Found " + entry.hudName,
                    this);
            }
        }

        private GameObject FindSceneObject(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            Transform[] transforms =
                Resources.FindObjectsOfTypeAll<Transform>();

            foreach (Transform t in transforms)
            {
                if (t == null || t.name != name)
                    continue;

                if (!t.gameObject.scene.IsValid())
                    continue;

                return t.gameObject;
            }

            return null;
        }

        public void ApplyOverrides()
        {
            foreach (HUDEntry entry in huds)
            {
                if (entry.runtimeTarget == null ||
                    !entry.captured)
                    continue;

                entry.runtimeTarget.localScale =
                    entry.baseScale *
                    entry.scaleMultiplier;

                entry.runtimeTarget.localPosition =
                    entry.basePosition +
                    entry.positionOffset;
            }
        }

        [ContextMenu("Find HUDs Now")]
        private void FindNow()
        {
            FindHUDs();
        }

        [ContextMenu("Apply Overrides Now")]
        private void ApplyNow()
        {
            ApplyOverrides();
        }

        [ContextMenu("Reset Multipliers")]
        private void ResetMultipliers()
        {
            foreach (HUDEntry entry in huds)
            {
                entry.scaleMultiplier = 1f;
                entry.positionOffset = Vector3.zero;
            }

            ApplyOverrides();
        }
    }
}
