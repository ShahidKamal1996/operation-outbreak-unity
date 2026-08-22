using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - an EXTREMELY SIMPLE debug mission-select overlay.
    ///
    /// Production mission-select UI is explicitly out of scope for this milestone. This
    /// component exists only so the new progression/selection foundation is drivable by a
    /// human during QA: it lists every Chapter 1 mission with its locked/completed state,
    /// lets the player select+start any UNLOCKED mission, and reloads the gameplay scene so
    /// the selected mission becomes authoritative. It is intentionally plain (no artwork, no
    /// animations) and is grouped under the scene's "MissionSystem" object.
    ///
    /// It consumes the same MissionSelectionService the future production UI will consume, so
    /// the selection contract is exercised exactly as designed - this is a thin view, not a
    /// parallel selection system.
    ///
    /// Scene reload uses the active scene's build index when valid, otherwise its path, so the
    /// debug loop works when Gameplay_Prototype is played directly in the Editor (where it is
    /// not in the build settings). Production scene flow is future work.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionSelectionDebugUi : MonoBehaviour
    {
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private bool verboseLogging = false;

        private MissionSelectionService _selection;
        private GameObject _canvas;
        private GameObject _panel;
        private bool _shown;

        private readonly List<GameObject> _rowButtons = new List<GameObject>();

        private void Awake()
        {
            _selection = new MissionSelectionService(MissionProgressionService.Default);
            Build();
        }

        private void OnEnable()
        {
            _shown = showOnStart;
            Refresh();
        }

        private void Update()
        {
            // Toggle the panel with the M key so it never gets in the way of gameplay QA.
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                Toggle();
            }
        }

        private void Toggle()
        {
            _shown = !_shown;
            Refresh();
        }

        /// <summary>Rebuilds the panel contents from the live selection/progression state.</summary>
        private void Refresh()
        {
            if (_canvas == null)
            {
                return;
            }

            _canvas.SetActive(_shown);

            if (!_shown)
            {
                return;
            }

            for (int i = 0; i < _rowButtons.Count; i++)
            {
                if (_rowButtons[i] != null)
                {
                    Destroy(_rowButtons[i]);
                }
            }

            _rowButtons.Clear();

            IReadOnlyList<MissionDefinition> missions = _selection.Missions;

            for (int i = 0; i < missions.Count; i++)
            {
                MissionDefinition mission = missions[i];
                if (mission == null)
                {
                    continue;
                }

                bool unlocked = _selection.IsUnlocked(mission);
                bool completed = _selection.IsCompleted(mission);
                bool selected = ReferenceEquals(_selection.SelectedMission, mission);

                string state = !unlocked ? "LOCKED" : (completed ? "DONE" : "READY");
                string label = "M" + mission.MissionNumber + "  " + mission.DisplayName +
                               "  [" + state + "]" + (selected ? "  *" : string.Empty);

                Color tint;
                if (!unlocked)
                {
                    tint = new Color(0.22f, 0.22f, 0.22f, 1f);
                }
                else if (completed)
                {
                    tint = new Color(0.18f, 0.55f, 0.30f, 1f);
                }
                else
                {
                    tint = new Color(0.30f, 0.42f, 0.62f, 1f);
                }

                GameObject row = MakeButton(label, tint, unlocked, () => OnMissionClicked(mission));
                row.transform.SetParent(_panel.transform, false);
                _rowButtons.Add(row);
            }

            // A small status line + reset affordance.
            string selectedName = _selection.HasSelection
                ? _selection.SelectedMission.DisplayName
                : "(none)";
            GameObject status = MakeButton(
                "SELECTED: " + selectedName + "   (M: toggle)",
                new Color(0.12f, 0.12f, 0.12f, 1f), false, null);
            status.transform.SetParent(_panel.transform, false);
            _rowButtons.Add(status);

            GameObject reset = MakeButton(
                "RESET PROGRESSION (dev)",
                new Color(0.5f, 0.2f, 0.2f, 1f), true, OnResetClicked);
            reset.transform.SetParent(_panel.transform, false);
            _rowButtons.Add(reset);
        }

        private void OnMissionClicked(MissionDefinition mission)
        {
            if (!_selection.IsUnlocked(mission))
            {
                if (verboseLogging)
                {
                    Debug.Log("[1X DEBUG] Mission '" + mission.MissionId + "' is locked.", this);
                }

                return;
            }

            _selection.Select(mission);
            bool started = _selection.StartSelected();

            if (!started)
            {
                Debug.LogWarning(
                    "[1X DEBUG] Could not start mission '" + mission.MissionId + "'.", this);
                return;
            }

            if (verboseLogging)
            {
                Debug.Log("[1X DEBUG] Starting mission '" + mission.MissionId + "'.", this);
            }

            ReloadGameplayScene();
        }

        private void OnResetClicked()
        {
            MissionProgressionService.Default.Reset();
            MissionProgressionService.InvalidateDefaultCache();
            _selection = new MissionSelectionService(MissionProgressionService.Default);

            if (verboseLogging)
            {
                Debug.Log("[1X DEBUG] Mission progression reset.", this);
            }

            Refresh();
        }

        private void ReloadGameplayScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid())
            {
                if (active.buildIndex >= 0)
                {
                    SceneManager.LoadScene(active.buildIndex);
                }
                else if (!string.IsNullOrEmpty(active.path))
                {
                    SceneManager.LoadScene(active.path);
                }
            }
        }

        // ------------------------------------------------------------------ UI construction

        private void Build()
        {
            _canvas = new GameObject("MissionSelectionDebugCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas.transform.SetParent(transform, false);

            Canvas canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5; // Below Mission Complete (30) and Game Over so it never hides a result.

            CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            // The scene needs exactly one EventSystem for clicks.
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("DebugEventSystem",
                    typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            _panel = new GameObject("DebugPanel",
                typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _panel.transform.SetParent(_canvas.transform, false);

            RectTransform panelRect = (RectTransform)_panel.transform;
            // Bottom-left so it never covers the top-left health HUD or the section/result banners
            // during verified gameplay QA. It grows upward (ContentSizeFitter) and is hidden by
            // default (toggled with the M key) so it only appears when a tester asks for it.
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 0f);
            panelRect.pivot = new Vector2(0f, 0f);
            panelRect.anchoredPosition = new Vector2(24f, 24f);
            panelRect.sizeDelta = new Vector2(520f, 720f);

            _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            VerticalLayoutGroup group = _panel.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(12, 12, 12, 12);
            group.spacing = 6f;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;

            ContentSizeFitter fitter = _panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private static GameObject MakeButton(string label, Color tint, bool interactable,
            UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject("Row",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));

            Image image = go.GetComponent<Image>();
            image.color = tint;

            LayoutElement layout = go.GetComponent<LayoutElement>();
            layout.preferredHeight = 56f;
            layout.minHeight = 56f;

            Button button = go.GetComponent<Button>();
            button.interactable = interactable;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            // Label as a child so the button Image is the only raycast target (matches the
            // 1V QA fix: oversized text labels must never intercept clicks).
            GameObject textGo = new GameObject("Label",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);

            RectTransform textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.text = label;
            text.fontSize = 26f;
            // TextMeshPro's middle-row (vertically centered) horizontal-left alignment is
            // 'Left' (NOT 'MiddleLeft', which is not a member of this TMP version).
            text.alignment = TextAlignmentOptions.Left;
            text.color = interactable ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            text.raycastTarget = false;

            return go;
        }
    }
}
