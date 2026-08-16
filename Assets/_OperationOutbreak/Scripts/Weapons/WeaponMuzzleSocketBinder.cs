using UnityEngine;

namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Milestone 1P.5 QA fix #2 - binds the authoritative MuzzlePoint to the Toon
    /// Soldier's animated weapon hand, PRESENTATION-ONLY.
    ///
    /// Manual QA found the projectile/muzzle flash originated away from the visible
    /// Toon Soldier rifle. WeaponController and its MuzzlePoint remain the ONLY gameplay
    /// authority for firing, projectile origin, targeting and muzzle feedback; this
    /// component only re-parents the existing MuzzlePoint transform so the visuals line
    /// up with the character.
    ///
    /// WHY THE RIGHT HAND BONE: the Toon Soldiers rifle is part of the skinned mesh
    /// (the package's WeaponContainer node is a vestigial root-level helper that does
    /// not follow the skeleton), so the rifle visually rides the hand. Binding the
    /// muzzle to the humanoid Right Hand bone (resolved through Animator.GetBoneTransform,
    /// never by guessing hierarchy names) makes the muzzle follow the animated rifle
    /// during idle/run/shoot with ZERO per-frame work: the bone moves, the muzzle moves.
    ///
    /// CONTRACT:
    ///   - One-time binding in Awake. No per-frame hierarchy searches, no Update loop.
    ///   - No duplicate weapon: this component never adds a WeaponController, never
    ///     spawns projectiles, never writes gameplay state.
    ///   - Fallback-safe: if the soldier visual is inactive (Carl/prototype restored),
    ///     has no valid humanoid Animator, or exposes no right hand, binding is skipped
    ///     and the MuzzlePoint keeps its authored position under the Weapon - exactly
    ///     the pre-1P.5 behavior.
    ///   - The barrel-tip offset lives here and is authored in the hand's local space,
    ///     so it is a tunable presentation value, never a hard-coded world coordinate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponMuzzleSocketBinder : MonoBehaviour
    {
        [Header("Presentation Socket (Toon Soldier)")]
        [Tooltip("The ToonSoldier_demo instance root. Its Animator must use the imported " +
                 "ToonSoldier_demoAvatar humanoid avatar.")]
        [SerializeField] private Transform soldierVisualRoot;

        [Header("Gameplay Anchor (authority, never replaced)")]
        [Tooltip("The existing WeaponController muzzle point. The SAME transform is " +
                 "re-parented; no new muzzle object is created.")]
        [SerializeField] private Transform muzzlePoint;

        [Header("Barrel Tip Tuning (hand-local, visual only)")]
        [Tooltip("Muzzle offset from the right hand along the rifle barrel. Tune this " +
                 "in the Inspector to sit the flash exactly at the visible barrel tip.")]
        [SerializeField] private Vector3 barrelTipOffset = new Vector3(0f, 0f, 0.6f);

        [Tooltip("Optional hand-local rotation correction if the rifle axis differs.")]
        [SerializeField] private Vector3 barrelRotationEuler = Vector3.zero;

        /// <summary>
        /// Pure decision: whether binding should happen. Split out so EditMode tests
        /// can pin the fallback rules (inactive soldier, missing animator/avatar/bone)
        /// without an imported humanoid rig.
        /// </summary>
        public static bool ShouldBind(bool soldierActiveInHierarchy, bool hasAnimator, bool hasHandBone)
        {
            return soldierActiveInHierarchy && hasAnimator && hasHandBone;
        }

        /// <summary>
        /// The single binding operation: parents the EXISTING muzzlePoint under the
        /// socket and applies the hand-local barrel offset. Pure enough to unit test
        /// with plain GameObjects - no Animator involved, no new objects created.
        /// </summary>
        public static void AttachMuzzleToSocket(
            Transform muzzlePoint, Transform socket, Vector3 localOffset, Vector3 localRotationEuler)
        {
            if (muzzlePoint == null || socket == null)
            {
                return;
            }

            muzzlePoint.SetParent(socket, false);
            muzzlePoint.localPosition = localOffset;
            muzzlePoint.localRotation = Quaternion.Euler(localRotationEuler);
        }

        /// <summary>
        /// Start, not Awake: the soldier Animator must be initialized before its bone
        /// hierarchy can be queried (GetBoneTransform returns null before init). Start
        /// runs after all Awakes and after the Animator's OnEnable, and still before the
        /// weapon's first Update - so the first shot already originates at the rifle.
        /// </summary>
        private void Start()
        {
            TryBind();
        }

        /// <summary>
        /// Resolves the soldier's right hand through the humanoid avatar and binds the
        /// muzzle to it. Returns true when the muzzle is now socket-bound. Safe to call
        /// more than once; a failed resolution leaves everything untouched.
        /// </summary>
        public bool TryBind()
        {
            if (muzzlePoint == null || soldierVisualRoot == null)
            {
                return false;
            }

            bool soldierActive = soldierVisualRoot.gameObject.activeInHierarchy;
            Animator animator = soldierVisualRoot.GetComponent<Animator>();
            bool hasAnimator = animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman;
            Transform handBone = hasAnimator ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

            if (!ShouldBind(soldierActive, hasAnimator, handBone != null))
            {
                // Carl/prototype fallback: keep the muzzle exactly where the Weapon
                // hierarchy authors it. No gameplay behavior changes.
                return false;
            }

            AttachMuzzleToSocket(muzzlePoint, handBone, barrelTipOffset, barrelRotationEuler);
            return true;
        }
    }
}
