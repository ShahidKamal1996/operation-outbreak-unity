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
| **1Q — production enemy foundation (Basic Infected)** | **VERIFIED** (2026-08-19 — owner manual Unity QA PASSED; EditMode 205/205; verified assets committed by the owner at `0ca4d76` "Milestone 1Q: save verified production zombie ragdoll assets") |
| 1R — enemy animation & death foundation | **VERIFIED through the extended 1Q delivery** (roadmap reconciliation: 1R's scope was fully delivered and QA-verified during the 1Q implementation/QA cycle — hybrid animation → ragdoll death, stabilized ragdoll, collider lifecycle, reuse/reset. NOT rebuilt separately.) |
| **1S — enemy variant architecture** | **IMPLEMENTED — AWAITING MANUAL UNITY QA** (2026-08-19) |
| 1T | NOT STARTED — begin only when the project owner authorizes it after 1S QA. |

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
7. **QA fix #1B — Bugs 1/2/3 (2026-08-17):**
   - **Bug 1 (floating):** the vendor mesh's lowest vertex sits at +0.536 cm above the
     model root (parsed from `StylizedZombie.fbx`, Y range 0.536–198.8 cm), while the
     enemy root rides at y = 1 — so the zombie floated a full unit. Fix: the setup
     tool now derives a deterministic grounding offset from the ACTUAL instance
     geometry (`TryComputeProductionGroundingOffsetY`: lowest renderer-bound point in
     root-local space lowered to `-EnemyRootGroundHeight`, i.e. ≈ −1.005 m) and writes
     it to `ProductionVisual.localPosition.y` every run. No gameplay root/collider/
     lane changes.
   - **Bug 2 (hit vibration):** under continuous fire, one overlapping legacy
     `HitReaction` coroutine per bullet raced white/clear material flashes across ALL
     renderers AND ran the prototype scale punch — reading as head/body vibration.
     Fix: a single restart-safe flash coroutine (`StartHitFeedback`/`StopHitFeedback`)
     eliminates the flicker, and the legacy scale punch now applies only when the
     PROTOTYPE visual is the active presentation
     (`ShouldApplyLegacyTransformPunch`); the Animator-driven production zombie
     receives only the animation-safe material flash. Prototype fallback keeps its
     legacy behavior.
   - **Bug 3 (death not visible):** the death presentation window was the old 1.15 s
     constant, but the imported `zombie death` clip is ~2.8–3.0 s (FBX take
     LocalTime 2.97 s / ReferenceTime 2.80 s) — the enemy deactivated mid-animation,
     after a final flash burst. Fix: the setup tool now writes
     `deathPresentationDuration = clip.length + 0.3 s margin`
     (`ComputeDeathPresentationDuration`), hit feedback is stopped and cleared at
     death (`StopHitFeedback`), the bridge's death latch already blocks the Attack
     trigger (`ShouldPlayAttackAnimation`), and the Death state remains terminal
     (no exits). Kill/section/mission accounting is unchanged — `Died` still fires
     immediately at zero health.
8. **QA fix #1C (2026-08-17)** — compile correction: Unity's
    `AnimationClip.averageSpeed` is a **Vector3** (average root-motion velocity), not a
    float; the tool compared it directly with `0.01f` (2 × CS0019). The cadence
    reference now uses `averageSpeed.magnitude` — the scalar speed the clip's root
    actually travels at, which is what the Walk-cadence multiplier divides by. Bug 4
    behavior is identical.
9. **QA fix #3 (2026-08-17) — deterministic death entry + portable URP materials:**
   - **Death (still not visible):** the death entry relied on `CrossFadeInFixedTime`
     (a transition request that can be delayed by same-frame state machine
     evaluation) — and on the old PC the zombie rendered as a MAGENTA silhouette
     (vendor built-in shader), so even a correct death pose was nearly
     indistinguishable. Fix: the bridge now uses `animator.Play(DeathStateHash, 0,
     0f)` — an immediate, transition-independent switch of the base layer into the
     terminal Death state at normalized time 0; the Dead bool remains as the
     parameter-driven backup. The setup tool now validates that the Death state and
     death clip resolve before finishing and logs a clear warning otherwise.
   - **Materials (magenta on clean clones):** the vendor `.mat` files use the
     BUILT-IN Standard shader, which renders magenta under URP; the old PC's local
     conversions were never committed. Fix: Operation Outbreak-owned URP materials
     (`Art/Materials/Enemies/OO_Zombie_01.mat` / `OO_Zombie_02.mat` — URP/Lit with
     the vendor BaseColor/Normal/MetallicSmoothness textures wired) are now
     source-controlled, and the setup tool deterministically assigns them to EVERY
     production renderer (LOD0 + LOD1) on every run, selected from the current
     vendor material name ("02" → OO_Zombie_02, else OO_Zombie_01). The vendor
     package assets are left untouched.
10. **QA fix #4 (2026-08-18) — full-path death state targeting:** the death entry
    used `Animator.Play` with the SHORT state name hash
    (`Animator.StringToHash("Death")`), but Unity's documented Play contract is the
    FULL state path hash (`"Base Layer.Death"`); the short name can fail to resolve
    in a generated controller, leaving the enemy in its previous state until the
    parameter-driven transition — exactly the observed "death never visibly plays".
    Fix: shared constants `EnemyAnimationBridge.BaseLayerName` /
    `DeathStateFullPath` / `DeathPlayLayer = 0`; the bridge plays
    `Animator.StringToHash("Base Layer.Death")` on layer 0 at normalized time 0;
    the controller tool pins the base state machine's name to the shared constant
    and validates it. New editor isolation diagnostic
    (`Tools > Operation Outbreak > Test Force Death On Selected Animator`) forces
    the death presentation with NO gameplay involvement and logs every precondition
    (enabled / controller / layers / avatar / state resolution) plus the reported
    state after the forced entry — answering whether the clip/avatar setup itself
    can play Death.
11. **QA fix #5 (2026-08-18) — one-shot death presentation:** QA observed the death
    animation enter but its first frames repeat/restart (jerking). Evidence:
    the imported `zombie death` clip is a single ~2.97 s take and its importer meta
    has no Loop Time (non-looping default), and gameplay's `Animator.Play` is
    already gated once per death — so the restart signature came from ANY repeated
    `Play(..., 0)` call (most plausibly repeated invocations of the isolation
    diagnostic during QA, which re-issued Play at normalized time 0). Fix:
    - `EnemyAnimationBridge` now carries an explicit one-shot gate
      (`ShouldStartDeathPresentation(deathLatched, presentationStarted)`) — latched
      + not-started allows the presentation exactly once; every later path
      (repeated `Died`, diagnostics) refuses and never calls Play again.
    - The isolation diagnostic no longer re-Plays when the Animator is already in
      the Death state; it logs the CURRENT normalized time instead, so the clip's
      progression (0 → 1) can be verified across invocations without restarting it.
    - The controller tool pins the Death state to speed 1, no speed parameter, and
      the `AnyState → Death` transition to `canTransitionToSelf = false` (validated),
      so nothing in the state machine can re-enter Death and restart the clip.
12. **QA fix #6 (2026-08-18) — corpse death grounding:** with death working, QA saw
    the corpse's final pose hovering above the road. Cause: the production visual's
    standing grounding offset (-1.005) is correct for the standing Idle/Walk/Attack
    pose, but the Mixamo death clip lies the body down (root motion OFF, gameplay
    root fixed at y=1), so the resting pose's lowest point sits above the lane.
    Fix (presentation only): after the death latch, the bridge waits until the death
    clip reaches `deathGroundingSampleNormalizedTime` (0.9), then measures the
    corpse pose's lowest point from the real skinned mesh
    (`TryMeasureDeathPoseLowestLocalY`) and smoothly blends the ProductionVisual's
    local Y toward `ComputeDeathGroundingTargetY` over
    `deathGroundingBlendDuration` (0.35 s) — corpse settles onto the road near the
    end of the fall, no teleporting. A serialized fallback offset
    (`deathGroundingOffsetY` 0.6) applies only when measurement is unavailable; the
    standing offset is captured at Awake and restored on disable/reset. The gameplay
    root, collider and root-motion policy are untouched, and the one-shot death
    latch from QA fix #5 is unchanged.
13. **QA fix #7 (2026-08-18) — corpse still floated + dead collider stayed on:**
    - **Grounding coordinate-space root cause:** the #6 correction mixed spaces —
      the corpse's lowest vertex was measured in the zombie-instance-ROOT-LOCAL
      frame and subtracted from a hard-coded local ground value (-1), then the
      result was applied as a ProductionVisual-local Y. Any offset/scale in the
      instance subtree (or any pose-dependent drift between the sample point and
      clip end) made the correction miss the lane. Fix: everything now happens in
      ONE space — WORLD. The lowest corpse vertex is measured as a world Y
      (`TryMeasureDeathPoseLowestWorldY`, baked mesh → renderer.TransformPoint →
      .y), the lane surface is derived as `groundWorldY = rootWorldY -
      enemyRootGroundHeight`, and the target is
      `ComputeDeathGroundedTargetLocalY(currentVisualLocalY, lowestWorldY,
      groundWorldY)` = currentLocalY + (groundWorldY - lowestWorldY) — a pure
      world-space delta applied to the visual's local Y (valid because the parent
      chain is identity-rotated/scaled). A FINAL REFINEMENT pass at
      `deathGroundingRefineNormalizedTime` (0.99) re-measures the true resting pose
      and re-targets, closing the "measured mid-fall" gap. One diagnostic log per
      pass prints standing/current visual Y, lowest corpse world Y, ground world Y,
      delta and target so the maths can be verified from the console.
    - **Collider lifecycle:** the prototype CapsuleCollider (enemy root) stayed
      enabled on the corpse. The bridge now captures the root-level colliders'
      authored enabled states in Awake (`CaptureColliderEnabledStates`), disables
      them once at death (`DisableGameplayColliders`, right after the one-shot
      gate), and restores the snapshot on OnEnable (`ApplyColliderEnabledStates`) so
      reused enemies collide again. The visual death animation is never affected.
