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

        /// <summary>
        /// The debug canvas sortingOrder. MUST be higher than the Mission Complete and Game Over
        /// overlay canvases (both sortingOrder 30, with full-screen raycast-target Images): if the
        /// debug canvas renders below them, their full-screen Image intercepts every pointer click
        /// and the debug mission buttons become unclickable while a result overlay is up (the QA
        /// fix #4 root cause). 100 keeps the debug panel on top so it stays clickable without
        /// spatially overlapping the centred Retry/Return buttons.
        /// </summary>
        public const int DebugCanvasSortingOrder = 100;

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

            // One concise diagnostic dump each time the panel is opened (never per-frame) so a
            // human QA can confirm the pointer path end-to-end: EventSystem count + current input
            // module state, the debug canvas sortingOrder, and the topmost raycast hit under the
            // pointer (which reveals whether a result overlay is intercepting).
            if (_shown)
            {
                LogPointerDiagnostics();
            }
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
            // ABOVE the Mission Complete / Game Over overlays (sortingOrder 30, full-screen
            // raycast Images) so the result overlay cannot intercept clicks meant for the debug
            // panel. The centred Retry/Return buttons are not spatially covered by this
            // bottom-left panel, so they stay clickable too.
            canvas.sortingOrder = DebugCanvasSortingOrder;

            CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            // Exactly one EventSystem with an InputSystemUIInputModule for pointer/click input.
            // IMPORTANT (Input System 1.20): InputSystemUIInputModule supplies its OWN default UI
            // actions (Point/Click/etc.), and the older per-action pointAction/leftClickAction
            // API plus InputActionProperty.FromAction were REMOVED in this version - so a freshly
            // created module is clickable out of the box and must NOT be hand-configured with those
            // removed members (the QA #3/#4 code did, which broke compilation).
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

        /// <summary>
        /// Logs a concise, one-shot picture of the UI pointer path so manual Play Mode QA can
        /// be conclusive about WHY a button is or is not clickable. Called once when the panel
        /// is toggled open (never per-frame).
        /// </summary>
        private void LogPointerDiagnostics()
        {
            EventSystem[] systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Debug.Log("[1X] Active EventSystem count: " + systems.Length);

            EventSystem current = EventSystem.current;
            if (current == null)
            {
                Debug.LogWarning("[1X] EventSystem.current is null - UI clicks will not work.");
                return;
            }

            BaseInputModule module = current.currentInputModule;
            Debug.Log("[1X] Current input module: " + (module != null ? module.GetType().Name : "null") +
                      ", enabled=" + (module != null && module.enabled));

            if (module is InputSystemUIInputModule uiModule)
            {
                // Input System 1.20: the module supplies its own default UI actions (the per-action
                // pointAction/leftClickAction API was removed), so report the actionsAsset state
                // (default when null) rather than individual removed action properties.
                Debug.Log("[1X] Input module actionsAsset: " +
                          (uiModule.actionsAsset != null ? uiModule.actionsAsset.name : "(module defaults)"));
            }

            Canvas debugCanvas = _canvas != null ? _canvas.GetComponent<Canvas>() : null;
            Debug.Log("[1X] Debug canvas sortingOrder: " +
                      (debugCanvas != null ? debugCanvas.sortingOrder : -1) +
                      " (result overlays are 30; debug must be higher to stay clickable).");

            Vector2 probe = ResolvePointerPosition();
            PointerEventData ped = new PointerEventData(current) { position = probe };
            List<RaycastResult> hits = new List<RaycastResult>();
            current.RaycastAll(ped, hits);
            Debug.Log("[1X] Pointer raycast top hit @ " + probe + ": " +
                      (hits.Count > 0
                          ? hits[0].gameObject.name + " (canvas sortingOrder " + hits[0].sortingOrder + ")"
                          : "(nothing - pointer is over non-UI space)"));
        }

        private static Vector2 ResolvePointerPosition()
        {
            // activeInputHandler is 1 (new Input System only), so use the InputSystem mouse,
            // not UnityEngine.Input.mousePosition. Fall back to screen centre on touch-only.
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }

            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
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
