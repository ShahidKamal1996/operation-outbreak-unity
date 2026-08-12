using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OperationOutbreak.UI
{
    /// <summary>
    /// Milestone 1L-R - temporary "UPGRADE ACQUIRED" toast.
    ///
    /// Built at runtime on its own screen-space canvas using the same portrait
    /// conventions as the approved PlayerHealthHud (1080x1920 reference, safe area,
    /// TMP_Settings.defaultFontAsset). It does NOT touch the health HUD or its styling -
    /// it is a separate canvas so the approved HUD is left exactly as shipped.
    ///
    /// Placed in the UPPER-MIDDLE of the portrait screen: clear of the health panel in
    /// the top-left and clear of the Game Over / Mission Complete overlays.
    ///
    /// The component only knows how to show a line of text and fade it out. It has no
    /// idea what an upgrade is, which keeps HUD separate from effect and progression.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeNotificationHud : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds the notification stays fully visible before fading.")]
        [Min(0.1f)] [SerializeField] private float holdDuration = 1.5f;

        [Tooltip("Seconds the fade-out takes after the hold.")]
        [Min(0.05f)] [SerializeField] private float fadeDuration = 0.35f;

        [Header("Portrait Layout")]
        [Tooltip("Distance from the top of the safe area, in 1080x1920 reference pixels.")]
        [SerializeField] private float topOffset = 210f;

        [SerializeField] private Vector2 panelSize = new Vector2(720f, 150f);

        private CanvasGroup _group;
        private TMP_Text _titleText;
        private TMP_Text _subtitleText;
        private Image _accent;
        private float _timer;
        private bool _isShowing;
        private bool _isBuilt;

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
                "UpgradeNotification_Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            // Above the health HUD (10), below the Game Over / Mission Complete overlays.
            canvas.sortingOrder = 20;

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

            RectTransform panel = CreateRect("NotificationPanel", safeArea);
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
            background.color = new Color(0.04f, 0.06f, 0.08f, 0.85f);
            background.raycastTarget = false;

            // Coloured accent strip, tinted per upgrade.
            RectTransform accentRect = CreateRect("Accent", panel);
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(1f, 0f);
            accentRect.pivot = new Vector2(0.5f, 0f);
            accentRect.sizeDelta = new Vector2(0f, 8f);
            accentRect.anchoredPosition = Vector2.zero;
            _accent = accentRect.gameObject.AddComponent<Image>();
            _accent.color = new Color(1f, 0.55f, 0.12f, 1f);
            _accent.raycastTarget = false;

            _titleText = CreateText("UpgradeLine", panel, 46f, FontStyles.Bold);
            RectTransform titleRect = (RectTransform)_titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(-32f, 58f);
            titleRect.anchoredPosition = new Vector2(0f, -18f);
            _titleText.alignment = TextAlignmentOptions.Center;

            _subtitleText = CreateText("AcquiredLine", panel, 30f, FontStyles.Bold);
            RectTransform subtitleRect = (RectTransform)_subtitleText.transform;
            subtitleRect.anchorMin = new Vector2(0f, 1f);
            subtitleRect.anchorMax = new Vector2(1f, 1f);
            subtitleRect.pivot = new Vector2(0.5f, 1f);
            subtitleRect.sizeDelta = new Vector2(-32f, 40f);
            subtitleRect.anchoredPosition = new Vector2(0f, -80f);
            _subtitleText.alignment = TextAlignmentOptions.Center;
            _subtitleText.color = new Color(0.75f, 0.82f, 0.88f, 1f);
            _subtitleText.text = "UPGRADE ACQUIRED";
        }

        /// <summary>
        /// Shows "&lt;upgradeLine&gt; / UPGRADE ACQUIRED" and restarts the fade timer.
        /// Calling it again while visible simply replaces the text.
        /// </summary>
        public void Show(string upgradeLine, Color tint)
        {
            BuildHud();

            if (_titleText != null)
            {
                _titleText.text = upgradeLine;
                _titleText.color = Color.white;
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

            if (_timer <= holdDuration)
            {
                return;
            }

            float fade = Mathf.Clamp01((_timer - holdDuration) / Mathf.Max(0.0001f, fadeDuration));
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