14. **QA fix #8 (2026-08-18) — downward-only death settle:** QA saw the zombie move
    UPWARD before settling: the first grounding sample (normalized ~0.9) can still
    catch the body mid-fall, producing a target ABOVE the current visual Y; the
    settle then lifted the visual before the clip-end refinement brought it back
    down. Fix — MONOTONIC DOWNWARD-ONLY rule: the death-grounding target starts at
    the standing ceiling and may only ever move downward.
    `ClampDeathGroundingTargetDownwardOnly(previousTarget, computedTarget,
    standingCeiling)` = min(previous, min(computed, ceiling)) is applied to every
    measurement AND refinement pass; the blend loop re-asserts
    `target = min(target, currentVisualY)` each frame. An upward "correction" (the
    corpse already below the ground) is discarded — a small sink is preferred to an
    upward pop. The settle still reaches the ground for genuine downward
    corrections, and no grounding movement occurs until a downward target exists.
15. **QA fix #9 (2026-08-18) — deactivation waits for the grounding settle:** QA saw
    the corpse begin its late downward settle and then vanish — the deactivation
    used only `deathPresentationDuration` (clip + margin), which expires before the
    late sample/refinement + blend completes. Fix: deactivation now waits for BOTH
    the death clip window AND the grounding settle. The bridge exposes
    `IsDeathPresentationComplete` (clip finished at normalized ≥ 0.999 AND
    `IsDeathGroundingComplete` = |visualY − targetY| ≤
    `deathGroundingCompletionTolerance` 0.015), with a snap-to-target once within
    tolerance so completion is reached promptly. `ZombieController.DeathFeedback`
    waits on that condition (pure `ShouldEndDeathPresentationWait`), then holds
    `postDeathPresentationHoldSeconds` (0.15) so the grounded pose is readable,
    then deactivates; a safety timeout (`deathPresentationSafetyTimeoutSeconds` 4)
    guarantees no corpse lives forever. Prototype fallback (no bridge) keeps the
    exact pre-1Q timer behavior, and the prototype shrink progress is clamped so
    the extended wait cannot over-rotate it.
