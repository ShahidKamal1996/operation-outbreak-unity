using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X.5 - a world-space activation control point for the ActivateTargets objective
    /// (Mission 5). The player must physically REACH and HOLD the point: while the player is
    /// within <see cref="activationRadius"/> an activation progress accumulates at 1/
    /// <see cref="activationDuration"/> per second; when it fills, the point is ACTIVATED and
    /// raises <see cref="MissionObjectiveTargetEvents.RaiseTargetActivated"/> exactly once, which
    /// the single objective authority routes into the ActivateTargets runtime.
    ///
    /// Mobile-friendly: no button prompts, no aiming - just stand in the zone. The
    /// <see cref="resetProgressOnLeave"/> policy is data-driven: if true, leaving the radius resets
    /// progress to zero; if false (default), progress is retained where it was.
    ///
    /// Detection is a cheap per-frame distance check (the Player has no Rigidbody, so trigger
    /// callbacks would not fire). The component is enabled by the world director only while the
    /// ActivateTargets objective is the active stage, so a point cannot be activated before its
    /// stage - and enemies dying alone can never activate it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActivationObjectiveTarget : MonoBehaviour
    {
        [Tooltip("Activation target id raised when this point activates.")]
        [SerializeField] private string targetId = "activate_point";

        [Tooltip("Seconds the player must remain in the radius to activate. Set from mission data.")]
        [SerializeField] private float activationDuration = 1.5f;

        [Tooltip("Activation radius around the point. Set from mission data.")]
        [SerializeField] private float activationRadius = 1.5f;

        [Tooltip("If true, leaving the radius resets activation progress to zero (else retained).")]
        [SerializeField] private bool resetProgressOnLeave = false;

        [Tooltip("Player transform whose distance is measured. Resolved by the spawner if empty.")]
        [SerializeField] private Transform playerTransform;

        [Tooltip("Optional visible body tinted by activation progress (set by spawner).")]
        [SerializeField] private MeshRenderer body;

        private bool _activated;
        private float _progressSeconds;
        private float _radiusSqr;

        public string TargetId => targetId;
        public bool IsActivated => _activated;
        public float NormalizedProgress => activationDuration > 0f ? Mathf.Clamp01(_progressSeconds / activationDuration) : 0f;

        /// <summary>Configures the point from mission data before it is used.</summary>
        public void Configure(string id, float duration, float radius, bool resetOnLeave,
            Transform player, MeshRenderer visibleBody)
        {
            if (!string.IsNullOrEmpty(id))
            {
                targetId = id;
            }

            activationDuration = Mathf.Max(0.01f, duration);
            activationRadius = Mathf.Max(0.1f, radius);
            resetProgressOnLeave = resetOnLeave;
            playerTransform = player;
            body = visibleBody;
            _radiusSqr = activationRadius * activationRadius;
        }

        private void Awake()
        {
            _radiusSqr = activationRadius * activationRadius;
            if (body == null)
            {
                body = GetComponentInChildren<MeshRenderer>();
            }
        }

        private void Update()
        {
            if (_activated || playerTransform == null || activationDuration <= 0f)
            {
                return;
            }

            Vector3 toPlayer = playerTransform.position - transform.position;
            toPlayer.y = 0f;

            bool inRange = toPlayer.sqrMagnitude <= _radiusSqr;

            if (inRange)
            {
                _progressSeconds += Time.deltaTime;

                if (_progressSeconds >= activationDuration)
                {
                    _activated = true;
                    MissionObjectiveTargetEvents.RaiseTargetActivated(targetId);
                }
            }
            else if (resetProgressOnLeave && _progressSeconds > 0f)
            {
                _progressSeconds = 0f;
            }

            UpdateVisual();
        }

        private void UpdateVisual()
        {
            if (body == null)
            {
                return;
            }

            // Simple readable feedback: green when activated, else lerp from amber to cyan as it fills.
            float p = _activated ? 1f : NormalizedProgress;
            body.sharedMaterial = null; // avoid mutating a shared material; tint via property block
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            body.GetPropertyBlock(block);
            Color tint = _activated
                ? new Color(0.20f, 0.80f, 0.35f, 1f)
                : Color.Lerp(new Color(0.95f, 0.65f, 0.15f, 1f), new Color(0.25f, 0.70f, 1.0f, 1f), p);
            block.SetColor("_BaseColor", tint);
            body.SetPropertyBlock(block);
        }
    }
}
