using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace OperationOutbreak.Upgrades
{
    /// <summary>
    /// Milestone 1J.2B - builds the readable world-space text shown on one upgrade gate.
    ///
    /// The label is generated at runtime with TextMesh Pro, matching the approach already
    /// approved for PlayerHealthHud and GameOverController. It is a pure visual: the text
    /// meshes carry no collider, so the label can never block the Player's passage.
    ///
    /// Orientation copies the existing camera's ROTATION only (never its position, and it
    /// never writes to the camera), so the text reads flat in the fixed 31-degree portrait
    /// view. Gate geometry, colours and placement are left exactly as approved in 1J.2A.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeGateLabel : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("Upper line, e.g. FIRE RATE.")]
        [SerializeField] private string titleText = "FIRE RATE";

        [Tooltip("Lower line, e.g. +25%.")]
        [SerializeField] private string valueText = "+25%";

        [Header("Placement (local to this gate)")]
        [Tooltip("Local position of the label. Sits just above the gate top bar.")]
        [SerializeField] private Vector3 localOffset = new Vector3(-2.9f, 4.5f, 13.5f);

        [Tooltip("World width/height of the text block, in units.")]
        [SerializeField] private Vector2 blockSize = new Vector2(6f, 1.1f);

        [Tooltip("Vertical gap between the two lines, in units.")]
        [Min(0f)] [SerializeField] private float lineGap = 0.62f;

        [Header("Style")]
        [SerializeField] private Color textColor = Color.white;
        [Min(0.05f)] [SerializeField] private float titleFontSize = 0.9f;
        [Min(0.05f)] [SerializeField] private float valueFontSize = 1.55f;

        [Tooltip("Dark outline so the text stays legible against the lane and skybox.")]
        [Range(0f, 1f)] [SerializeField] private float outlineWidth = 0.18f;
        [SerializeField] private Color outlineColor = new Color(0.04f, 0.05f, 0.07f, 1f);

        [Header("Facing")]
        [Tooltip("Copy the camera's rotation so the text reads flat in the portrait view.")]
        [SerializeField] private bool matchCameraRotation = true;

        [Tooltip("Camera used for orientation only. Resolved from the scene at Awake when empty.")]
        [SerializeField] private Camera viewCamera;

        private Transform _labelRoot;

        private void Awake()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            BuildLabel();
            AlignToCamera();
        }

        private void BuildLabel()
        {
            if (_labelRoot != null)
            {
                return;
            }

            GameObject root = new GameObject($"{name}_Label");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = localOffset;
            _labelRoot = root.transform;

            CreateLine("Title", titleText, titleFontSize, lineGap * 0.5f);
            CreateLine("Value", valueText, valueFontSize, -lineGap * 0.5f);
        }

        private void CreateLine(string lineName, string content, float fontSize, float localY)
        {
            // TextMeshPro derives from Graphic, so the RectTransform is created up front.
            GameObject line = new GameObject(lineName, typeof(RectTransform));
            line.transform.SetParent(_labelRoot, false);

            RectTransform rect = (RectTransform)line.transform;
            rect.sizeDelta = blockSize;
            rect.localPosition = new Vector3(0f, localY, 0f);

            TextMeshPro text = line.AddComponent<TextMeshPro>();
            text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = textColor;

            // Dark rim keeps the words readable over both the lane and the sky.
            if (outlineWidth > 0f)
            {
                text.outlineWidth = outlineWidth;
                text.outlineColor = outlineColor;
            }

            // A prototype signpost should stay legible rather than be lit like geometry.
            MeshRenderer meshRenderer = line.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }
        }

        private void AlignToCamera()
        {
            if (!matchCameraRotation || _labelRoot == null || viewCamera == null)
            {
                return;
            }

            // Rotation only - the camera is read, never modified.
            _labelRoot.rotation = viewCamera.transform.rotation;
        }
    }
}
