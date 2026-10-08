# Release implementation

## Objective
Release main commits to the existing nexoruta-demo VPS contract without infrastructure changes.

## Constraints
No root/mobile changes, commits, remote tags, SSH or real deploy. Preserve develop fixes and PR CI. Explicit Demo:Enabled only; PostgreSQL remains untouched. Work-unit commits forbidden by user.

## Scope and acceptance
SHA/run-tagged GHCR images; immutable source tag; selective health-checked deploy with shared lock, monotonic execution state, exact env preservation and running-image rollback.

## Tasks
- [x] R1 Image and configuration compatibility, focused configuration tests.
- [x] R2 Main-only release workflow and safe VPS script, mock checks.
- [x] R3 Local static, .NET and Docker validation; report actual test counts and unavailable checks.

## Route
Delegated direct; preparation and multiple non-trivial files require bounded writer. Advisory forecast: approximately 650 authored lines per repository; no PR requested.

## Checks
Configuration RED/GREEN; bash mock scenarios; bash -n; actionlint; ShellCheck; three Docker builds; dotnet restore/build/test both solutions; git diff --check. Native risk assessment read-only, no review start.

## Progress
R1: Commerce and Backoffice Docker images built; each starts its selected assembly and responds anonymously on /health/ready with curl at port 8080 in Production.
R2: actionlint 1.7.12 passed; bash -n and ShellCheck 0.11.0 passed. 14/14 mocked deployment checks passed, including signal rollback, rollback failure, first-release honesty, watermark and real shared flock.
R3: local restore/build passed without warnings. Windows culture-dependent suite ran 26 tests: 21 passed / 5 failed in existing decimal validation. Same source suite executed in Linux SDK container (CI-like environment): 26 passed / 0 failed / 0 skipped. No unrelated UI or tests changed. git diff --check passed. Read-only native assessment returned high/unassessable because new files require explicit inventory; no review started.
Parent correction: failed env restoration or image rollback retains original env, immutable rollback override and non-secret recovery metadata; temporary registry credentials are removed even when recovery artifacts remain. Extended mock check observed RED before correction, then 14/14 checks passed per repository; ShellCheck and bash -n passed again. No rollback success is reported if env restore fails; partial prior-image availability is explicitly recorded as partial.
Commit: none by explicit user instruction.

## Next
Independent verification completed: final 14/14 deploy mocks in each repository and all four bash syntax/ShellCheck checks passed; no severe defect found. Backend migration-test blocker resolved by authorized R4: exact Linux Backend CI passed 20/20 plus 10/10 focused repetitions. Native review awaits intended untracked inventory selection: selectorless status returned intended_untracked_selection_required; no review started and no approval issued. No remote operation performed.

## Final audit
All 17 final files read: backend 9, frontend 8. No code blockers found. Independent actionlint, all four bash syntax/ShellCheck checks, 28 deploy mock scenarios and git diff --check passed. Backend exact Linux CI: 20/20 passed plus 10/10 focused migration-test repetitions. Native review remains pending intended untracked inventory selection; no review started or approval issued. No live deploy, remote operation or commit performed.
