using UnityEngine;

namespace WebSnatcher
{
    /// <summary>Small temporary headset-relative HUD. Replace with the final suit HUD later.</summary>
    [DisallowMultipleComponent]
    public sealed class SurvivalStatusHUD : MonoBehaviour
    {
        [SerializeField] private SurvivalGameManager game;
        [SerializeField] private SurvivalOverloadController overload;
        [SerializeField] private Transform head;
        [SerializeField] private Vector3 localOffset = new Vector3(-0.32f, 0.20f, 1.10f);
        [SerializeField, Min(0.05f)] private float refreshSeconds = 0.15f;
        [SerializeField] private Color textColor = new Color(0.93f, 0.83f, 0.7f);

        private TextMesh display;
        private float nextRefresh;

        private void Start()
        {
            if (game == null) game = FindFirstObjectByType<SurvivalGameManager>();
            if (overload == null) overload = FindFirstObjectByType<SurvivalOverloadController>();
            if (head == null)
            {
                OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig != null) head = rig.centerEyeAnchor;
                if (head == null && Camera.main != null) head = Camera.main.transform;
            }
            if (head == null) { Debug.LogWarning("[WS HUD] No headset anchor.", this); return; }
            var root = new GameObject("WS_Survival_Status_Runtime");
            root.transform.SetParent(head, false);
            root.transform.localPosition = localOffset;
            root.transform.localRotation = Quaternion.identity;
            display = root.AddComponent<TextMesh>();
            display.anchor = TextAnchor.UpperLeft;
            display.alignment = TextAlignment.Left;
            display.fontSize = 50;
            display.characterSize = 0.0036f;
            display.color = textColor;
        }

        private void Update()
        {
            if (game == null || display == null || Time.time < nextRefresh) return;
            nextRefresh = Time.time + refreshSeconds;
            if (!game.IsRunning)
            {
                display.text = "GAME OVER\nSCORE " + game.Score;
                return;
            }
            int progress = overload != null ? overload.Progress : 0;
            int required = overload != null ? overload.RequiredCatches : 5;
            string power = overload != null && overload.IsCharged ? "READY" : progress + "/" + required;
            display.text = "SCORE  " + game.Score + "\n" +
                "HP     " + game.Health + "\n" +
                "COMBO  x" + game.CurrentMultiplier + "\n" +
                "WEB    " + power + "\n" +
                "TIME   " + Mathf.FloorToInt(game.ElapsedSeconds) + "s";
        }
    }
}
