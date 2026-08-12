using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1M - temporary "SECTION n / OUTBREAK" banner shown when a section begins.
    ///
    /// Deliberately a sibling of the approved UpgradeNotificationHud rather than a change
    /// to it: same portrait conventions (1080x1920, safe area, TMP default font), its own
    /// canvas, so the shipped Health HUD and Upgrade toast are untouched.
    ///
    /// Placement is the UPPER-THIRD of the screen but BELOW the upgrade toast band, so the
    /// two can never overlap if an upgrade is collected as a section starts. It never
    /// blocks input (no raycast targets, no interactable group) and it always fades out -
    /// there is no permanent section UI.
    ///
    /// Knows nothing about missions or combat: it just shows two lines and fades them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionSectionHud : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds the banner stays fully visible before fading.")]
        [Min(0.1f)] [SerializeField] private float holdDuration = 1.8f;

        [Tooltip("Seconds the fade-out takes after the hold.")]
        [Min(0.05f)] [SerializeField] private float fadeDuration = 0.5f;

        [Tooltip("Seconds the shorter \"ADVANCE\" prompt stays visible.")]
        [Min(0.1f)] [SerializeField] private float promptHoldDuration = 1.6f;

        [Header("Portrait Layout")]
        [Tooltip("Distance from the top of the safe area, in 1080x1920 reference pixels. " +
                 "Sits below the upgrade toast band so the two never collide.")]
        [SerializeField] private float topOffset = 430f;

        [SerializeField] private Vector2 panelSize = new Vector2(760f, 132f);

        [Header("Prompt")]
        [Tooltip("Shown after a section is cleared, telling the player to keep moving.")]
        [SerializeField] private string advanceLine = "AREA CLEAR";

        [SerializeField] private string advanceSubtitle = "MOVE UP";

        private CanvasGroup _group;
        private TMP_Text _titleText;
        private TMP_Text _subtitleText;
        private Image _accent;
        private float _timer;
        private float _hold;
        private bool _isShowing;
        private bool _isBuilt;

        private static readonly Color SectionTint = new Color(1f, 0.78f, 0.2f, 1f);
        private static readonly Color ClearTint = new Color(0.35f, 0.85f, 0.95f, 1f);

        private void Awake()
        {
            BuildHud();
        }

        private void BuildHud()
        {
            if (_isBuilt)
            {
                return;
            }

            _isBuilt = true;

            GameObject canvasObject = new GameObject(
                "MissionSection_Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            // Above the health HUD (10), below the upgrade toast (20) and the
            // Game Over / Mission Complete overlays (30).
            canvas.sortingOrder = 15;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform safeArea = CreateRect("SafeArea", canvasObject.transform);
            Rect safe = Screen.safeArea;
            safeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;

            RectTransform panel = CreateRect("SectionPanel", safeArea);
            panel.anchorMin = new Vector2(0.5f, 1f);
            panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.sizeDelta = panelSize;
            panel.anchoredPosition = new Vector2(0f, -topOffset);

            _group = panel.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.05f, 0.05f, 0.07f, 0.7f);
            background.raycastTarget = false;

            RectTransform accentRect = CreateRect("Accent", panel);
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(0f, 6f);
            accentRect.anchoredPosition = Vector2.zero;
            _accent = accentRect.gameObject.AddComponent<Image>();
            _accent.color = SectionTint;
            _accent.raycastTarget = false;

            _titleText = CreateText("SectionLine", panel, 52f, FontStyles.Bold);
            RectTransform titleRect = (RectTransform)_titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(-32f, 62f);
            titleRect.anchoredPosition = new Vector2(0f, -14f);
            _titleText.alignment = TextAlignmentOptions.Center;

            _subtitleText = CreateText("SubtitleLine", panel, 32f, FontStyles.Bold);
            RectTransform subtitleRect = (RectTransform)_subtitleText.transform;
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0.5f, 1f);
            subtitleRect.sizeDelta = new Vector2(-32f, 42f);
            subtitleRect.anchoredPosition = new Vector2(0f, -78f);
            _subtitleText.alignment = TextAlignmentOptions.Center;
            _subtitleText.color = new Color(0.78f, 0.84f, 0.9f, 1f);
        }

        /// <summary>Shows "&lt;title&gt; / &lt;subtitle&gt;" and restarts the fade timer.</summary>
        public void Show(string title, string subtitle)
        {
            Present(title, subtitle, SectionTint, holdDuration);
        }

        /// <summary>Short "AREA CLEAR / MOVE UP" nudge shown between sections.</summary>
        public void ShowAdvancePrompt()
        {
            Present(advanceLine, advanceSubtitle, ClearTint, promptHoldDuration);
        }

        private void Present(string title, string subtitle, Color tint, float hold)
        {
            BuildHud();

            if (_titleText != null)
            {
                _titleText.text = title;
                _titleText.color = Color.white;
            }

            if (_subtitleText != null)
            {
                _subtitleText.text = subtitle;
            }

            if (_accent != null)
            {
                Color accent = tint;
                accent.a = 1f;
                _accent.color = accent;
            }

            if (_group != null)
            {
                _group.alpha = 1f;
            }

            _hold = hold;
            _timer = 0f;
            _isShowing = true;
        }

        private void Update()
        {
            if (!_isShowing || _group == null)
            {
                return;
            }

            _timer += Time.deltaTime;

            if (_timer <= _hold)
            {
                return;
            }

            float fade = Mathf.Clamp01((_timer - _hold) / Mathf.Max(0.0001f, fadeDuration));
            _group.alpha = 1f - fade;

            if (fade >= 1f)
            {
                _isShowing = false;
                _group.alpha = 0f;
            }
        }

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            GameObject child = new GameObject(objectName, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RectTransform>();
        }

        private static TMP_Text CreateText(string objectName, Transform parent, float fontSize, FontStyles fontStyle)
        {
            RectTransform textRect = CreateRect(objectName, parent);
            TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
