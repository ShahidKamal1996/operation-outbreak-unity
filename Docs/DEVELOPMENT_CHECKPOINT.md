# OPERATION OUTBREAK — DEVELOPMENT CHECKPOINT

> **NOTE ON RECORD PROVENANCE (Milestone 1P, 2026-08-14):**
> The milestone brief referenced `Docs/OPERATION_OUTBREAK_MASTER_ROADMAP.md`,
> `Docs/DEVELOPMENT_CHECKPOINT.md`, `Docs/ARCHITECTURE_DECISIONS.md` and
> `Docs/MILESTONE_LEDGER.md`. At the start of Milestone 1P **none of these files existed
> anywhere in the repository or its git history** (verified with `git rev-list --all`).
> The three files below were therefore **recreated during 1P** from the two authoritative
> sources that do exist: the git history (commit messages) and the current repository
> contents. The master roadmap could not be reconstructed from those sources and is
> intentionally NOT recreated here — it must be re-authored by the project owner.
> Where a fact could not be verified it is marked UNKNOWN rather than guessed.

## Current branch & base

| Item | Value |
|---|---|
| Working branch | `arena/milestone-1p-weapon-combat-feel` |
| Created from | `arena/milestone-1o5-real-player-integration` |
| Base commit | `f43af78` — Add gameplay post-processing baseline |
| Rendering baselines preserved | `35f8481` (mobile rendering), `f43af78` (gameplay post-processing) — untouched by 1P |

## Milestone status

