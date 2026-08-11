using OperationOutbreak.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OperationOutbreak.UI
{
    /// <summary>
    /// Event-driven prototype combat HUD. It builds a single screen-space canvas and never
    /// samples the world or searches the scene during gameplay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHealthHud : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private PlayerHealth playerHealth;

        [Header("Portrait Layout")]
        [SerializeField] private Vector2 panelSize = new Vector2(300f, 118f);
        [SerializeField] private Vector2 padding = new Vector2(36f, 36f);

        private static Sprite _fillSprite;

        private Image _fillImage;
        private TMP_Text _healthText;
        private bool _isBuilt;

        private void Awake()
        {
            BuildHud();
        }

        private void OnEnable()
        {
            if (playerHealth != null)
            {
                playerHealth.HealthChanged += UpdateHealth;
                UpdateHealth(playerHealth.CurrentHealth, playerHealth.MaxHealth);
            }
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.HealthChanged -= UpdateHealth;
            }
        }

        private void BuildHud()
        {
            if (_isBuilt)
            {
                return;
            }

            _isBuilt = true;
            GameObject canvasObject = new GameObject("CombatHUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform safeArea = CreateRect("SafeArea", canvasObject.transform);
            ApplySafeArea(safeArea);

            RectTransform panel = CreateRect("HealthPanel", safeArea);
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.sizeDelta = panelSize;
            panel.anchoredPosition = new Vector2(padding.x, -padding.y);
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.04f, 0.06f, 0.08f, 0.82f);

            TMP_Text label = CreateText("HP", panel, 28f, FontStyles.Bold);
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.sizeDelta = new Vector2(100f, 34f);
            labelRect.anchoredPosition = new Vector2(18f, -14f);
            label.alignment = TextAlignmentOptions.Left;

            RectTransform barBackground = CreateRect("HealthBar", panel);
            barBackground.anchorMin = new Vector2(0f, 1f);
            barBackground.anchorMax = new Vector2(1f, 1f);
            barBackground.pivot = new Vector2(0.5f, 1f);
            barBackground.sizeDelta = new Vector2(-36f, 26f);
            barBackground.anchoredPosition = new Vector2(0f, -55f);
            Image backgroundImage = barBackground.gameObject.AddComponent<Image>();
            backgroundImage.color = new Color(0.14f, 0.16f, 0.18f, 1f);

            RectTransform fill = CreateRect("Fill", barBackground);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            _fillImage = fill.gameObject.AddComponent<Image>();
            // A runtime-created Image has no source sprite by default. In that state Unity
            // uses its simple-mesh fallback and ignores Image.fillAmount. Supply a sprite so
            // the Filled image path is used for the green health fill.
            _fillImage.sprite = GetFillSprite();
            _fillImage.type = Image.Type.Filled;
            _fillImage.fillMethod = Image.FillMethod.Horizontal;
            _fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fillImage.color = new Color(0.2f, 0.82f, 0.32f, 1f);

            _healthText = CreateText("HealthText", panel, 26f, FontStyles.Bold);
            RectTransform healthTextRect = (RectTransform)_healthText.transform;
            healthTextRect.anchorMin = new Vector2(0f, 0f);
            healthTextRect.anchorMax = new Vector2(1f, 0f);
            healthTextRect.pivot = new Vector2(0.5f, 0f);
            healthTextRect.sizeDelta = new Vector2(-36f, 34f);
            healthTextRect.anchoredPosition = new Vector2(0f, 12f);
            _healthText.alignment = TextAlignmentOptions.Center;
        }

        private static Sprite GetFillSprite()
        {
            if (_fillSprite == null)
            {
                _fillSprite = Sprite.Create(
                    Texture2D.whiteTexture,
                    new Rect(0f, 0f, 1f, 1f),
                    new Vector2(0.5f, 0.5f),
                    1f);
            }

            return _fillSprite;
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

        private static void ApplySafeArea(RectTransform safeArea)
        {
            Rect safe = Screen.safeArea;
            safeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;
        }

        private void UpdateHealth(int currentHealth, int maxHealth)
        {
            if (!_isBuilt || maxHealth <= 0)
            {
                return;
            }

            float normalizedHealth = Mathf.Clamp01((float)currentHealth / maxHealth);
            _fillImage.fillAmount = normalizedHealth;
            _healthText.text = $"{currentHealth} / {maxHealth}";
        }
    }
}
