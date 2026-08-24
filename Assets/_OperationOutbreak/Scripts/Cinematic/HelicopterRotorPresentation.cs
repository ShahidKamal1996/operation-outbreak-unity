using UnityEngine;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Milestone 1Z.1B — lightweight visual rotor spin for the cinematic helicopter. Purely visual:
    /// no physics, no Rigidbody, no colliders. Rotates configurable main/tail rotor transforms
    /// around their local Y axis at a configurable speed. Only spins while <see cref="IsActive"/>
    /// is set (controlled by the cinematic controller).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HelicopterRotorPresentation : MonoBehaviour
    {
        [Header("Rotor References")]
        [Tooltip("Main rotor transform (top). Leave null if the model has no separate rotor.")]
        [SerializeField] private Transform mainRotor;
        [Tooltip("Tail rotor transform (tail boom). Leave null if not present.")]
        [SerializeField] private Transform tailRotor;

        [Header("Speed")]
        [Tooltip("Rotor spin speed in degrees per second.")]
        [SerializeField] private float mainRotorDps = 1800f;
        [SerializeField] private float tailRotorDps = 2200f;

        [Tooltip("When true the rotors spin. Set by the cinematic controller.")]
        public bool IsActive { get; set; } = true;

        private void Update()
        {
            if (!IsActive) return;
            float dt = Time.unscaledDeltaTime;

            if (mainRotor != null)
                mainRotor.localRotation *= Quaternion.Euler(0f, mainRotorDps * dt, 0f);

            if (tailRotor != null)
                tailRotor.localRotation *= Quaternion.Euler(tailRotorDps * dt, 0f, 0f);
        }
    }
}