| Milestone | Status |
|---|---|
| 1C — 1O.5 (shooting, zombies, combat HUD, waves, targeting, gates, feedback, upgrades, sections, Runner, diagnostics, Carl) | IMPLEMENTED (see MILESTONE_LEDGER.md) |
| Gate systems (1J series) | Development paused per milestone briefs. NOT resumed by 1P. |
| **1P — weapon & combat feel foundation** | **VERIFIED** (2026-08-16 — project owner's local Unity QA; see "Manual Unity QA" below) |
| **1P.5 — Toon Soldier visual integration** | **VERIFIED** (owner QA 2026-08-17: EditMode 137/137, animations/aim/grounding/muzzle all accepted) |
| **1Q — production enemy foundation (Basic Infected)** | **IMPLEMENTED — AWAITING MANUAL UNITY QA** (2026-08-17) |
| 1R | NOT STARTED — begin only when the project owner authorizes it after 1Q QA. |

## What Milestone 1P delivered

1. **Muzzle flash** — extracted from `WeaponController` (where it was a sphere built in
   `Start()` and toggled by a coroutine) into the feedback layer:
   `MuzzleFlashFeedback` listens to the weapon's existing `ShotFired` event and spawns a
   pooled, short-lived flash at the existing `MuzzlePoint`. Weapon gameplay code now
   contains zero presentation code.
2. **Projectile readability** — a short, thin, shadowless `TrailRenderer` attached at
   runtime by `Projectile.EnsureTrailPresentation()` (idempotent, shared material, no
   collider). Projectile mechanics, damage, speed, lifetime and collision behaviour are
   untouched.
3. **Hit impact feedback** — `CombatFeedback.SpawnHitSpark` (the Milestone 1H contract,
   still called from `Projectile`) now uses a bounded object pool and a shared material
   instead of instantiating a primitive + material per hit.
4. **Enemy hit reaction** — the existing white-flash `HitFlash` was refined (not
   duplicated) into `HitReaction`: same white flash plus a tiny 7% visual-only scale
   punch on the `Visual` child, with explicit death guards.
5. **Tests** — `Assets/_OperationOutbreak/Tests/Editor/CombatFeedbackTests.cs` covers
   pool lifecycle, envelope/punch curves, muzzle flash duration clamping and projectile
   trail + configuration clamping.
6. **Docs** — this file, `MILESTONE_LEDGER.md`, `ARCHITECTURE_DECISIONS.md` (recreated).

## Frozen by 1P (untouched, must remain untouched)

Player movement/speeds, forward progression, auto-targeting and target selection, weapon
damage, fire rate balance, enemy HP/movement/damage/spawning, BASIC and RUNNER behaviour,
section activation/completion, mission completion, Game Over, upgrade/pickup balance,
camera position/rotation/FOV, URP configuration, post-processing, global lighting, Carl
integration architecture, player root authority, character animations (except the enemy
`Visual`-child scale punch, which is a presentation hook), postponed gate systems.

No final weapon models, no production art, no audio, no camera shake, no realtime
per-shot lights were added.

## Manual Unity QA — RESULT: PASSED (Milestone 1P VERIFIED)

Milestone 1P was **verified by the project owner** in local Unity QA on 2026-08-16.
Arena implementation alone is never treated as verification; this section records the
owner's acceptance evidence:

**1. Unity EditMode Test Runner (Unity 6.5):**
- 109 tests total / **109 passed** / **0 failed**.

**2. Full gameplay diagnostic run:**
- Verdict: **PASS** — 39 checks total, 34 passed, **0 failed**, 5 warnings.
- The 5 warnings concern the **existing RUNNER encounter/spawn-pressure behavior** and
  are explicitly NOT Milestone 1P combat-feedback failures. They remain open as a
  pre-existing tuning item for a future milestone and do not affect 1P acceptance.

**3. Unity Console during the accepted gameplay run:**
- 0 errors, 0 warnings.

**4. Full gameplay recording manually reviewed — visually confirmed:**
- muzzle/shot feedback works repeatedly
- projectile trail/readability works
- hit/impact feedback works
- enemy hit flash/reaction works
- rapid firing does not leave stuck feedback objects
- enemy death and section progression remain functional
- Carl animation remains functional
- mission reaches Mission Complete normally

**5. Pooled-feedback coroutine regression:**
- RESOLVED. The "Coroutine couldn't be started because the game object is inactive"
  failure found in the first QA pass was fixed in commit `2762433` (activation-order
  fix) and its follow-up test corrections `a02c65b`; the corresponding regression tests
  now pass locally.

The 12-step checklist that was executed for this acceptance:

1. FIRE — muzzle feedback appears for each valid shot. ✔
2. PROJECTILE — clearly readable; trajectory/mechanics unchanged. ✔
3. HIT — impact feedback appears on actual hits. ✔
4. ENEMY REACTION — enemy communicates damage without gameplay movement changes. ✔
5. RAPID FIRING — no permanent muzzle flashes / impact objects / trails. ✔
6. ENEMY DEATH — hit feedback does not survive incorrectly after death. ✔
7. BASIC ENEMY — behaviour unchanged. ✔
8. RUNNER — behaviour unchanged. ✔
9. SECTION PROGRESSION — all 3 authored sections progress correctly. ✔
10. MISSION COMPLETE — fires exactly once. ✔
11. GAME OVER — behaviour remains valid. ✔
12. CONSOLE — no new exceptions/errors caused by combat feedback. ✔

## What Milestone 1Q delivered

Production enemy VISUAL foundation only — zero enemy gameplay changes:

1. **Enemy presentation bridge** — `EnemyAnimationBridge` (one-way
   gameplay → animator, mirroring the 1O.5/1P.5 player bridge): reads the new
   read-only `ZombieController.CurrentPlanarSpeed`, observes the existing
   `DamagedPlayer` (→ Attack trigger) and `Died` (→ Dead latch + death anim)
   events, enforces root motion OFF. Deleting the bridge leaves the enemy fully
   playable on the prototype visual.
2. **Reusable enemy controller** — `Art/Animations/Enemies/OO_BasicInfected.controller`,
   authored by Unity via `Tools > Operation Outbreak > Rebuild Basic Infected
   Animator Controller` (the project's no-hand-authored-FBX-fileIDs rule):
   params Speed/Attack/Dead; states Idle (zombie idle), Walk (zombie walk),
   Attack (zombie attack; AnyState trigger, exit-time return by Speed),
   Death (zombie death; no exits). The zombie run clip stays RESERVED for
   future Runner variants — it is not part of Basic Infected locomotion.
3. **Production visual setup tool** — `Tools > Operation Outbreak > Set Up Basic
   Infected Production Visual` edits the `Zombie_Prototype.prefab` asset
   idempotently: instantiates `StylizedZombie_01` under a new `ProductionVisual`
   child, assigns the controller + `StylizedZombieAvatar` (root motion OFF,
   AlwaysAnimate), hides the prototype `Visual` renderers (never deleted — the
   safe fallback), wires the bridge, and raises `deathPresentationDuration` to
   1.15 s so the death clip plays before deactivation. If the production prefab
   is missing the tool aborts and the prototype keeps working.
4. **Death presentation** — gameplay accounting is untouched (`Died` still fires
   immediately at zero health; kill counting, section clear and mission
   completion timing are identical). Only the GameObject deactivation is
   delayed by the serialized `deathPresentationDuration` (default 0.38 s =
   the pre-1Q behavior byte-for-byte when the tool never runs).
5. **Movement rule** — the Animator has Apply Root Motion OFF; `ZombieController`
   remains the single movement authority (no animation-driven movement).
6. **QA fix 1 — Bug 4 (2026-08-17)** — the walk animation played at native clip
   speed (~1.3 u/s cadence) while gameplay moves at 2.5 u/s, so feet slid across the
   ground. Fix: a new `LocomotionSpeedMultiplier` Animator float parameter now drives
   ONLY the Walk state's playback speed (`speedParameterActive` on Walk; Idle, Attack
   and Death keep their authored fixed speed, and `Animator.speed` is never touched).
   `EnemyAnimationBridge.ComputeLocomotionSpeedMultiplier` derives the multiplier from
   `CurrentPlanarSpeed` against a `walkReferenceSpeed` (clamped 0.5–2.5); the setup
   tool derives the reference from the walk clip's own `averageSpeed` (fallback 1.3)
   and writes it onto the prefab, so the future Runner variant reuses the same
   mechanism at higher speeds. Gameplay speed is untouched (still pinned at 2.5 by
   tests). Re-run `Tools > Operation Outbreak > Set Up Basic Infected Production
   Visual` and commit the regenerated controller + prefab.

## Manual Unity QA checklist for 1Q

0. **REQUIRED FIRST STEP — run `Tools > Operation Outbreak > Set Up Basic
   Infected Production Visual`** (rebuilds the controller with the cadence
   multiplier AND sets up the prefab with the clip-derived walk reference), save,
   and commit the regenerated `OO_BasicInfected.controller` plus the modified
   `Zombie_Prototype.prefab`.
1. Basic Infected spawns with the Stylized Zombie visual.
2. Prototype enemy mesh is hidden (not rendered).
3. Zombie walks while pursuing (idle when still).
4. **Walk cadence sync (Bug 4): footstep/cycle cadence approximately matches the
   translation speed — no obvious foot skating at normal pursuit speed.**
5. Zombie faces the player correctly.
6. Zombie attack animation plays when attacking (attack timing unchanged).
7. Existing damage timing still works (unchanged).
8. Zombie death animation plays once (death timing unchanged).
9. Dead enemy cannot continue attacking.
10. Mission kill/wave accounting remains correct (3 sections, 12 enemies, 9 BASIC / 3 RUNNER, Mission Complete exactly once).
11. Multiple zombies animate independently.
12. Enemy separation still works.
13. Zombie feet/ground alignment looks correct (no floating/sinking).
14. LOD/prefab rendering produces no obvious errors.
15. Player Toon Soldier remains unaffected (shooting, aim, muzzle, animations).
16. Console remains clean.
17. Full EditMode suite passes — expect **147/147** (144 previous + 3 new
    cadence-sync tests: Walk driven by the multiplier with Idle/Attack/Death
    unaffected, multiplier pure-maths, and the approved 2.5 gameplay speed pin;
    the controller tests require step 0 to have been run once).

## What Milestone 1P.5 delivered

Character presentation replacement ONLY — zero gameplay changes:

1. **Toon Soldier as active presentation** — the imported `ToonSoldier_demo` (humanoid,
   `ToonSoldier_demoAvatar`) is the active player visual under `Player/ToonSoldierVisual`;
   `CarlVisual` is parked INACTIVE and kept fully intact as the known-good fallback.
2. **Animator controller** — `Art/Animations/Player/ToonSoldier_Player.controller`
   mirrors the verified Carl controller parameter contract exactly (Speed / IsMoving /
   Gunplay / HitReaction / Dead), so the existing single `PlayerAnimationBridge` drives
   the soldier without any code change:
   - IDLE → `assault_combat_idle` (loop),
   - MOVING → locomotion blend tree (idle → `assault_combat_run`, driven by Speed),
   - FIRING → `assault_combat_shoot` (non-looping) via the existing Gunplay trigger,
     with exit-time return to locomotion. Rapid fire cannot lock the animator.
   - Known presentation limitation: the package has no hit-reaction or death clips, so
     those two bridge parameters exist but have no states; a hit player shows no soldier
     reaction animation (enemy-side 1P hit feedback is unaffected).
3. **Bridge wiring** — the scene pins the bridge's `animator` to the Toon Soldier's
   Animator (stripped prefab reference) and assigns the controller via a prefab-instance
   override. One bridge, one authority — no second animation system.
4. **URP material** — `Materials/Player/ToonSoldier_Player.mat` (URP/Lit, same texture,
   white base colour) replaces the package's built-in "Standard" material at setup time.
   The original package material is left untouched on disk.
5. **Editor tools (presentation swap)** — `Tools > Operation Outbreak > Set Up Toon
   Soldier Player Visual` (new, idempotent) activates the soldier, deactivates Carl and
   pins the bridge to the soldier; the 1O.5 Carl tool gained the mirror toggle so Carl
   can be restored as a one-click fallback. `ToonSoldier_demo` stays at its normalized
   local transform (0,0,0 / identity / 1,1,1) — placement offsets belong on
   `ToonSoldierVisual` only.
6. **Weapon** — the model's internal rifle is presentation only. Gameplay firing,
   projectile origin, targeting and muzzle feedback remain owned by the existing
   `Weapon`/`MuzzlePoint` hierarchy. Rifle/muzzle visual alignment is a QA observation,
   not a gameplay fix.
7. **QA fix (2026-08-16)** — first manual QA found the soldier static ("Clip Count: 0"):
   the hand-authored controller's motion references guessed FBX sub-asset fileIDs that
   the Toon Soldiers package does not use. The controller is now rebuilt by Unity itself:
   `Tools > Operation Outbreak > Rebuild Toon Soldier Animator Controller` (also run
   automatically by `Set Up Toon Soldier Player Visual`) resolves the real
   `AnimationClip` sub-assets through AssetDatabase and re-author the controller with
   `UnityEditor.Animations` APIs. See ARCHITECTURE_DECISIONS.md AD-1P.5-1 addendum.
8. **QA fix #2 (2026-08-17)** — animations play, but the soldier (a) floated above the
   lane, (b) kept facing forward instead of aiming at left/right targets, and (c) fired
   from a point away from the visible rifle. All three are presentation fixes:
   - **Grounding** — FBX forensics (parsed `ToonSoldier_demo.FBX`: centimeter units,
     Z-up, feet vertices at Z = 0.004 cm) prove the model's feet sit at the model origin,
     so `ToonSoldierVisual.localPosition.y = -1` places them on the ground plane under
     the Player root's y = 1 — the same proven offset Carl uses. `ToonSoldier_demo`
     keeps its normalized transform.
   - **Visual aim** — new `ToonSoldierPresentationAim` (Player root) reads the weapon's
     new read-only `CurrentTargetTransform` and rotates ONLY `ToonSoldierVisual`'s yaw
     toward the current combat target (smoothed, shortest-path, snap epsilon, back to
     forward when no target). The gameplay Player root is never rotated; targeting and
     firing are untouched.
   - **Muzzle at the rifle** — new `WeaponMuzzleSocketBinder` (Weapon GO) re-parents the
     EXISTING authoritative `MuzzlePoint` under the Toon Soldier's humanoid Right Hand
     bone (resolved once via `Animator.GetBoneTransform`, zero per-frame work) with a
     tunable hand-local `barrelTipOffset` (default 0,0,0.6). The rifle is part of the
     skinned mesh, so the muzzle now rides the animated rifle during idle/run/shoot.
     Carl/prototype fallback: binding is skipped when the soldier is inactive, leaving
     the muzzle exactly as authored. See AD-1P.5-6.
9. **QA fix #3 (2026-08-17)** — EditMode `TurnToward_TakesTheShortestPathAcrossTheWrap`
   failed. Investigation: the runtime `TurnToward` was CORRECT (from 179° to -179° it
   turns 2° through the wrap and lands exactly on the target; the raw output 181° is the
   same orientation as -179°). The TEST was wrong on two counts: its expected value
   (-176°) was arithmetically incorrect, and it compared periodic angles with exact
   float equality instead of angular difference. Production code unchanged; the test now
   asserts via `Mathf.DeltaAngle` equivalence and a new boundary-matrix test pins
   ±179/±170 wrap cases (short direction, maxDelta respected, no ~358° rotation).
10. **QA fix #4 (2026-08-17)** — the projectile/muzzle flash still originated near the
    soldier's head/upper-body instead of the rifle barrel. Root causes: (a) the
    hand-local `barrelTipOffset` (0,0,0.6) was a blind guess — FBX forensics showed the
    real barrel tip sits ~1.25 m from the right hand's bind origin in a rotated bone
    frame, so no naive hand-local offset can land on it; (b) if the Animator's humanoid
    bones were not resolvable on the first attempt, the muzzle silently stayed at its
    authored Weapon position (Player y=1 + Weapon 0.25 ≈ head/upper-body height —
    exactly what QA photographed). Fix: `WeaponMuzzleSocketBinder` now MEASURES the
    barrel tip at startup — it bakes the soldier's SkinnedMeshRenderer, picks the
    forward-most vertex along the soldier root's facing (the visible barrel end), and
    parents the existing MuzzlePoint to a runtime socket
    (`Right Hand → ToonSoldierMuzzleSocket → MuzzlePoint`) at that measured hand-local
    position. Binding now retries over a bounded number of frames for late
    Animator/avatar init, and `Unbind()` restores the muzzle to its authored Weapon
    ownership for the Carl/prototype fallback. The authored offset remains only as a
    fallback/override when `useMeasuredBarrelTip` is disabled. See AD-1P.5-6.
11. **QA fix #5 (2026-08-17)** — `TryPickBarrelTipHandLocal_SelectsTheForwardMostVertex`
    failed while displaying identical expected/actual vectors. Diagnosis: the test used
    exact `Vector3` equality (Unity 2021.2+ `Vector3.Equals` is exact per-component), but
    the measured tip passes through `Transform.InverseTransformPoint` (float4x4 inverse),
    so it differs from the decimal-constructed expected vector at the 1e-8..1e-6 level —
    invisible at the Test Runner's 0.01 display precision, yet not bit-identical.
    Production was CORRECT (the right vertex is selected; the nearest wrong selection is
    ~1.16 units away). TEST-ONLY fix: the assertion now uses
    `Vector3.Distance(expected, actual) <= 1e-4` plus a selection-correctness guard
    (nearest wrong candidate must be > 0.5 away). No production code changed.
12. **QA fix #6 (2026-08-17)** — Play Mode QA showed the muzzle still at the face, above
    the rifle. Deep FBX forensics found the real causes: (a) the package's rifle is a
    tube of 153 vertices rigidly skinned (weight 1.0) to the Bip001 R Hand — the muzzle
    is 53.4 cm from the hand along the tube; (b) in the BIND pose the rifle points
    SIDEWAYS, so the bind-pose global forward-most vertex is the helmet/face (Head
    cluster); (c) the one-shot bake ran in Start / early LateUpdate — before the
    Animator had posed the idle animation — so it captured the bind pose and parked the
    socket at the face. Fix: the muzzle is now measured FROM THE HAND CLUSTER, which is
    pose-independent — the rifle is rigid on the hand, so the vertex farthest from the
    hand among hand-dominated vertices (dominant bone weight = hand bone, ≥ 0.9) IS the
    muzzle in every pose, at any animation time, bind pose included. The socket is
    oriented so its +Z runs hand→muzzle, keeping the flash's authored forward offset on
    the barrel line. The authored `barrelTipOffset` remains only as a last-resort
    fallback when the mesh/weights are unavailable. See AD-1P.5-6.
13. **QA fix #7 (2026-08-17)** — compile error CS0165 in `WeaponMuzzleSocketBinder`:
    the measurement call declared its out variable inside a short-circuiting `&&`
    expression (`useMeasuredBarrelTip && TryMeasureMuzzle(handBone, out Vector3
    measuredOffset)`), and C# definite-assignment analysis only considers an
    out-declared variable definitely assigned when the whole expression is definitely
    evaluated — `useMeasuredBarrelTip` is a runtime bool, so the compiler could not
    prove the call ran, even inside `if (measured)`. Fix: the variable is now declared
    with an initializer equal to the documented fallback (`barrelTipOffset`) before the
    call — semantically correct on every path; the out call overwrites it whenever
    measurement runs. Verified under real Roslyn: the old pattern reproduces CS0165 and
    the fixed file compiles with 0 errors. Runtime muzzle logic unchanged.
14. **QA fix #8/#9 (2026-08-17)** — full weapon-presentation architecture correction.
    QA showed the soldier carrying TWO weapon presentations (the old prototype gun —
    `Weapon > WeaponModel`, a scaled cube with an enabled MeshRenderer — rendered
    through the soldier) plus the Console error "Cannot set the parent of 'MuzzlePoint'
    while activating or deactivating 'ToonSoldierMuzzleSocket'" from Unbind/OnDisable.
    Fixes:
    - **Duplicate presentation removed:** the binder now hides the prototype gun's
      renderers exactly when the Toon Soldier is active AND bound
      (`ShouldHidePrototypeWeapon`); the Carl/prototype fallback restores them as
      before. All logical gameplay components (WeaponController, MuzzlePoint,
      MuzzleFlashFeedback) are preserved untouched.
    - **Follow architecture (no more re-parenting):** the MuzzlePoint stays owned by
      the Weapon forever; a runtime socket under the animated Right Hand is created and
      the muzzle FOLLOWS its world pose each frame (`DefaultExecutionOrder(-100)` so the
      follow runs before WeaponController.Update — no 1-frame firing lag). Unbind only
      stops following and destroys the socket — no SetParent exists on any deactivation
      path, so the parenting Console error is structurally eliminated, the muzzle can
      never be orphaned, and the Carl fallback is simply "stop following" (the muzzle's
      authored local pose under the Weapon was never touched).
    - **Deterministic FBX-derived socket:** the muzzle constants are now derived from
      the actual rifle geometry (hand-rigid tube, 153 verts @ weight 1.0 on the R Hand):
      hand-local muzzle position **(0.543, -0.0327, 0.0765) m**, barrel direction
      **(0.9885, -0.0595, 0.1392)** (54.9 cm from the hand). These are serialized as
      `fbxBarrelTipOffset`/`fbxBarrelDirection`; the default runtime path recomputes the
      same quantity in Unity's exact frames from the hand-rigid cluster (pose-
      independent). The old blind `barrelTipOffset (0,0,0.6)` was removed. See
      AD-1P.5-6.
