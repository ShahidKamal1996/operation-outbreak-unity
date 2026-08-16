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

## Known discrepancies reported during 1P

- `Docs/` records referenced by the brief were absent from the entire repository history
  (see note at top). Recreated as best as possible; roadmap intentionally not fabricated.