16. **QA fix #10 (2026-08-19) — remove post-death sinking; corpse grounding is now
    part of the death animation:** video QA showed the fall end with the corpse
    hovering slightly above the lane, then the WHOLE visual translating downward
    after the animation — reading as "sinking in water". Root cause: the QA fix
    #6–#9 system was a POST-animation correction — it sampled the death pose late
    (normalized 0.9), refined at 0.99 and then ran a time-based `MoveTowards`
    settle AFTER the clip finished, so by construction the lowering could only
    happen (and be seen) once the body had stopped moving. Fix — DEATH-TIME-DRIVEN
    GROUNDING: the production zombie and death clip are fixed assets, so the final
    grounded Y is now determined ONCE at setup time. The setup tool samples the
    near-final death pose (normalized 0.95, via `AnimationMode.SampleAnimationClip`
    inside prefab contents, transform poses recorded and restored so the prefab
    always saves standing) and serializes the stable
    `EnemyAnimationBridge.deathGroundedVisualY` (world-space-delta formula, same
    maths as fix #7; documented fallback constant −1.5 when the measurement is
    unavailable). At runtime the bridge blends
    `standingVisualY (−1.005) → finalDeathGroundedY` as a smoothstep of the Death
    clip's normalized time between `deathGroundingStartNormalizedTime` 0.25 and
    `deathGroundingEndNormalizedTime` 0.85, with a per-frame downward-only clamp
    (the visual can never move upward, even on misconfiguration or a restart).
    The grounded Y is therefore ALREADY reached at normalized ~0.85 — well before
    the clip-finish gate at 0.999 — so the corpse rests on the road the moment
    the final lying pose arrives: no hover, no second downward motion, no
    post-animation settle, no visible sinking. After the clip finishes and the Y
    is within tolerance, no further writes occur (completely stationary). The
    obsolete runtime measurement/refinement/MoveTowards machinery is REMOVED, not
    bypassed (reflection-pinned by tests). Completion keeps the fix #9 contract:
    clip finished AND grounded Y reached; the production-only 0.15 s hold and the
    4 s safety timeout remain; collider lifecycle (fix #7), standing grounding
    (−1.005), walk cadence, materials and all gameplay are untouched.
17. **QA fix #11 (2026-08-19) — final pose calibration: sample the true near-end
    pose + small contact margin:** QA confirmed the fix #10 motion was natural
    but the final lying pose rested slightly ABOVE the road. Diagnosis: the
    fix #10 calibration sampled the death pose at normalized 0.95 — slightly too
    early, so the serialized `deathGroundedVisualY` was derived from a pose that
    is not yet the true final resting pose (the clip keeps changing vertically
    through its tail). Fix (setup-time calibration only — the death-time-driven
    architecture is unchanged, no runtime measurement, no settle):
    - the calibration sample moved to `DeathPoseMeasurementNormalizedTime =
      0.999` (1.0 minus a tiny epsilon — the last evaluable instant, the true
      near-end pose);
    - the setup tool now samples a VERTICAL PROFILE (0.95 / 0.99 / 0.999),
      bakes the skinned mesh at each pose and logs each lowest corpse world Y,
      so the tail's vertical movement is directly visible in the console;
    - a small configurable DOWNWARD contact margin
      (`deathGroundingContactMargin`, default `0.02`, clamped to [0, 0.05] by
      `ClampDeathGroundingContactMargin`) is serialized alongside and subtracted
      at runtime from the measured Y (`ApplyDeathGroundingContactMargin`) — the
      corpse prefers a very slight contact with the road over visible hovering,
      and can never sink deeply. The blend target, the completion gate and the
      misconfiguration warning all use the margin-adjusted final Y.
    Re-run `Tools > Operation Outbreak > Set Up Basic Infected Production
    Visual` and commit the regenerated prefab (the measured Y and the vertical
    profile are in the console log).
18. **1Q FINAL (2026-08-19) — hybrid animation → ragdoll death (approved
    production direction):** animation-only corpse grounding was still visibly
    hovering after fix #11, so the production death is now a two-stage hybrid:
    (1) the existing one-shot `Base Layer.Death` clip plays as an ANIMATION
    LEAD-IN for a configurable `deathRagdollHandoffSeconds` (default 0.30 s,
    inside the required 0.25–0.40 s band — the clip's body is already starting
    its fall there); (2) the bridge hands the skeleton to RAGDOLL PHYSICS
    exactly once (`EnemyRagdoll.ActivateRagdoll`: Animator disabled, bodies
    non-kinematic, ragdoll colliders enabled), and physics naturally completes
    the fall and establishes ground contact — no corpse-Y correction, no hover,
    no sinking. The presentation completes after the physics settle window
    (`deathRagdollSettleSeconds` 0.6 s), then the existing production hold
    (0.15 s) and safety timeout (4 s) despawn the corpse. New
    `EnemyRagdoll` runtime component keeps the ragdoll inert while alive
    (bodies KINEMATIC, colliders DISABLED — the gameplay CapsuleCollider stays
    the only live collider) and provides the full REUSE RESET (authored bone
    poses restored parent-before-child, velocities zeroed, kinematic states
    restored, colliders disabled, Animator re-enabled, latch cleared) called by
    the bridge on OnDisable — a pooled enemy can never spawn collapsed. New
    `Tools > Operation Outbreak > Set Up Basic Infected Ragdoll` (also called
    by the production visual setup tool) authors the ragdoll deterministically
    from the StylizedZombieAvatar: 11 major humanoid bones (Hips, Spine, Head,
    upper/lower arms, upper/lower legs — NO fingers/toes/hands/feet),
    primitive colliders (capsule along long bones, sphere for the head, radius
    from bone length capped at 0.3), ConfigurableJoints with symmetric hard
    limits per bone group (spine 45°, head 80°, shoulder 100°, elbow 120°,
    hip/knee 90°), locked linear motion, no projection, discrete collision
    detection, no interpolation — mobile-friendly. When the ragdoll is
    configured the tool ZEROES the animation grounding window
    (`deathGroundingStart/EndNormalizedTime = 0`), so the corpse-Y blend is a
    no-op and the two systems can never fight; the prototype (no production
    visual) keeps the animation-only path. Gameplay, accounting, walk/attack,
    materials, standing grounding (−1.005), root motion OFF and the Toon
    Soldier are untouched.
19. **1Q Hybrid Ragdoll QA fix #1 (2026-08-19) — stabilize the production
    ragdoll:** manual QA showed the FINAL authoring was physically unstable -
    after the handoff the limbs violently twisted/kicked/flipped ("random
    dance"). Root causes, all in the authoring: (a) every capsule blindly used
    the bone's LOCAL Y axis, so on this skeleton capsules crossed into
    neighbors; (b) the aggressive `boneLength * 0.9 / 2` radii (capped 0.3)
    made connected colliders mushroom over each other at the joints, and the
    solver kicked them apart at activation; (c) self-collision was only off
    for DIRECTLY connected pairs - thighs, forearms and torso parts could
    still hit each other (and other corpses); (d) every joint had the same
    symmetric ±90–120° freedom on ALL axes, so elbows/knees free-flailed
    instead of hinging; (e) the handoff inherited the Animator's residual
    linear/angular velocities (kinematic bodies moved per frame) straight into
    the first simulated step; (f) nothing capped angular velocity or damped
    the flailing. Fix — STABILIZED ANATOMICAL AUTHORING (same hybrid
    architecture, zero gameplay change):
    - **Collider alignment:** every capsule now lives on a per-bone child
      `RagdollCollider` holder rotated with
      `ComputeColliderAlignmentRotation` so the capsule axis follows the
      ACTUAL bone→child vector (measured per bone in local space; the head
      stays a sphere). No fixed local-Y assumption.
    - **Collider sizes:** conservative per-group radius table
      (`GetBoneColliderRadius`: 0.05–0.17 m), capsule height = measured bone
      length with a full-diameter minimum (`GetCapsuleHeight`) - connected
      pairs taper smoothly (ratio ≤ 2.5, policy-pinned).
    - **Self-collision OFF:** all ragdoll colliders live on the dedicated
      `OO_Ragdoll` layer (TagManager layer 8, committed); `EnemyRagdoll` calls
      `Physics.IgnoreLayerCollision(8, 8, true)` in Awake (guarded by
      `ShouldUseLayerSelfCollisionPolicy`) and re-asserts the collider layers.
      Corpse parts interact ONLY with the environment/road - never with each
      other, never corpse-vs-corpse. Joints keep `enableCollision = false` as
      defense in depth.
    - **Anatomical joints:** axes computed from the real bone chain
      (`ComputeJointAxes`: twist axis = bone direction, hinge axis =
      cross(parent, child) with degenerate fallbacks) and stored child-local;
      per-axis limits per group: elbows ±100° bend / ±15° twist / ±10°
      lateral and knees ±110°/±15°/±10° (HINGE-LIKE), shoulders ±80°/±60°,
      hips ±70°/±40°, spine ±30° bend / ±25° twist / ±15° lateral, head
      controlled. Zero-freedom axes are LOCKED.
    - **Stable handoff:** `EnemyRagdoll.ActivateRagdoll` now zeroes every
      body's linear/angular velocity BEFORE the Animator is disabled, then
      enables the colliders, verifies the pure `IsActivationPrepared` gate
      (velocities zeroed + Animator off + colliders on) and only then frees
      the bodies hips-first (parent-before-child array order).
    - **Physics tuning:** `maxAngularVelocity = 7` (no spin-kicks), angular
      drag 0.4 (damps flailing), linear drag 0, discrete detection, no
      interpolation, no projection; masses rebalanced (hips 1.8 heaviest,
      connected mass ratios ≤ 2.4, ceiling 4x policy-pinned).
    - **Validation + diagnostics:** the tool now reports every bone's
      collider (shape/radius/height/layer) and flags PROBLEMATIC connected
      overlap/mass pairs; also available read-only via
      `Tools > Operation Outbreak > Debug Basic Infected Ragdoll`.
    Re-run the setup tool and commit the regenerated prefab.
9. **QA fix #2 (2026-08-17) — floating, vibration and death still unresolved:**
   - **Grounding:** QA fix #1B's renderer-bounds measurement read the vendor prefab's
     EDITOR/REFERENCE pose (the vendor ships a crouched cartoon pose), not the animated
     Mixamo idle stance — the QA run measured -0.628 and the feet still floated. Fix:
     the tool now applies a DETERMINISTIC, FBX-derived offset
     (`ProductionVisualGroundingOffsetY = -1.005`): the vendor mesh's lowest vertex
     sits at +0.536 cm above the model root (parsed from `StylizedZombie.fbx`), the
     enemy root rides at y=1 and the lane is y=0, so −(1 + 0.00536) grounds the
     retargeted idle feet. Static, pose-independent, applied every run; the bounds
     calculation is removed entirely.
   - **Vibration:** the "restart-safe" flash still restarted a new white pulse on
     every bullet, so the white/base strobe ran at the fire rate (~5 Hz) — the
     vibration. Fix: a hit-flash cooldown (`hitFlashCooldownSeconds` = 0.35) gates new
     flashes (`ShouldStartHitFlash`), producing one readable pulse per window instead
     of a strobe.
   - **Death:** the parameter-driven `AnyState → Death` transition could be raced by
     the same-frame `AnyState → Attack` self-transition, and hit feedback could still
     be mid-pulse when `Died` fired. Fix: `TakeDamage` now stops ALL hit feedback
     BEFORE raising `Died`; the bridge latches Death, freezes Speed and the
     locomotion multiplier, and additionally performs a DIRECT
     `CrossFadeInFixedTime` into the Death state (name shared via
     `EnemyAnimationBridge.DeathStateName`, used by the controller tool too), so the
     death clip starts immediately and cannot be swallowed. Death has no exits, so the
     crossfade is terminal.

## Manual Unity QA checklist for 1Q

0. **REQUIRED FIRST STEP — run `Tools > Operation Outbreak > Set Up Basic
   Infected Production Visual`** (rebuilds the controller with the cadence
   multiplier, applies the DETERMINISTIC FBX-derived grounding offset −1.005,
   assigns the source-controlled OO URP zombie materials to every production
   renderer, measures the near-final death pose (vertical profile 0.95/0.99/0.999,
   calibration at the true near-end pose t=0.999), writes the stable final
   death grounded Y + the small contact margin (0.02), and — 1Q FINAL + ragdoll
   QA fix #1 — CONFIGURES THE STABILIZED HYBRID RAGDOLL DEATH (11 major
   humanoid bones; capsules aligned to each bone's REAL child direction on
   per-bone `RagdollCollider` holders; conservative per-group radii;
   anatomical per-axis ConfigurableJoint limits; self-collision OFF via the
   `OO_Ragdoll` layer; maxAngularVelocity 7; handoff 0.30 s, settle 0.6 s;
   animation grounding window zeroed so corpse-Y correction never fights
   physics)), save, and commit the regenerated `OO_BasicInfected.controller`
   plus the modified `Zombie_Prototype.prefab`. The tool's console log reports
   grounding Y (-1.005), the vertical profile, the measured final death
   grounded Y + contact margin (or the documented fallback −1.5 with a
   warning), the ragdoll bone/joint/handoff summary PLUS the per-bone
   collider diagnostics (shape/radius/height/layer, PROBLEMATIC overlap/mass
   flags), death window (≈ 3.1 s), death state resolution, assigned renderer
   count and walk cadence reference.
   `Tools > Operation Outbreak > Set Up Basic Infected Ragdoll` re-runs the
   ragdoll step alone; `Tools > Operation Outbreak > Debug Basic Infected
   Ragdoll` prints the same diagnostics read-only.
1. Basic Infected spawns with the Stylized Zombie visual.
2. **Correct textures/materials appear — NO magenta/pink** (OO URP materials
   active on LOD0 and LOD1).
3. **Feet sit on the lane** — no floating, no significant sinking.
4. Prototype enemy mesh is hidden (not rendered).
5. Zombie walks while pursuing (idle when still).
6. **Walk cadence sync (Bug 4):** footstep/cycle cadence approximately matches
   translation speed — no obvious foot skating.
7. Zombie faces the player correctly.
8. Zombie attack animation plays when attacking (attack timing unchanged).
9. Existing damage timing still works (unchanged).
10. **Continuous bullets produce readable hit feedback with NO head/body
    vibration** (QA fix #2: cooldown-gated flash — one pulse per ~0.35 s, no
    fire-rate strobe).
11. **Hybrid ragdoll death (1Q FINAL):** lethal damage registers immediately
    (accounting unchanged); movement/attacks stop; the gameplay CapsuleCollider
    disables; the one-shot Death clip plays as the animation LEAD-IN (full-path
    `Animator.Play("Base Layer.Death")`, layer 0, time 0); after ~0.30 s the
    Animator stops controlling the skeleton and the corpse hands off to ragdoll
    physics — the body falls naturally, hits the road and settles with real
    ground contact (no hover, no Y-correction sinking). The corpse stays still
    briefly (~0.6 s settle + 0.15 s hold), then deactivates. Run
    `Tools > Operation Outbreak > Test Force Death On Selected Animator` to
    verify the clip entry alone (it does NOT activate the ragdoll — gameplay
    death does).
11b. **Isolation check (if death still fails):** select a spawned zombie in Play
     Mode and run `Tools > Operation Outbreak > Test Force Death On Selected
     Animator` — the console reports every precondition and the state after the
     forced entry; run it AGAIN a few frames later: it must NOT restart the clip
     and must log a HIGHER normalized time (one-shot + progression proof). If the
     zombie animates there, the problem is sequencing, not clip/avatar setup.
12. Dead enemy cannot move or attack.
12b. **Ragdoll physics own the corpse after the handoff:** during the lead-in
     the Death clip is the only driver; after the handoff there is NO
     ProductionVisual Y correction (the animation grounding window is zeroed by
     the setup tool) — the corpse is physics-only. The desired sequence is:
     upright → killed → death clip starts → handoff at ~0.30 s → body
     NATURALLY collapses onto the road with real contact → small natural
     variation between deaths → corpse settles → stays still briefly →
     disappears. **QA fix #1 target: NO pop/explosion at the handoff, NO
     twisting/kicking limbs, NO spinning/"random dancing".** No hover, no
     sinking, no upward pop.
12c. **Dead collider disabled:** the CapsuleCollider turns off at death and the
     next spawned enemy has it enabled again (QA fix #7).
12d. **Corpse disappears only AFTER the physics settle:** the enemy stays
     visible until the bridge reports the ragdoll presentation complete (settle
     window elapsed), plus the production hold — no vanishing mid-fall.
12e. **Reuse reset:** after a zombie dies and the spawner reuses the enemy, it
     must come back UPRIGHT and animating — bones at their authored pose, no
     residual velocity, ragdoll colliders off, bodies kinematic, Animator
     running, gameplay capsule colliding. A pooled enemy must NEVER spawn
     collapsed.
12f. **Alive state:** while alive (and during the lead-in), no ragdoll collider
     collides and no ragdoll body is simulated — the zombie moves/attacks
     exactly as before (mobile perf: keep an eye on frame time with several
     enemies alive).
12g. **Self-collision off (QA fix #1):** during a corpse's fall, its body parts
     must never visibly kick off each other, and two corpses must never
     interact - the `OO_Ragdoll` layer is self-ignoring. The corpse collides
     with the ROAD/environment only. `Tools > Operation Outbreak > Debug Basic
     Infected Ragdoll` prints the per-bone collider report (no PROBLEMATIC
     flags expected).
13. Mission kill/wave accounting remains correct (3 sections, 12 enemies,
    9 BASIC / 3 RUNNER, Mission Complete exactly once).
14. Multiple zombies animate independently.
15. Enemy separation still works.
16. LOD/prefab rendering produces no obvious errors (both LODs textured).
17. Player Toon Soldier remains unaffected (shooting, aim, muzzle, animations).
18. Console remains clean.
19. Full EditMode suite passes — expect **205/205** (198 previous; the 1Q
    Hybrid Ragdoll QA fix #1 ADDED 7: capsule-alignment-follows-the-bone-child-
    direction (rotation mapping + degenerate fallback), joint-axes-follow-the-
    bone-chain (orthogonality + collinear/childless fallbacks),
    connected-colliders-do-not-significantly-overlap (10-pair ratio walk +
    boundary math), connected-mass-ratios-are-stable, activation-requires-
    zeroed-velocities-disabled-animator-and-enabled-colliders (prepared-gate
    truth table), self-collision-policy-deterministic (layer index truth table
    + pinned layer name), reuse-reset-restores-parents-before-children
    (parent-before-child order invariant); REPLACED 3: the handoff test now
    pins the ONE-SHOT gate (already-active/already-done cases), the collider
    test pins the conservative per-group radius/height policy, and the joint
    test pins the anatomical per-axis hinge-like limits; the reflection test
    pins the renamed one-shot handoff gate; the controller tests require step 0
    to have been run once).

## What Milestone 1S delivered

Data-driven enemy variant architecture — ONE reusable gameplay framework,
variant differences from data only:

1. **Enemy archetype definition** — `EnemyArchetypeDefinition`
   (ScriptableObject, assets under
   `Assets/_OperationOutbreak/Resources/EnemyArchetypes/`): identity
   (`archetypeId`, `displayName`), gameplay tuning (max health, move speed,
   attack damage/interval/range, separation radius/strength), presentation
   (production visual source path, locomotion profile name, locomotion
   controller Resources path, cadence reference, requires-ragdoll flag). All
   fields are read-only at runtime.
2. **Ownership stays clean** — `ZombieController` remains the single gameplay
   authority (it READS a definition at spawn via `ApplyArchetype`, including a
   spawn-time health re-seed); `EnemyAnimationBridge` remains the single
   presentation bridge (it READS the definition's locomotion profile at spawn
   via `ApplyArchetype`: controller swap + cadence reference). No variant
   branch exists anywhere in gameplay code, and NO per-variant controller
   class exists (reflection-pinned by tests).
3. **Basic Infected migration** — `EnemyArchetype_Basic.asset` replicates the
   VERIFIED 1Q values byte-for-byte (health 3, speed 2.5, damage 1, interval
   1, range 1.25, separation 1.1/1.5, cadence reference 0.29091793 = the
   verified prefab serialization, Walk profile, shared prefab controller — no
   swap). Applying it to the shared prefab is numerically a no-op.
4. **Runner preparation** — `EnemyArchetype_Runner.asset` validates the
   architecture: speed 4.5, health 2, same shared controller, `Run`
   locomotion profile mapping to the reserved Mixamo `zombie run` clip. The
   Runner's controller is authored by the SAME tool as the Basic controller
   (`Tools > Operation Outbreak > Rebuild Runner Animator Controller` writes
   `Assets/_OperationOutbreak/Resources/EnemyArchetypes/OO_Runner.controller`
   — identical state machine, run clip in the locomotion state — and patches
   the archetype's cadence reference from the run clip's measured average
   speed). Full Runner mission/content belongs to the later Chapter 1 Runner
   milestone; this milestone only proves the framework supports it.
5. **Runtime registry** — `EnemyArchetypeRegistry` resolves stable ids from
   `Resources.LoadAll` (no scene wiring, build-safe), reports duplicate ids as
   errors (first asset wins) and resolves null/empty/unknown requests to the
   DEFAULT (`basic_infected`) — the verified Basic behaviour for every caller
   that asks for nothing.
6. **Spawner seam** — `EnemySpawner.SpawnEnemy(string archetypeId)` /
   `SpawnEnemy(EnemyArchetypeDefinition)` /
   `SpawnEnemyWithDefinition(definition, position)`: instantiate the SHARED
   gameplay prefab, apply the definition, then run the exact bookkeeping every
   other spawn runs (SetTarget, Died subscription, `_activeEnemies` tracking,
   `EnemySpawned` report). The existing 1N mission composition path is
   deliberately UNTOUCHED — the current mission keeps spawning exactly what it
   always spawned. 1T mission definitions will call this seam.
7. **Prefab strategy** — ONE shared gameplay prefab
   (`Zombie_Prototype.prefab`) for every production variant; variants differ
   only by applied data (stats + controller swap). No prefab duplication. The
   legacy `Runner_Prototype.prefab` remains ONLY for the current scene's 1N
   composition (prototype fallback) and is not part of the new architecture.
8. **Animation/profile strategy** — locomotion presentation is a
   per-archetype AnimatorController profile: same state machine contract
   (Speed/Attack/Dead/LocomotionSpeedMultiplier, `Base Layer.Death` full
   path), different locomotion clip. `EnemyAnimationSetup` was generalized
   (`ResolveLocomotionClipPath`, parameterized rebuild + validation) — no
   hard-coded "if runner play X" anywhere.
9. **Hybrid ragdoll stays SHARED** — death accounting, animation lead-in,
   stabilized handoff, collider lifecycle and reset/reuse all live on the
   shared prefab and are identical for every production archetype. The
   definition only declares `requiresRagdoll` (editor-validated against the
   shared prefab); no variant forks the death system.
10. **Validation** — definition-level checks (stable id, ranges, locomotion
    setup, run profile requires its controller), duplicate-id detection and
    asset-level checks (production prefab resolves, shared prefab ragdoll
    configured, runner controller exists) via
    `Tools > Operation Outbreak > Validate Enemy Archetypes`. Broken
    archetypes fail loudly in editor/dev QA; runtime spawns also log clear
    errors (missing controller, unknown id) instead of silently spawning
    incorrectly.
11. **Debug spawns** — `Tools > Operation Outbreak > Spawn Basic Infected
    (Debug)` / `Spawn Runner (Debug)` prove the shared framework in Play Mode
    (mission-tracked through the spawner seam when a spawner is in the scene).

## Manual Unity QA checklist for 1S

0. **REQUIRED FIRST STEP — run `Tools > Operation Outbreak > Rebuild Runner
   Animator Controller`**, save, and commit the generated
   `Assets/_OperationOutbreak/Resources/EnemyArchetypes/OO_Runner.controller`
   (and the patched `EnemyArchetype_Runner.asset` if its cadence reference
   changed). Then run `Tools > Operation Outbreak > Validate Enemy
   Archetypes` — it must PASS (before the runner controller is generated it
   correctly reports the missing controller as an error; that is the
   fail-loudly behaviour working).
1. The existing current mission looks and plays EXACTLY as before: 9 BASIC +
   3 RUNNER entries from the 1N composition, identical visuals, speeds,
   3 sections, Mission Complete exactly once. No new console errors.
2. Basic stats/animation/death behaviour unchanged (speed 2.5, health 3,
   walk cadence, hit feedback, hybrid ragdoll death).
3. In Play Mode run `Tools > Operation Outbreak > Spawn Runner (Debug)`: the
   spawned Runner uses the SAME shared framework — Stylized Zombie visual,
   configured higher speed (4.5), RUN animation, same attack, same hybrid
   ragdoll death, same collider lifecycle and reuse reset.
4. Run `Tools > Operation Outbreak > Spawn Basic Infected (Debug)` after the
   Runner: Basic still behaves exactly as before.
5. Kill the debug-spawned enemies: death lead-in → stabilized ragdoll fall →
   settle → despawn (no random dancing), reused enemies respawn upright.
6. Mission counts/sections/Mission Complete remain unchanged after the debug
   spawns (or restart and re-verify the mission).
7. Console clean (the only expected logs are the 1S debug/validation logs).
8. Full EditMode suite passes — expect **216/216** (205 previous + 11 new
   1S tests: verified Basic values, production Basic presentation, shared
   controller application, run clip/profile resolution, unique stable ids,
   invalid-archetype rejection, spawner seam resolution, default-to-Basic
   behaviour, no duplicated controller classes, shared ragdoll across
   archetypes, runner controller tool path pin).

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
    - **QA fix #12B (2026-08-17) — Toon Soldier animator persistence:** Unity logged
      "Controller 'ToonSoldier_Player': Statemachine for layer 'Shoot Layer' is
      missing" after every editor/domain reload and scene restore, until the rebuild
      tool was re-run. Root cause: the rebuild created the Shoot Layer's nested
      `AnimatorStateMachine` IN MEMORY ONLY — it was never added as a controller
      sub-asset, so the serialized layer kept `m_StateMachine: {fileID: 0}`. Fix:
      `AssetDatabase.AddObjectToAsset(shootMachine, controller)` (+
      `HideFlags.HideInHierarchy`) before `AddLayer`, and `ClearController` now also
      removes orphaned nested state machines from previous rebuilds (never the base
      layer's root). A new EditMode test performs the full persistence round-trip:
      rebuild → SaveAssets → `ImportAsset(ForceUpdate)` → reacquire from disk →
      assert both layers, the shoot state machine, its Empty/Gunplay states and the
      mask all survive. The generated controller asset must be regenerated locally
      once (step 0) and committed.

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
15. Full EditMode suite passes — expect **154/154** (counted by inspection;
    the QA fix #12B persistence round-trip test is included: rebuild → SaveAssets →
    forced reimport → reacquire from disk → shoot layer state machine and contents
    must survive). The animator tests require the step-0 rebuild to have been run
    once, and the regenerated `ToonSoldier_Player.controller` (now with the Shoot
    Layer state machine persisted as a sub-asset) must be committed.
16. Play Mode: verify the soldier's skinned rifle is the ONLY visible weapon (the old
    prototype gun must be gone while the soldier is active), the projectile + muzzle
    flash start at the visible rifle barrel opening, the muzzle follows the rifle
    during idle/run/shoot and left/right aiming, exiting Play Mode / toggling the
    soldier / restoring Carl produces NO "Cannot set the parent ... while activating
    or deactivating" Console error, and the prototype gun reappears with Carl.

## Known discrepancies reported during 1P

- `Docs/` records referenced by the brief were absent from the entire repository history
  (see note at top). Recreated as best as possible; roadmap intentionally not fabricated.
