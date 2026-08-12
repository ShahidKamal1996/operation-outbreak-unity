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
    /// Font sizes are large numbers on purpose. A TextMeshPro (3D) component viewed
    /// through a PERSPECTIVE camera renders at fontSize * 0.1 world units per em, so a
    /// "size 1" label is only a tenth of a unit tall. There is no world-space Canvas and
    /// no scaled RectTransform anywhere in the chain - the mesh is sized purely by
    /// fontSize * 0.1 - which is why small-looking numbers stay small on screen.
    ///
    /// 1J.2B.3: the real limit is HORIZONTAL, not the font number. At the gate plane the
    /// whole portrait frame is only ~11.3 world units wide, so a label centred over one
    /// gate has ~5.5 units to work with. "FIRE RATE" on one line was already using ~89%
    /// of that, which capped the type at a small size. The title is therefore stacked
    /// onto two lines ("FIRE" / "RATE"), the labels are pulled inboard to x +/-2.84 so
    /// each is centred in its own half of the frame, and both lines are then pushed to
    /// the width limit. Result: title ~2.25x and values ~1.6x their previous cap height.
    ///
    /// Orientation copies the existing camera's ROTATION only (never its position, and it
    /// never writes to the camera), so the text reads flat in the fixed 31-degree portrait
    /// view. Gate geometry, colours and placement are left exactly as approved in 1J.2A.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeGateLabel : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("Upper line. May contain a line break to stack the words, e.g. FIRE\\nRATE.")]
        [TextArea(1, 3)]
        [SerializeField] private string titleText = "FIRE\nRATE";

        [Tooltip("Lower line, e.g. +25%.")]
        [SerializeField] private string valueText = "+25%";

        [Header("Placement (local to this gate)")]
        [Tooltip("Local position of the label. Sits just above the gate top bar.")]
        [SerializeField] private Vector3 localOffset = new Vector3(-2.84f, 4.41f, 14.2f);

        [Tooltip("World width/height of the text block, in units.")]
        [SerializeField] private Vector2 blockSize = new Vector2(11f, 3f);

        [Tooltip("Height of the title above the label origin, in units (camera-up).")]
        [SerializeField] private float titleLineY = 1.204f;

        [Tooltip("Height of the value below the label origin, in units (camera-up).")]
        [SerializeField] private float valueLineY = -1.208f;

        [Header("Style")]
        [SerializeField] private Color textColor = Color.white;
        [Min(0.05f)] [SerializeField] private float titleFontSize = 19.2f;
        [Min(0.05f)] [SerializeField] private float valueFontSize = 21.2f;

        [Tooltip("TMP line spacing for a stacked title, as a percent of the em. Negative tightens.")]
        [SerializeField] private float titleLineSpacing = -33f;

        [Tooltip("Dark outline so the text stays legible against the lane and skybox.")]
        [Range(0f, 1f)] [SerializeField] private float outlineWidth = 0.22f;
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

            CreateLine("Title", titleText, titleFontSize, titleLineY, titleLineSpacing);
            CreateLine("Value", valueText, valueFontSize, valueLineY, 0f);
        }

        private void CreateLine(string lineName, string content, float fontSize, float localY, float lineSpacing)
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
            text.lineSpacing = lineSpacing;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
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
