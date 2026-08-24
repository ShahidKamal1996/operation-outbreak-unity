using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #8 — drives a VISUAL-ONLY cinematic clone of the production Toon
    /// Soldier into a seated pose for the helicopter interior briefing, WITHOUT touching the
    /// gameplay Animator controller.
    ///
    /// The clone runs the production controller (so the upper body — chest, head, arms, rifle —
    /// is the real, recognisable idle Kane, alive with subtle motion). Each LateUpdate this reads
    /// the Animator's evaluated HumanPose and overrides ONLY the leg muscles (hip flexion + knee
    /// bend) plus a tiny forward spine lean, then writes the pose back. Because it runs after the
    /// Animator's own evaluation it wins the frame, and because it edits muscles (not raw Euler
    /// angles) the result always stays inside the avatar's joint limits — no dislocated limbs.
    ///
    /// This component adds NO gameplay authority: it is a presentation-only pose driver living on
    /// a clone that has been scrubbed of PlayerController / WeaponController / health / colliders.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class CinematicKanePose : MonoBehaviour
    {
        [Header("Seated leg muscles (normalized -1..1)")]
        [Range(-1f, 1f)] [SerializeField] private float upperLegFrontBack = 0.78f;
        [Range(-1f, 1f)] [SerializeField] private float lowerLegStretch = 0.9f;
        [Range(-1f, 1f)] [SerializeField] private float footUpDown = 0.15f;
        [Range(-1f, 1f)] [SerializeField] private float spineFrontBack = 0.1f;

        [Header("Seated pelvis (metres above the avatar root = floor)")]
        [Tooltip("HumanPose.bodyPosition.y is the pelvis height in metres above the avatar root. " +
                 "Set to the bench seat height so the pelvis rests ON the seat (QA fix #11).")]
        [SerializeField] private float seatHeight = 0.5f;

        private Animator _animator;
        private HumanPoseHandler _handler;
        private HumanPose _pose;
        private int _mLeftUpperLegFB, _mRightUpperLegFB;
        private int _mLeftLowerLeg, _mRightLowerLeg;
        private int _mLeftFootUD, _mRightFootUD;
        private int _mSpineFB;
        private int _mChestFB;
        private bool _ready;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
            {
                Debug.LogWarning("[STORY M01] CinematicKanePose: no valid humanoid avatar — seated override disabled (clone idles standing).");
                enabled = false;
                return;
            }

            _handler = new HumanPoseHandler(_animator.avatar, _animator.transform);
            _pose = new HumanPose();

            // Resolve muscle indices by name once — robust to Unity reordering the table.
            _mLeftUpperLegFB = MuscleIndex("Left Upper Leg Front-Back");
            _mRightUpperLegFB = MuscleIndex("Right Upper Leg Front-Back");
            _mLeftLowerLeg = MuscleIndex("Left Lower Leg Stretch");
            _mRightLowerLeg = MuscleIndex("Right Lower Leg Stretch");
            _mLeftFootUD = MuscleIndex("Left Foot Up-Down");
            _mRightFootUD = MuscleIndex("Right Foot Up-Down");
            _mSpineFB = MuscleIndex("Spine Front-Back");
            _mChestFB = MuscleIndex("Chest Front-Back");

            _ready = true;
        }

        private void LateUpdate()
        {
            if (!_ready || _handler == null) return;

            // HumanPose reads/writes can fail on the very first frames before the Animator has
            // fully evaluated; guard so a transient avatar issue can never break the cinematic.
            try
            {
                _handler.GetHumanPose(ref _pose);

                // QA fix #11 — lower the pelvis onto the bench. bodyPosition.y is the pelvis height
                // in metres above the avatar root (which the rig places on the floor), so setting it
                // to the seat height drops the whole upper body until the pelvis rests on the bench.
                _pose.bodyPosition = new Vector3(_pose.bodyPosition.x, seatHeight, _pose.bodyPosition.z);

                // Override only the legs + slight torso lean. Everything else (arms, head, rifle)
                // stays exactly as the production idle clip authored it.
                SetMuscle(_mLeftUpperLegFB, upperLegFrontBack);
                SetMuscle(_mRightUpperLegFB, upperLegFrontBack);
                SetMuscle(_mLeftLowerLeg, lowerLegStretch);
                SetMuscle(_mRightLowerLeg, lowerLegStretch);
                SetMuscle(_mLeftFootUD, footUpDown);
                SetMuscle(_mRightFootUD, footUpDown);
                SetMuscle(_mSpineFB, spineFrontBack);
                SetMuscle(_mChestFB, spineFrontBack * 0.5f);

                _handler.SetHumanPose(ref _pose);
            }
            catch (System.Exception e)
            {
                // Disable the override so the clone simply idles rather than throwing every frame.
                Debug.LogWarning("[STORY M01] CinematicKanePose override disabled: " + e.Message);
                enabled = false;
            }
        }

        private void SetMuscle(int index, float value)
        {
            if (index >= 0 && index < _pose.muscles.Length)
                _pose.muscles[index] = value;
        }

        private static int MuscleIndex(string name)
        {
            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < names.Length; i++)
                if (names[i] == name) return i;
            return -1;
        }

        private void OnDestroy() => _handler?.Dispose();
    }
}
