# OPERATION OUTBREAK — MILESTONE LEDGER

> **PROVENANCE (2026-08-14):** This ledger was absent from the repository at the start of
> Milestone 1P and was recreated from `git log` (commit subject + SHA + date) plus the
> current repository state. Statuses marked IMPLEMENTED are backed by landed commits.
> Anything a commit cannot prove is marked UNKNOWN or POSTPONED instead of being guessed.

## Legend

- **IMPLEMENTED** — work landed on an Arena working branch and its commit(s) are below.
- **IMPLEMENTED — AWAITING MANUAL UNITY QA** — Arena implementation complete; human
  Unity QA required before the milestone is accepted (never self-verified).
- **VERIFIED** — the project owner's local Unity QA passed and the milestone was
  accepted. Arena implementation alone is never sufficient for this status.
- **POSTPONED** — work paused by milestone brief; not part of the active line.

## Ledger

| Milestone | Title | Status | Date | Evidence |
|---|---|---|---|---|
| 1A | Combat lane + camera foundation | IMPLEMENTED | 2026-08-11 | `348a972` |
| 1A.1 | Refine camera composition | IMPLEMENTED | 2026-08-11 | `47001cb` |
| 1B | Player movement prototype | IMPLEMENTED | 2026-08-11 | `c7fc1f8` |
| 1C | Basic shooting foundation | IMPLEMENTED | 2026-08-11 | `9cf757d` |
| 1D | First zombie combat prototype | IMPLEMENTED | 2026-08-11 | `a6e9273` |
| 1D.1 | Fix zombie player damage | IMPLEMENTED | 2026-08-11 | `2410038` |
| 1D.2 | Implement player death state | IMPLEMENTED | 2026-08-11 | `e47f5d5` |
| 1E | Basic player health HUD | IMPLEMENTED | 2026-08-11 | `4c36c4a` |
| 1E.1 | Fix health bar fill | IMPLEMENTED | 2026-08-11 | `8a10eef` |
| 1F | Controlled zombie waves | IMPLEMENTED | 2026-08-11 | `5c104fc` |
| 1F.1 | Fix zombie crowd separation | IMPLEMENTED | 2026-08-11 | `b4fc178` |
| 1G | Automatic enemy targeting | IMPLEMENTED | 2026-08-11 | `2d4b87f` |
| 1H | Combat feedback | IMPLEMENTED | 2026-08-11 | `1418b5c` |
| 1H.1 | Fix combat feedback visibility | IMPLEMENTED | 2026-08-11 | `d1ca9f5` |
| 1I | Game over and restart flow | IMPLEMENTED | 2026-08-11 | `b3c4120` |
| 1I.1 | Remove obsolete Unity API warning | IMPLEMENTED | 2026-08-11 | `503870e` |
| 1J.1 | Weapon upgrade foundation | IMPLEMENTED (feature POSTPONED) | 2026-08-11 | `94e914f` |
| 1J.2A | Visible upgrade gate pair | IMPLEMENTED (feature POSTPONED) | 2026-08-11 | `a9f5796` |
| 1J.2B | Gate labels + player triggers / readability passes | IMPLEMENTED (feature POSTPONED) | 2026-08-11 | `42afe3f`, `8e8163c`, `df431e8`, `32f6389`, `65c2a29` |
| 1J.3 | Gate choice application and one-choice locking | IMPLEMENTED (feature POSTPONED) | 2026-08-12 | `57468b0` |
| 1K | Mission complete flow | IMPLEMENTED | 2026-08-12 | `9d66804` |
| 1L | Second upgrade gate pair | SUPERSEDED by 1L-R | 2026-08-12 | `490a584` |
| 1L-R | Replace upgrade gates with timed upgrade pickups | IMPLEMENTED | 2026-08-12 | `65db6a8` |
| 1L-R.1 | Tighten upgrade pickup sequence timing | IMPLEMENTED | 2026-08-12 | `c561559` |
| 1L-R.2 | Randomise upgrade order and pickup spawn positions | IMPLEMENTED | 2026-08-12 | `014e2f8` |
| 1M | First structured mission flow (3 sections) | IMPLEMENTED | 2026-08-12 | `7ed7483` |
| 1N | Enemy variety foundation (Runner) | IMPLEMENTED | 2026-08-12 | `aaa338c` |
| 1N.1 | Archetype-specific spawn distance offset | IMPLEMENTED | 2026-08-12 | `ea30642` |
| 1N.2 | Per-archetype minimum spawn standoff | IMPLEMENTED | 2026-08-13 | `e81c60f` |
| 1N.2-R | Correct offset cancellation semantics + pickup window fixture | IMPLEMENTED | 2026-08-13 | `d6a62a4` |
| 1O | Automated gameplay diagnostics + EditMode test foundation | IMPLEMENTED | 2026-08-12 | `c76959c` |
| 1O-R | Correct diagnostic false failures + audit runner spawn offset | IMPLEMENTED | 2026-08-13 | `0102997` |
| 1O.5 | Real player character integration (Carl visual + animation bridge) | IMPLEMENTED | 2026-08-13 | `4349004`, `b6bc395` |
| — | Mobile rendering baseline | IMPLEMENTED | 2026-08-14 | `35f8481` |
| — | Gameplay post-processing baseline | IMPLEMENTED | 2026-08-14 | `f43af78` |
| **1P** | **Weapon & combat feel foundation** | **VERIFIED** | 2026-08-14 → QA 2026-08-16 | commits on `arena/milestone-1p-weapon-combat-feel` (see git log) — owner QA: EditMode 109/109 PASS, gameplay diagnostics PASS (0 failures), recording reviewed |
| **1P.5** | **Toon Soldier visual integration** | **IMPLEMENTED — AWAITING MANUAL UNITY QA** | 2026-08-16 | commits on `arena/milestone-1p5-toon-soldier-integration` — presentation-only swap: soldier active, Carl inactive fallback, existing bridge reused, URP material applied. QA fix 1: controller rebuilt by editor tool from real clips (hand-authored FBX sub-asset fileIDs were invalid — "Clip Count: 0"); 3 regression tests. QA fix 2 (2026-08-17): FBX-derived grounding offset -1, presentation-only aim pivot (Player root never rotated), muzzle bound to the Right Hand bone with tunable barrel offset; 7 regression tests. QA fix 3 (2026-08-17): wrap test corrected to angular-equivalence assertions (runtime TurnToward was already correct; 181° == -179°), +1 boundary-matrix regression test. QA fix 4 (2026-08-17): muzzle now MEASURES the real rifle barrel tip (baked skinned mesh, forward-most vertex) and binds to a hand socket at that position; bounded bind retries; Unbind restores authored ownership for Carl fallback; +3 regression tests. QA fix 5 (2026-08-17): barrel-tip test corrected from exact Vector3 equality to a 1e-4 positional tolerance + selection guard (production was correct; float32 transform noise broke exact equality) |
| 1Q | NEXT | NOT STARTED — 1P is verified and no longer blocks; begin only when the owner authorizes | — | — |

