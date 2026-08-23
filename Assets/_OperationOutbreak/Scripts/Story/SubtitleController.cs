using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z — prototype subtitle/radio presentation. Shows speaker name + dialogue text
    /// via TMP. Built in code (no asset dependency), sortingOrder 15 (above objective HUD 10,
    /// below result overlays 30 and debug 100). raycastTarget=false on all graphics so it never
    /// intercepts clicks. Hides cleanly after each line / on sequence end.
    ///
    /// Radio lines use a slightly different colour and no speaker emphasis (future: radio frame).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SubtitleController : MonoBehaviour
    {
        private Canvas _canvas;
        private TMP_Text _speakerText;
        private TMP_Text _bodyText;
        private GameObject _panel;

        private void Awake() => Build();

        /// <summary>Shows a dialogue line (speaker name + text). isRadio adjusts styling.</summary>
        public void ShowDialogue(string speakerName, string text, bool isRadio)
        {
            if (_panel == null) return;

            _panel.SetActive(true);

            if (_speakerText != null)
            {
                _speakerText.text = string.IsNullOrEmpty(speakerName) ? "" : speakerName;
                _speakerText.color = isRadio ? new Color(0.6f, 0.8f, 1f, 1f) : new Color(1f, 0.85f, 0.4f, 1f);
            }

            if (_bodyText != null)
            {
                _bodyText.text = text ?? string.Empty;
                _bodyText.color = isRadio ? new Color(0.85f, 0.92f, 1f, 1f) : Color.white;
            }
        }

        /// <summary>Hides the subtitle panel.</summary>
        public void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private void Build()
        {
            _canvas = new GameObject("SubtitleCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            _canvas.transform.SetParent(transform, false);
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 15;

            CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            _panel = new GameObject("SubtitlePanel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(_canvas.transform, false);
            RectTransform pr = (RectTransform)_panel.transform;
            pr.anchorMin = new Vector2(0f, 0f);
            pr.anchorMax = new Vector2(1f, 0f);
            pr.pivot = new Vector2(0.5f, 0f);
            pr.anchoredPosition = new Vector2(0f, 160f);
            pr.sizeDelta = new Vector2(-48f, 160f);
            _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
            _panel.GetComponent<Image>().raycastTarget = false;

            _speakerText = CreateText("Speaker", _panel.transform, new Vector2(0f, 50f), 34,
                new Color(1f, 0.85f, 0.4f, 1f), TextAlignmentOptions.Left);
            _bodyText = CreateText("Body", _panel.transform, new Vector2(0f, 0f), 30,
                Color.white, TextAlignmentOptions.Left);
            RectTransform bodyRect = (RectTransform)_bodyText.transform;
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 0.5f);

            _panel.SetActive(false);
        }

        private static TMP_Text CreateText(string name, Transform parent, Vector2 pos, float size,
            Color color, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.offsetMin = new Vector2(16f, r.offsetMin.y);
            r.offsetMax = new Vector2(-16f, r.offsetMax.y);
            r.sizeDelta = new Vector2(0f, 60f);

            TMP_Text t = go.GetComponent<TextMeshProUGUI>();
            t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size;
            t.alignment = alignment;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }
    }
}
