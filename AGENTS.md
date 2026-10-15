# PyroSync / Mitali Service Station — Agent Instructions

## 1. Production safety

This is a live petrol-pump management application used by a real client.

Never:
- modify the live production database
- connect to or write to a live client database unless explicitly instructed
- perform destructive database operations without explicit approval
- run migrations against a client/production database
- delete production records manually
- rebuild or replace the installer unless explicitly requested
- deploy anything
- push Git changes unless explicitly requested

For database investigation, use a disposable copy/clone of the database.

Never assume a database is disposable unless that has been explicitly established.

## 2. Investigation before implementation

For every requested change:

1. Understand the existing implementation.
2. Trace the complete data flow.
3. Identify the relevant models, services, repositories, ViewModels, Views and tests.
4. Determine whether existing behavior is intentional.
5. Check existing tests before changing logic.
6. Make the smallest safe change that satisfies the request.

Do not redesign working architecture unnecessarily.

Do not replace existing business rules with a theoretically cleaner implementation unless explicitly requested.

## 3. Minimal-change principle

Preserve existing behavior outside the requested scope.

Do not:
- refactor unrelated code
- rename unrelated classes
- change database schemas unnecessarily
- change formulas merely to make a displayed number look correct
- replace working synchronization logic without evidence
- introduce new abstractions when an existing abstraction already fits

If a problem can be fixed in the existing architecture, prefer that solution.

## 4. Database and schema safety

The application uses SQLite with EF Core.

Before proposing any schema change:
- determine whether the existing schema already supports the requirement
- inspect current migrations and model configuration
- determine whether the change is actually required

Do not create an EF migration merely because a feature is being changed.

Do not modify Supabase schema/scripts unless the requested change genuinely requires it.

Local database behavior and remote Supabase behavior must be considered separately.

## 5. Mitali shift model

Mitali Service Station uses:

A(D) → B(D) → C(D) → A(D+1)

Therefore:
- A's predecessor is C of the previous operational day.
- B's predecessor is A of the same day.
- C's predecessor is B of the same day.
- The next A belongs to the following day.

Never use calendar-date-only logic to determine operational predecessor relationships.

Do not introduce wall-clock time logic or effective-date logic unless explicitly requested.

## 6. Nozzle meter continuity

For a nozzle opening reading:

- Find the most recent valid closing reading in the operational predecessor sequence.
- Search backward through the operational shift sequence when the immediate predecessor does not contain a valid usable reading.
- Never cross between different nozzles.
- Never cross between different pumps.
- Never simply use MAX(ClosingReading).
- Preserve the existing validated 15-shift search behavior unless there is a demonstrated requirement to change it.

Existing Mitali production behavior and continuity tests are authoritative evidence.

## 7. Debtor and reconciliation accounting

Keep these concepts separate:

- Gross Fuel Sale
- Debtor Register
- Debtor Recovery
- Actual Collection
- Expected Collection
- Reconciliation Difference

Do not alter reconciliation formulas merely to make the displayed Gross Sale match another number.

Gross Fuel Sale must remain the pure fuel-sale amount.

Expected Collection and reconciliation logic must retain their established business meaning.

Any accounting change must be traced through the existing tests before implementation.

## 8. Duplicate data / Database Integrity

Duplicate records must be handled through the existing Database Integrity architecture.

Do not solve duplicate-data problems by silently ignoring duplicates in report calculations.

The intended workflow is:

scan → review → explicit administrator confirmation → transactional deletion/repair → sync-safe deletion/tombstone handling

Preserve legitimate same-value transactions.

Duplicate identity/lineage must be considered before deleting records.

Never delete records merely because their business values happen to be equal.

When duplicate cleanup is related to synchronized data, ensure deleted duplicate records cannot simply be resurrected by synchronization.

## 9. Sync safety

`FuelPro.Sync` and `SyncEngine` are sensitive production components.

Do not modify sync behavior unless the requested change actually requires it.

Before changing synchronization:
- understand local persistence
- understand SyncChangeLog
- understand SyncIdMapping
- understand remote behavior
- identify possible resurrection/conflict scenarios

Prefer fixing data/lineage problems at the appropriate persistence/integrity layer rather than adding calculation-level workarounds.

## 10. WPF UI rules

This application uses WPF.

Do not change established business calculations merely to solve a UI display issue.

For Mitali tank/card layouts, preserve the intentional composition:

TOP:
[CNG] [HSD] [SPEED]

SECOND:
[MS-I] [MS-II]

SPEED may extend downward independently because it contains more nozzles.

Do not introduce equal-height grid rows that unnecessarily stretch smaller cards.

Shift Total and Day Total should maintain the same intentional composition where applicable.

Visual acceptance of WPF UI changes requires human/user verification. Do not claim visual acceptance based only on compilation or automated tests.

## 11. Tests

After code changes:

- run the most targeted relevant tests first
- then run the broader test suite when appropriate
- distinguish newly introduced failures from pre-existing failures
- report exact discovered/passed/failed/skipped counts
- never hide failures merely because they are unrelated

Do not modify tests simply to make an implementation pass unless the existing test is demonstrably incorrect and that change is part of the requested work.

## 12. Release / installer safety

Current known application version:
1.3.44

Do not rebuild the installer unless explicitly requested.

Do not claim an installer is current merely because its version number matches the application version.

Packaging is a separate release step.

## 13. Git safety

Before significant implementation work:
- inspect git status
- know the current branch
- avoid overwriting unrelated user changes

Do not reset, clean, force-checkout, rebase, or otherwise discard user work unless explicitly instructed.

Do not push commits unless explicitly requested.

## 14. Reporting requirements

After every implementation task, report:

1. What was changed.
2. Why it was changed.
3. Files modified.
4. Tests added/changed.
5. Tests executed and exact results.
6. Any remaining risks.
7. Whether database/schema changes were made.
8. Whether installer/release artifacts were changed.
9. Any user action required for visual or client validation.

If something genuinely requires user/business/client input, stop and ask for that input instead of guessing.

## 15. Agent workflow

For every task:

UNDERSTAND → INSPECT → PLAN → IMPLEMENT → TEST → REPORT

Do not implement while requirements are still ambiguous.

When requirements are sufficiently clear, do not unnecessarily stop for confirmation between routine implementation steps.

If a report reveals a genuine blocker requiring user input, clearly identify exactly what information is required.

Otherwise continue the workflow.

## 16. Critical principle

This is a production business application.

Correctness and data safety are more important than speed.

Prefer:
- existing validated behavior
- minimal changes
- targeted fixes
- explicit tests
- reversible operations
- evidence from the actual code/database

Avoid:
- speculative refactoring
- overengineering
- silent business-rule changes
- calculation hacks
- destructive shortcuts.
