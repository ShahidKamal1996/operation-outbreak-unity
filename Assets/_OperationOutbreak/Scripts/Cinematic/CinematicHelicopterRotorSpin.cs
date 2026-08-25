using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Micro task #1 — rotor spin ONLY, for the manually authored Helicopter_Cinematic scene.
    ///
    /// Spins two rotor transforms in place. That is the entire responsibility.
    ///
    /// STRICT SAFETY CONTRACT
    /// ----------------------
    /// At runtime this component writes to EXACTLY two things:
    ///     mainRotor.localRotation
    ///     tailRotor.localRotation
    ///
    /// It never touches the helicopter root, the body, rotor positions or scales, the camera,
    /// lighting, materials, physics, animators, gameplay, Mission 01, or the story systems.
    /// There is no flight, no camera movement, no timeline, and no scene transition.
    ///
    /// It uses Transform.Rotate(axis, degrees, Space.Self), which composes into localRotation and
    /// therefore cannot move or rescale anything. Position and scale are never read or written.
    ///
    /// It is NOT auto-attached anywhere: no [RequireComponent], no [ExecuteAlways], no editor
    /// bootstrap. Nothing can modify the manually authored scene unless you add this component
    /// yourself in the Inspector.
    ///
    /// AXES ARE INSPECTOR-DRIVEN ON PURPOSE
    /// ------------------------------------
    /// The rotors are children with a local rotation of (-90, 0, 0), which is the classic sign of
    /// a Z-up authored model (Blender/3ds Max) rotated into Unity's Y-up convention. Under that
    /// -90 X rotation, the axis that points "up" in the model's own local space is +Z, not +Y.
    /// The defaults below are chosen for that case, but the authored axis of an imported mesh
    /// cannot be confirmed without opening the model, so if a rotor spins the wrong way just
    /// change its axis in the Inspector. No code change is needed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Operation Outbreak/Cinematic/Cinematic Helicopter Rotor Spin")]
    public sealed class CinematicHelicopterRotorSpin : MonoBehaviour
    {
        [Header("Rotor Transforms")]
        [Tooltip("The top/main rotor. Assign 'rotor_up'. Safe to leave empty — it is simply skipped.")]
        [SerializeField] private Transform mainRotor;

        [Tooltip("The tail rotor. Assign 'rotor_tail'. Safe to leave empty — it is simply skipped.")]
        [SerializeField] private Transform tailRotor;

        [Header("Spin Axes (LOCAL space)")]
        [Tooltip("Local spin axis for the main rotor. Default (0,0,1) = local Z, which is the " +
                 "'up' axis for a Z-up authored model sitting at local rotation (-90,0,0). " +
                 "If it spins on the wrong plane, try (0,1,0). Flip the sign to reverse direction.")]
        [SerializeField] private Vector3 mainRotorAxis = new Vector3(0f, 0f, 1f);

        [Tooltip("Local spin axis for the tail rotor. Default (0,1,0) = local Y. " +
                 "If it spins on the wrong plane, try (0,0,1) or (1,0,0). " +
                 "Flip the sign to reverse direction.")]
        [SerializeField] private Vector3 tailRotorAxis = new Vector3(0f, 1f, 0f);

        [Header("Speeds (degrees / second)")]
        [Tooltip("Main rotor speed in degrees per second. Negative reverses direction.")]
        [SerializeField] private float mainRotorSpeed = 1500f;

        [Tooltip("Tail rotor speed in degrees per second. Negative reverses direction.")]
        [SerializeField] private float tailRotorSpeed = 2200f;

        [Header("Control")]
        [Tooltip("Uncheck to stop both rotors without removing the component.")]
        [SerializeField] private bool spinEnabled = true;

        [Tooltip("Use unscaled time so the rotors keep spinning if Time.timeScale is 0 (paused / " +
                 "cinematic). Uncheck to respect timeScale.")]
        [SerializeField] private bool useUnscaledTime = true;

        /// <summary>Enables/disables the spin at runtime without touching the component itself.</summary>
        public bool SpinEnabled
        {
            get => spinEnabled;
            set => spinEnabled = value;
        }

        private void Update()
        {
            if (!spinEnabled) return;

            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt <= 0f) return; // paused with scaled time, or a zero-length frame

            Spin(mainRotor, mainRotorAxis, mainRotorSpeed, dt);
            Spin(tailRotor, tailRotorAxis, tailRotorSpeed, dt);
        }

        /// <summary>
        /// Rotates one rotor about a normalized LOCAL axis. Writes only localRotation.
        /// A null transform or a zero/degenerate axis is skipped rather than throwing —
        /// Quaternion.AngleAxis with a zero axis produces an invalid rotation that would
        /// corrupt the transform.
        /// </summary>
        private static void Spin(Transform rotor, Vector3 axis, float degreesPerSecond, float dt)
        {
            if (rotor == null) return;

            // sqrMagnitude avoids a needless sqrt and safely rejects (0,0,0).
            if (axis.sqrMagnitude < 1e-8f) return;

            rotor.Rotate(axis.normalized, degreesPerSecond * dt, Space.Self);
        }
    }
}