## Notes

- Gate systems (1J series): per milestone briefs, gate development is postponed and must
  not be resumed. The 1J commits above are retained in history; 1L-R superseded gates
  with timed pickups in the live gameplay path.
- Milestone 1P explicitly freezes all validated combat/mission balance values; the
  EditMode suite in `Assets/_OperationOutbreak/Tests/Editor/` pins the approved values
  (e.g. Basic 2.5 speed / 3 HP / 1 dmg, Runner 3.5 / 2 / 1, 3 sections of 3/4/5 enemies
  with 0/1/2 Runners).
- **Milestone 1P QA result (2026-08-16, project owner's local Unity run): VERIFIED.**
  Evidence: EditMode Test Runner 109/109 passed (Unity 6.5); gameplay diagnostics
  PASS — 39 checks, 34 passed, 0 failed, 5 warnings; Console 0 errors / 0 warnings
  during the accepted run; full gameplay recording reviewed and all combat-feedback
  behaviors confirmed. The pooled-feedback coroutine regression found in the first QA
  pass is RESOLVED (`2762433` activation-order fix, `a02c65b` test corrections).
- The 5 gameplay-diagnostics warnings concern existing RUNNER encounter/spawn-pressure
  behavior only. They are explicitly **NOT** Milestone 1P failures and do not affect
  1P acceptance; they remain a pre-existing tuning item for a future milestone.
- **Milestone 1P.5 (2026-08-16):** Toon Soldier replaced Carl as the ACTIVE presentation
  layer only — gameplay untouched (no movement, weapon, enemy, mission, camera or
  rendering values changed). The verified 1P status is unchanged. 1P.5 awaits the
  project owner's manual Unity QA and is NOT verified by Arena implementation alone.
