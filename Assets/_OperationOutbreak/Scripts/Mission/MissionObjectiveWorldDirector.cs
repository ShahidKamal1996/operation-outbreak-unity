using System.Collections.Generic;
using OperationOutbreak.Player;
using UnityEngine;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X.5 - spawns the world-space objective targets the ACTIVE mission needs and
    /// wires them to the existing objective authority. It reads the mission selected through the
    /// 1X selection system (ActiveMissionContext) and the objective controller, and creates only
    /// the targets the mission's objectives declare:
    ///   * DestroyTargets  -> barricade slabs (BarricadeTarget, IDamageable) in the lane.
    ///   * ActivateTargets -> activation pillars (ActivationObjectiveTarget) the player reaches.
    /// Targets are built from primitives in code (no new prefabs/assets), placed at authored
    /// positions, and configured from the mission's objective data (health / radius / duration /
    /// reset policy / count). This keeps the single gameplay scene usable for every mission:
    /// Mission 1/2 (no such objectives) simply spawn nothing.
    ///
    /// Activation pillars start ENABLED only while the ActivateTargets objective is the active
    /// stage (so they cannot be triggered early); the director subscribes to ObjectiveActivated to
    /// enable them at the right stage. Barricades are active from mission start. No completion
    /// authority lives here - the targets raise events the controller routes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionObjectiveWorldDirector : MonoBehaviour
    {
        [Header("References (auto-resolved if empty)")]
        [SerializeField] private MissionObjectiveController objectiveController;
        [SerializeField] private Transform playerTransform;

        [Header("Barricade placement (DestroyTargets)")]
        [Tooltip("Authored world positions for barricades; the first N are used (N = requiredTargetCount).")]
        [SerializeField] private Vector3[] barricadePositions = new Vector3[]
        {
            new Vector3(0f, 1f, 30f),
            new Vector3(0f, 1f, 42f)
        };

        [Header("Activation point placement (ActivateTargets)")]
        [Tooltip("Authored world positions for activation points; the first N are used.")]
        [SerializeField] private Vector3[] activationPositions = new Vector3[]
        {
            new Vector3(-2f, 0.5f, 44f),
            new Vector3(2f, 0.5f, 48f),
            new Vector3(0f, 0.5f, 50f)
        };

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<ActivationObjectiveTarget> _activationPoints = new List<ActivationObjectiveTarget>();

        private void Awake()
        {
            if (objectiveController == null) objectiveController = FindAnyObjectByType<MissionObjectiveController>();
            if (playerTransform == null)
            {
                PlayerController player = FindAnyObjectByType<PlayerController>();
                if (player != null) playerTransform = player.transform;
            }
        }

        private void OnEnable()
        {
            Build();
            if (objectiveController != null)
            {
                objectiveController.ObjectiveActivated += HandleObjectiveActivated;
            }
        }

        private void OnDisable()
        {
            if (objectiveController != null)
            {
                objectiveController.ObjectiveActivated -= HandleObjectiveActivated;
            }

            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) Destroy(_spawned[i]);
            }

            _spawned.Clear();
            _activationPoints.Clear();
        }

        private void Build()
        {
            MissionDefinition mission = ActiveMissionContext.Current;
            if (mission == null || objectiveController == null)
            {
                return;
            }

            IReadOnlyList<MissionObjectiveDefinition> objectives = mission.Objectives;
            if (objectives == null)
            {
                return;
            }

            for (int i = 0; i < objectives.Count; i++)
            {
                MissionObjectiveDefinition objective = objectives[i];
                if (objective == null)
                {
                    continue;
                }

                switch (objective.objectiveType)
                {
                    case MissionObjectiveType.DestroyTargets:
                        SpawnBarricades(objective);
                        break;
                    case MissionObjectiveType.ActivateTargets:
                        SpawnActivationPoints(objective);
                        break;
                }
            }

            // Activation points reflect the current stage (enabled iff the objective is active).
            RefreshActivationPointState();
        }

        private void SpawnBarricades(MissionObjectiveDefinition objective)
        {
            int count = Mathf.Max(0, objective.requiredTargetCount);
            for (int i = 0; i < count; i++)
            {
                Vector3 position = Position(barricadePositions, i, new Vector3(0f, 1f, 30f + i * 3f));
                GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "Barricade_" + (i + 1);
                slab.transform.SetParent(transform, false);
                slab.transform.position = position;
                slab.transform.localScale = new Vector3(4.5f, 2.6f, 0.9f);

                MeshRenderer body = slab.GetComponent<MeshRenderer>();
                Tint(body, new Color(0.55f, 0.42f, 0.28f, 1f));

                BarricadeTarget target = slab.AddComponent<BarricadeTarget>();
                target.Configure(objective.objectiveId + "_barricade_" + (i + 1),
                    Mathf.Max(1, objective.targetHealth), body);

                _spawned.Add(slab);
            }
        }

        private void SpawnActivationPoints(MissionObjectiveDefinition objective)
        {
            int count = Mathf.Max(0, objective.requiredTargetCount);
            for (int i = 0; i < count; i++)
            {
                Vector3 position = Position(activationPositions, i, new Vector3(0f, 0.5f, 24f + i * 6f));
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "ActivationPoint_" + (i + 1);
                pillar.transform.SetParent(transform, false);
                pillar.transform.position = position;
                pillar.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);

                // The default cylinder collider is fine for a visual marker; activation is by radius.
                MeshRenderer body = pillar.GetComponent<MeshRenderer>();
                Tint(body, new Color(0.95f, 0.65f, 0.15f, 1f));

                ActivationObjectiveTarget point = pillar.AddComponent<ActivationObjectiveTarget>();
                point.Configure(objective.objectiveId + "_point_" + (i + 1),
                    objective.activationDuration,
                    objective.activationRadius,
                    objective.resetProgressOnLeave,
                    playerTransform,
                    body);
                point.enabled = false; // enabled only while the objective's stage is active

                _spawned.Add(pillar);
                _activationPoints.Add(point);
            }
        }

        private void HandleObjectiveActivated(MissionObjectiveRuntime runtime)
        {
            if (runtime != null && runtime.Type == MissionObjectiveType.ActivateTargets)
            {
                RefreshActivationPointState();
            }
        }

        private void RefreshActivationPointState()
        {
            if (objectiveController == null)
            {
                return;
            }

            bool active = IsObjectiveActive(MissionObjectiveType.ActivateTargets);
            for (int i = 0; i < _activationPoints.Count; i++)
            {
                if (_activationPoints[i] != null)
                {
                    _activationPoints[i].enabled = active;
                }
            }
        }

        private bool IsObjectiveActive(MissionObjectiveType type)
        {
            IReadOnlyList<MissionObjectiveRuntime> objectives = objectiveController.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                if (objectives[i] != null && objectives[i].Type == type && objectives[i].IsActive)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3 Position(Vector3[] authored, int index, Vector3 fallback)
        {
            if (authored != null && index < authored.Length)
            {
                return authored[index];
            }

            return fallback;
        }

        private static void Tint(MeshRenderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
        }
    }
}
