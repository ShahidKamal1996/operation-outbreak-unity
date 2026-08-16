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
| **1P.5 — Toon Soldier visual integration** | **IMPLEMENTED — AWAITING MANUAL UNITY QA** (2026-08-16) |
| 1Q | NOT STARTED. Milestone 1P is accepted, so 1Q is no longer blocked — but it must not begin until the project owner explicitly authorizes it. |

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

## Manual Unity QA checklist for 1P.5

0. **REQUIRED FIRST STEP — rebuild the controller on your machine:**
   `Tools > Operation Outbreak > Rebuild Toon Soldier Animator Controller`
   (or the full `Set Up Toon Soldier Player Visual`), then save the scene.
   This regenerates the controller asset with real clip references — skip this and the
   character will stay static exactly as in the failed QA run. Afterwards, commit the
   regenerated `ToonSoldier_Player.controller` file so the repository carries valid
   references.
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
15. Full EditMode suite passes — expect **129/129** (previous 128, minus the 2 tests of
    the removed global forward-most heuristic, plus 3 new hand-cluster regression tests:
    muzzle-selected-not-face, graceful failure, and dominant-hand-weight filtering).

## Known discrepancies reported during 1P

- `Docs/` records referenced by the brief were absent from the entire repository history
  (see note at top). Recreated as best as possible; roadmap intentionally not fabricated.
