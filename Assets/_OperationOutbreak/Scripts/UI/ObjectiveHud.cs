using OperationOutbreak.Mission;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OperationOutbreak.UI
{
    /// <summary>
    /// Milestone 1X.5 - a minimal, prototype objective readout so the player understands the
    /// active objective at a glance:
    ///   SURVIVE   00:18
    ///   BARRICADES 1 / 2
    ///   ACTIVATE   2 / 3
    ///   SECTIONS   1 / 3
    /// It reads the single objective authority (MissionObjectiveController) and shows the first
    /// non-complete REQUIRED objective, formatted per type. It is intentionally plain (a small
    /// anchored TextMeshPro label, no artwork) and rebuilds text on a 0.1 s cadence rather than
    /// every frame. Production objective UI is a later milestone.
    ///
    /// Built in code like the other 1X UIs, on its own ScreenSpaceOverlay canvas (sortingOrder 10
    /// - above gameplay, below the result overlays at 30 and the debug panel at 100).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveHud : MonoBehaviour
    {
        [SerializeField] private MissionObjectiveController objectiveController;

        private TMP_Text _label;
        private float _nextRefreshTime;
        private const float RefreshInterval = 0.1f;

        private void Awake()
        {
            if (objectiveController == null)
            {
                objectiveController = FindAnyObjectByType<MissionObjectiveController>();
            }

            Build();
        }

        private void OnEnable()
        {
            _nextRefreshTime = 0f;
        }

        private void Update()
        {
            if (Time.time < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.time + RefreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            if (_label == null)
            {
                return;
            }

            _label.text = FormatActiveObjective();
        }

        private string FormatActiveObjective()
        {
            if (objectiveController == null)
            {
                return string.Empty;
            }

            System.Collections.Generic.IReadOnlyList<MissionObjectiveRuntime> objectives =
                objectiveController.Objectives;

            MissionObjectiveRuntime focus = null;
            for (int i = 0; i < objectives.Count; i++)
            {
                MissionObjectiveRuntime objective = objectives[i];
                if (objective == null || !objective.Required || objective.IsComplete)
                {
                    continue;
                }

                focus = objective;
                break;
            }

            if (focus == null)
            {
                return string.Empty;
            }

            switch (focus.Type)
            {
                case MissionObjectiveType.SurviveDuration:
                {
                    float remaining = Mathf.Max(0f, focus.RequiredDuration - focus.ElapsedSeconds);
                    int seconds = Mathf.CeilToInt(remaining);
                    return "SURVIVE\n" + seconds.ToString("00") + "s";
                }
                case MissionObjectiveType.DestroyTargets:
                    return "BARRICADES\n" + focus.CurrentProgress + " / " + focus.RequiredProgress;
                case MissionObjectiveType.ActivateTargets:
                    return "ACTIVATE\n" + focus.CurrentProgress + " / " + focus.RequiredProgress;
                default:
                    return "SECTIONS\n" + focus.CurrentProgress + " / " + focus.RequiredProgress;
            }
        }

        private void Build()
        {
            GameObject canvasGo = new GameObject("ObjectiveHudCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            GameObject labelGo = new GameObject("ObjectiveLabel",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(canvasGo.transform, false);

            RectTransform r = (RectTransform)labelGo.transform;
            r.anchorMin = new Vector2(0.5f, 1f);
            r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -120f);
            r.sizeDelta = new Vector2(360f, 150f);

            _label = labelGo.GetComponent<TextMeshProUGUI>();
            _label.font = TMP_Settings.defaultFontAsset;
            _label.fontSize = 40f;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = new Color(1f, 0.95f, 0.7f, 1f);
            _label.raycastTarget = false;
            _label.text = string.Empty;
        }
    }
}
