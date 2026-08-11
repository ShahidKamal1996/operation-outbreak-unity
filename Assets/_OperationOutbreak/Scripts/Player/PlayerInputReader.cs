using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace OperationOutbreak.Player
{
    /// <summary>
    /// Milestone 1B - single source of movement intent for the player.
    /// Reads keyboard (Editor / Standalone testing) and one-finger touch drag (mobile)
    /// and normalises both into ONE 2D move vector:
    ///   x = strafe  (-1 left  .. +1 right)
    ///   y = advance (-1 back  .. +1 forward)
    /// Uses the Input System package device APIs directly, so no project-wide
    /// input settings, action assets or packages have to be modified.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInputReader : MonoBehaviour
    {
        [Header("Keyboard (Editor / Standalone testing)")]
        [Tooltip("W / Up = forward, S / Down = back, A / Left = left, D / Right = right.")]
        [SerializeField] private bool keyboardEnabled = true;

        [Header("Touch drag (Mobile)")]
        [Tooltip("Enables one-finger drag steering. No on-screen joystick is drawn.")]
        [SerializeField] private bool touchEnabled = true;

        [Tooltip("Drag distance, as a fraction of the shortest screen edge, that produces full speed.")]
        [Range(0.02f, 0.5f)]
        [SerializeField] private float dragFullDeflectionScreenFraction = 0.12f;

        [Tooltip("Keeps the drag origin trailing the finger so a long drag never saturates out of reach.")]
        [SerializeField] private bool recenterDragAnchor = true;

        [Tooltip("Left mouse button emulates a finger drag so touch control can be tested in the Game view.")]
        [SerializeField] private bool simulatePointerDragInEditor = true;

        /// <summary>Combined, clamped movement intent. Magnitude is never greater than 1.</summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>True while a finger (or emulated pointer) is steering the player.</summary>
        public bool IsDragging { get; private set; }

        private bool _pointerActive;
        private Vector2 _dragAnchor;

        private void OnDisable()
        {
            MoveInput = Vector2.zero;
            IsDragging = false;
            _pointerActive = false;
        }

        private void Update()
        {
            Vector2 move = ReadKeyboard() + ReadDrag();

            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            MoveInput = move;
        }

        private Vector2 ReadKeyboard()
        {
            if (!keyboardEnabled)
            {
                return Vector2.zero;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            Vector2 move = Vector2.zero;

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                move.x -= 1f;
            }

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                move.x += 1f;
            }

            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                move.y += 1f;
            }

            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                move.y -= 1f;
            }

            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

        private Vector2 ReadDrag()
        {
            if (!touchEnabled)
            {
                _pointerActive = false;
                IsDragging = false;
                return Vector2.zero;
            }

            if (!TryReadPointer(out Vector2 position, out bool pressed))
            {
                _pointerActive = false;
                IsDragging = false;
                return Vector2.zero;
            }

            if (!pressed)
            {
                _pointerActive = false;
                IsDragging = false;
                return Vector2.zero;
            }

            if (!_pointerActive)
            {
                _pointerActive = true;
                _dragAnchor = position;
                IsDragging = true;
                return Vector2.zero;
            }

            IsDragging = true;

            float fullDeflectionPixels = Mathf.Max(1f,
                Mathf.Min(Screen.width, Screen.height) * dragFullDeflectionScreenFraction);

            Vector2 offset = position - _dragAnchor;
            Vector2 move = offset / fullDeflectionPixels;

            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();

                if (recenterDragAnchor)
                {
                    // Pull the anchor along behind the finger so the stick stays reachable
                    // and a reversal of direction responds immediately.
                    _dragAnchor = position - (move * fullDeflectionPixels);
                }
            }

            return move;
        }

        private bool TryReadPointer(out Vector2 position, out bool pressed)
        {
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                TouchControl touch = touchscreen.primaryTouch;
                pressed = touch.press.isPressed;
                position = touch.position.ReadValue();
                return true;
            }

            if (simulatePointerDragInEditor)
            {
                Mouse mouse = Mouse.current;
                if (mouse != null)
                {
                    pressed = mouse.leftButton.isPressed;
                    position = mouse.position.ReadValue();
                    return true;
                }
            }

            position = Vector2.zero;
            pressed = false;
            return false;
        }
    }
}