15. **QA fix #10 (2026-08-17)** — compile error CS0579 (Duplicate 'Test' attribute) in
    `ToonSoldierPresentationTests.cs`: the QA fix #8/#9 edit left a stray `[Test]`
    attribute above the follow-architecture section comment, so two `[Test]`
    attributes applied to the same `WriteFollowPose_...` method. One line removed; no
    test was deleted or disabled, and no production behavior changed. Verified with a
    real Roslyn compile of the actual test + binder + aim files (0 errors).
16. **QA fix #11 (2026-08-17)** — Play Mode QA showed the old prototype gun STILL
    visible over the soldier. Root cause: the scene serialized
    `prototypeWeaponRoot: {fileID: 210010}` — the WeaponModel **GameObject** — into a
    **Transform**-typed field. Unity only resolves Transform fields from `!u!4`
    Transform fileIDs (WeaponModel's is `210011`), so the reference deserialized to
    null and every hide call returned at its first guard. Fixes: (a) the scene now
    points at `{fileID: 210011}` (WeaponModel's Transform); (b) the hide logic is
    decoupled from muzzle binding — `RefreshPrototypeWeaponVisibility()` hides the
    prototype gun whenever the Toon Soldier visual layer is ACTIVE and restores it
    when inactive (Carl fallback), evaluated every Update but writing renderers only
    on state change, so a slow or failed Animator bind can never leave the old gun
    visible. Two new EditMode tests drive the real renderer state for both cases.
17. **QA fix #12 (2026-08-17)** — firing froze the soldier's locomotion: the
    full-body shoot clip lived on the Animator BASE Layer next to the locomotion blend
    tree, so every Gunplay trigger replaced the locomotion state and the legs locked
    in the shoot pose while the code-driven player root kept moving. Fix — LAYERED
    SHOOTING (controller authored by the rebuild tool, same workflow as QA fix #1):
    - BASE Layer: NeutralStance (idle) + Locomotion blend tree ONLY — the legs are
      never interrupted by firing.
    - SHOOT Layer (weight 1, Override blending): `Gunplay` plays
      `assault_combat_shoot` under an upper-body Avatar Mask
      (`ToonSoldier_UpperBodyMask.mask` — torso/head/arms active; pelvis/hips, legs,
      fingers excluded), with an **Empty default state** that passes the base-layer
      pose through when not firing, and an exit-time (0.9, 0.15s blend) transition
      back to Empty so the upper body smoothly returns to locomotion when firing
      stops.
    - Result: idle/run continue on the legs while the upper body shoots; the bridge,
      parameters, aiming, muzzle binding and Carl fallback are all unchanged.
    Re-run `Tools > Operation Outbreak > Rebuild Toon Soldier Animator Controller`,
    then commit the regenerated controller AND the new mask asset. See AD-1P.5-7.
    - **QA fix #12A (2026-08-17)** — compile correction: Unity's
      `AvatarMask.SetTransformActive` / `GetTransformActive` take a transform INDEX,
      not a bone-name string (3 × CS1503). The "Hips" exclusion now resolves the path
      to its mask index via `GetTransformPath(i)` first (`FindTransformIndex` /
      `SetTransformActiveByPath`), with a safe no-op when the mask has no "Hips"
      path. The animator test uses the same index-based lookup. Fix #12's layered
      design is unchanged.

## Manual Unity QA checklist for 1P.5

0. **REQUIRED FIRST STEP — rebuild the controller on your machine:**
   `Tools > Operation Outbreak > Rebuild Toon Soldier Animator Controller`
   (or the full `Set Up Toon Soldier Player Visual`), then save the scene.
   This regenerates the controller asset with real clip references — skip this and the
   character will stay static exactly as in the failed QA run. Afterwards, commit the
   regenerated `ToonSoldier_Player.controller` file **and the new
   `ToonSoldier_UpperBodyMask.mask` asset (+ its .meta)** so the repository carries
   valid references.
1. **Soldier feet visually contact the ground in idle** (no floating/sinking).
2. **Soldier remains grounded while running.**
3. Target in front → soldier faces forward.
4. Target on left → soldier visibly turns/aims left.
5. Target on right → soldier visibly turns/aims right.
6. No visual jitter between targets.
7. Projectile originates at the visible rifle barrel (not head/chest/hand center).
8. Muzzle flash appears at the visible rifle barrel.
9. Muzzle follows the rifle during idle/run/shoot animation.
   - The barrel tip is measured automatically from the deformed mesh at startup. If it
     is still visibly off, set `WeaponMuzzleSocketBinder` → `useMeasuredBarrelTip` OFF
     and hand-tune `barrelTipOffset` / `barrelRotationEuler` (presentation fields only).
10. Idle/run/shoot animations still work (controller rebuilt per step 0 above).
11. Player movement/lane behavior unchanged (Player root never rotated).
12. All 3 mission sections complete normally; Mission Complete exactly once.
13. Console clean — no new errors or warnings.
14. Carl fallback: re-enable via Tools > Operation Outbreak > Set Up Carl Player Visual
    — no broken references; the muzzle returns to its authored Weapon position
    (Unbind restores the original parent/local transform).
15. Full EditMode suite passes — expect **137/137** (legitimate [Test] methods
    counted by inspection; QA fix #12 adds the four layered-shooting regression tests:
    base-layer-has-no-Gunplay, upper-body masked shoot layer with Empty default, mask
    excludes hips/legs, and Gunplay exit-transition back to Empty). The fixture
    TearDown fails on any unexpected Unity error, pinning the parenting-error class.
    The new animator tests require the step-0 rebuild to have been run once.
16. Play Mode: verify the soldier's skinned rifle is the ONLY visible weapon (the old
    prototype gun must be gone while the soldier is active), the projectile + muzzle
    flash start at the visible rifle barrel opening, the muzzle follows the rifle
    during idle/run/shoot and left/right aiming, exiting Play Mode / toggling the
    soldier / restoring Carl produces NO "Cannot set the parent ... while activating
    or deactivating" Console error, and the prototype gun reappears with Carl.

## Known discrepancies reported during 1P

- `Docs/` records referenced by the brief were absent from the entire repository history
  (see note at top). Recreated as best as possible; roadmap intentionally not fabricated.
