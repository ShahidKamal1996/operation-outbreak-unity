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
   renderer, and writes the clip-derived walk reference + death window onto the
   prefab), save, and commit the regenerated `OO_BasicInfected.controller` plus the
   modified `Zombie_Prototype.prefab`. The tool's console log reports grounding Y
   (-1.005), death window (≈ 3.1 s), death state resolution, assigned renderer
   count and walk cadence reference.
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
11. **Death animation visibly plays once to completion** (QA fix #4: full-path
    `Animator.Play("Base Layer.Death")` on layer 0 at time 0; ~2.8 s clip + 0.3 s
    margin window), uninterrupted, then the zombie deactivates.
11b. **Isolation check (if death still fails):** select a spawned zombie in Play
     Mode and run `Tools > Operation Outbreak > Test Force Death On Selected
     Animator` — the console reports every precondition and the state after the
     forced entry; run it AGAIN a few frames later: it must NOT restart the clip
     and must log a HIGHER normalized time (one-shot + progression proof). If the
     zombie animates there, the problem is sequencing, not clip/avatar setup.
12. Dead enemy cannot move or attack.
12b. **Corpse settles onto the road** near the end of the death animation — no
     hovering, no sinking, and NEVER an upward pop (QA fixes #6/#7/#8: world-space
     measurement, downward-only monotonic clamp; the console prints one
     death-grounding log per pass with computedTargetY vs clampedTargetY).
12c. **Dead collider disabled:** the CapsuleCollider turns off at death and the
     next spawned enemy has it enabled again (QA fix #7).
13. Mission kill/wave accounting remains correct (3 sections, 12 enemies,
    9 BASIC / 3 RUNNER, Mission Complete exactly once).
14. Multiple zombies animate independently.
15. Enemy separation still works.
16. LOD/prefab rendering produces no obvious errors (both LODs textured).
17. Player Toon Soldier remains unaffected (shooting, aim, muzzle, animations).
18. Console remains clean.
19. Full EditMode suite passes — expect **176/176** (172 previous + 4 new
    QA-fix-#8 tests: downward-only target clamp, refinement monotonicity, no
    movement until a downward target exists, and downward settle still reaches the
    ground; the controller tests require step 0 to have been run once).
    local-space test + 5 new QA-fix-#7 tests: world-delta target formula, corpse-
    lands-on-lane invariant matrix, refinement gate, collider capture/apply round-
    trip, and mismatched-size guard; the controller tests require step 0 to have
    been run once).
    QA-fix-#6 tests: death-grounding gate, late-pose measurement threshold, pure
    grounding-target maths (incl. standing-offset consistency), and the unchanged
    standing offset pin; the controller tests require step 0 to have been run once).
    QA-fix-#5 tests: one-shot presentation gate truth table, non-looping full
    death clip, and Death state unit-speed/no-exits/no-self-re-entry; the
    controller tests require step 0 to have been run once).
    QA-fix-#4 tests: full-path hash shared with layer-0 targeting, generated
    controller contains the exact 'Base Layer.Death' path, and the path survives a
    rebuild + forced reimport; the controller tests require step 0 to have been run
    once).
    QA-fix-#3 tests: direct death entry targets the layer-0 Death state, URP
    shader check on both OO materials, vendor texture wiring check, and the
    deterministic material-selection rule; the controller tests require step 0 to
    have been run once).

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
