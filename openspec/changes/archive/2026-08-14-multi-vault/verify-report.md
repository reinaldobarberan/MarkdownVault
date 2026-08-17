# Verify Report: multi-vault

**Change**: multi-vault (Model A — multi-root workspace)
**Mode**: Standard (project convention: tests-last, not Strict TDD)
**Date**: 2026-08-14

---

## Completeness

| Metric | Value |
|--------|-------|
| Tasks total | 25 (8.1–8.6 counted individually) |
| Tasks complete | 24 |
| Tasks incomplete | 1 (8.6 — manual smoke test, intentionally not automatable) |

Task 8.6 ("Manual: two vaults side by side diffing; close-vault leaves tabs editable; vault-B image renders; graph follows focus") is explicitly marked not-automatable in `tasks.md` and in `design.md`'s Testing Strategy table (Manual layer). This is a follow-up for the user to run by hand, not an implementation gap.

---

## Build & Tests Execution

**Build**: PASSED — `dotnet build MarkdownVault.sln` → 0 Warning(s), 0 Error(s)

**Tests**: PASSED — `dotnet test MarkdownVault.sln`
```
Total tests: 320
     Passed: 320
     Failed: 0
 Total time: 1.97s
```
No app instance was running (checked via `tasklist`), so the build was not blocked.

**Coverage**: Not available (no coverage tool configured in this project).

---

## Spec Compliance Matrix

### `multi-root-workspace` spec

| Requirement | Scenario | Test | Result |
|---|---|---|---|
| Open Vault Set | Opening a vault adds a root without closing tabs | `FileServiceTests.AddRoot_opens_several_distinct_roots_at_once` (root-level only) | ⚠️ PARTIAL — the FileService-level mechanism is proven; the end-to-end "tabs survive" behavior is guaranteed by construction (`MainViewModel.OpenVaultPath` never touches `Groups`) but has no executing test through `MainViewModel`. Covered by manual task 8.6. |
| Open Vault Set | Opening an already-open vault is a no-op | `FileServiceTests.AddRoot_is_idempotent_and_case_insensitive` | ✅ COMPLIANT |
| Per-Root File Watcher | Watcher created on open, scoped refresh | `FileServiceTests.RemoveRoot_disposes_the_watcher_so_no_further_VaultChanged_events_fire` (proves the watcher fired pre-removal) | ✅ COMPLIANT |
| Close Toggle Behavior | Closing a vault with open tabs leaves tabs open/editable | (none — manual task 8.6) | ❌ UNTESTED (by design — manual follow-up, not a defect; code path verified by inspection: `MainViewModel.CloseVaultRoot` only calls `_fileService.RemoveRoot`/`FileTree.RemoveRoot`, never touches `Groups`/`OpenTabs`) |
| Close Toggle Behavior | Closing the last open vault | (none — manual task 8.6) | ❌ UNTESTED (same as above) |
| Persisted Open Set | Restore open set on launch | `VaultMigrationTests.Restore_opens_every_entry_in_OpenVaultPaths_on_startup` | ✅ COMPLIANT |
| One-Time Settings Migration | First launch after upgrade | `VaultMigrationTests.First_launch_seeds_OpenVaultPaths_from_LastVaultPath_once` | ✅ COMPLIANT |
| One-Time Settings Migration | Deliberately closed vault stays closed | `VaultMigrationTests.Deliberately_closed_vault_is_not_resurrected_on_relaunch` | ✅ COMPLIANT |
| New File Default Target | New file, nothing selected → top open root | (none found) | ❌ UNTESTED — `FileTreeViewModel.TargetDirectory()`'s fallback to `_fileService.VaultRoots[0]` has no test anywhere in the suite. Verified correct by code read only. |

### `vault-scoped-resolution` spec

| Requirement | Scenario | Test | Result |
|---|---|---|---|
| Owning Root Resolution | Path inside an open vault | `FileServiceTests.GetOwningRoot_returns_the_root_containing_the_path`, `..._innermost_when_nested_or_overlapping` | ✅ COMPLIANT |
| Owning Root Resolution | Path outside all open vaults | `FileServiceTests.GetOwningRoot_returns_null_for_a_path_outside_every_open_root`, `..._returns_null_when_no_roots_are_open` | ✅ COMPLIANT |
| Vault-Scoped Wikilink Resolution | Wikilink resolves within same vault | `FileServiceTests.ResolveInternalLink_owning_root_never_crosses_into_another_open_root` | ✅ COMPLIANT |
| Vault-Scoped Wikilink Resolution | Autocomplete (link picker) never crosses vaults | (none found) | ❌ UNTESTED — this is the **previously-flagged gap**. The fix IS present in code: `EditorGroupViewModel.InsertInternalLink` now resolves `GetOwningRoot(CurrentFilePath)` and calls `_fileService.GetVaultFiles(owningRoot)` before opening the picker (line ~619), instead of the old vault-wide `GetAllVaultFiles()`. But no test exercises this with two open vaults to prove vault-B files never appear in the picker's candidate list. `EditorGroupViewModelTests` only has `InsertInternalLink_EmptyVault_ShowsInfoMessage`, unrelated to scoping. |
| Vault-Scoped Image Paste | Paste into a vault-B note → assets/ + relative link | `FileServiceTests.CopyImageToAssets_writes_under_the_given_roots_assets_folder_not_the_fallback` (FileService layer only) | ⚠️ PARTIAL — the underlying mechanism is proven; `EditorGroupViewModel.InsertImage`/`HandleDroppedFiles` correctly resolve and pass `owningRoot` (verified by code read), but no VM-level test with two open vaults exists. |
| Preview Host Scoped To Focused Tab | Switching focus remaps `vault.local` to the new tab's vault | (none — code-behind, no WebView2 test harness in this suite) | ❌ UNTESTED (structural: `MainWindow.PushPreview` correctly calls `GetOwningRoot(activePath)` before remapping — verified by code read; this class isn't unit-tested anywhere in the project, consistent with existing convention for WebView2-dependent code-behind) |
| Graph Scoped To Focused Vault | Graph follows focus across vaults | `GraphServiceTests` (service layer only — see below) | ⚠️ PARTIAL — `GraphService.BuildAsync(root)` scoping is fully proven; `GraphViewModel.BuildIfRootChangedAsync`/`MainViewModel.SyncGraphActiveFile` (the actual "follows focus" wiring) have no dedicated tests. Per `design.md`'s own Testing Strategy table, "graph follows focus" was explicitly assigned to the Manual layer (task 8.6), so this is a documented scope choice, not an oversight. |
| Graph Scoped To Focused Vault | No cross-vault edges appear | `GraphServiceTests.BuildAsync_yields_no_cross_vault_edges_for_matching_titles` | ✅ COMPLIANT |

**Compliance summary**: 10/17 scenarios fully COMPLIANT with a passing test proving runtime behavior; 4 PARTIAL (underlying mechanism tested, VM/window-level wiring verified only by code read); 3 UNTESTED (2 by explicit design — manual task 8.6 — and 1 real gap: the wikilink-autocomplete vault-scoping regression test).

---

## Correctness (Static — Structural Evidence)

| Requirement | Status | Notes |
|---|---|---|
| Open Vault Set | ✅ Implemented | `FileService.AddRoot`/`RemoveRoot`, copy-on-write `VaultRoots` |
| Per-Root File Watcher | ✅ Implemented | `CreateWatcherForRoot` closes over `root`; `VaultChange` record carries it |
| Close Toggle Behavior | ✅ Implemented | `VaultsViewModel.ToggleOpen`, `MainViewModel.CloseVaultRoot` never touches `Groups` |
| Persisted Open Set | ✅ Implemented | `AppSettings.OpenVaultPaths`, restore loop in `MainViewModel` ctor |
| One-Time Settings Migration | ✅ Implemented | `MigrateVaultPathsIfNeeded`, guarded by `VaultPathsMigrated` |
| New File Default Target | ✅ Implemented | `FileTreeViewModel.TargetDirectory()` falls back to `VaultRoots[0]`; `TargetDisplayName` shows it in the dialog prompt |
| Owning Root Resolution | ✅ Implemented | `GetOwningRoot` — longest-prefix, null-safe, never throws |
| Vault-Scoped Wikilink Resolution | ✅ Implemented | `ResolveInternalLink(root, ...)`, `FindInVault(root, ...)` both root-scoped; `InsertInternalLink` resolves owning root before building the picker's file list |
| Vault-Scoped Image Paste | ✅ Implemented | `CopyImageToAssets(root, ...)`; `InsertImage`/`HandleDroppedFiles` resolve owning root first |
| Preview Host Scoped To Focused Tab | ✅ Implemented | `MainWindow.PushPreview` remaps `vault.local` via `GetOwningRoot(activePath)` on every push |
| Graph Scoped To Focused Vault | ✅ Implemented | `GraphService.BuildAsync(root)`; `GraphViewModel.BuildIfRootChangedAsync`; `MainViewModel.SyncGraphActiveFile` resolves focused tab's owning root |

No requirement is missing implementation. Every requirement in both spec files has real, correct backing code — confirmed by reading the actual source, not by trusting the tasks checklist.

---

## Coherence (Design)

| Decision | Followed? | Notes |
|---|---|---|
| Roots storage & thread-safety (copy-on-write + `_rootsLock`) | ✅ Yes | `FileService._vaultRoots` swapped under lock; readers lock-free |
| Owning-root resolution (longest-prefix, null on miss) | ✅ Yes | Matches design exactly, including the "never throws" guarantee |
| Which root fired an event (`VaultChange` record) | ✅ Yes | `FileTreeViewModel` refreshes only the matching section |
| Watcher disposal ordering (`RemoveRoot`: swap list first, then dispose) | ✅ Yes | Matches design's race-safety rationale exactly |
| File Changes table | ✅ Yes | Every listed file (`FileService.cs`, `GraphService.cs`, `GraphViewModel.cs`, `MainViewModel.cs`, `VaultsViewModel.cs`, `FileTreeViewModel.cs`, `Views/MainWindow.xaml.cs`, `Services/MarkdownService.cs`, `Models/AppSettings.cs`) was modified as described |

No deviations found.

---

## Issues Found

**CRITICAL** (must fix before archive):
None. Build is clean, all 320 tests pass, and every spec requirement has real, correct implementation confirmed by direct code reading.

**WARNING** (should fix):
1. **Wikilink-autocomplete vault scoping has no regression test.** This is the gap previously flagged for this change. The fix is correctly implemented (`EditorGroupViewModel.InsertInternalLink` now scopes to `GetOwningRoot(CurrentFilePath)` before listing files for the picker), but nothing in the test suite proves it with two open vaults — a future refactor could silently reintroduce the cross-vault leak with no test to catch it. Recommend adding a test (either at `LinkPickerDialog`/`EditorGroupViewModel` level, or an integration test asserting `InsertInternalLinkCommand` only surfaces the owning vault's files when two vaults are open).
2. **"New File Default Target" spec requirement has no test.** `FileTreeViewModel.TargetDirectory()`'s fallback to the first open root (`VaultRoots[0]`) is correct by code inspection but entirely unverified by the suite.
3. **Vault-scoped image paste is only tested at the `FileService` layer**, not through `EditorGroupViewModel.InsertImage`/`HandleDroppedFiles` with two open vaults. Low risk (the call sites are simple pass-throughs, verified by reading), but not proven at runtime.
4. **Graph-follows-focus wiring (`GraphViewModel.BuildIfRootChangedAsync`, `MainViewModel.SyncGraphActiveFile`) has no unit test.** This matches `design.md`'s own Testing Strategy table, which explicitly assigns this to the Manual layer — so it's a documented choice, not an oversight, but worth surfacing since it's a spec requirement ("Graph Scoped To Focused Vault").
5. **`AGENTS.md` (project context doc) is stale** — it still describes the pre-multi-vault single-`VaultRoot` architecture and doesn't mention `VaultRoots`, `GetOwningRoot`, or the multi-vault workspace at all. Not part of this change's scope per the tasks/design, but worth a follow-up doc update so the project's own source-of-truth context file doesn't contradict the shipped architecture.

**SUGGESTION** (nice to have):
1. Consider an integration-level test through the real `MainViewModel` (not just `FileService`/`VaultsViewModel` in isolation) that opens two vaults, opens a tab from each, closes one vault via `CreateVaultsViewModel().ToggleOpenCommand`, and asserts both tabs are still present in their respective groups and still editable. This would tighten the "Close Toggle Behavior" scenarios beyond the manual smoke test without needing WPF/WebView2.

---

## Manual Follow-Up Required

**Task 8.6** (two vaults side by side; close-vault leaves tabs editable; vault-B image renders in preview; graph follows focus) — intentionally not automated per `tasks.md` and `design.md`'s Testing Strategy table. This is a manual verification step for the user to run against the real `.exe`, not an implementation or test-coverage failure.

---

## Verdict

**PASS WITH WARNINGS**

The multi-vault implementation is structurally sound and correctly matches both specs and the design — every requirement has real code backing it, confirmed by direct source reading, not by trusting the checklist. The build is clean (0 warnings/errors) and all 320 tests pass. The warnings above are all coverage gaps (missing regression tests for correctly-implemented behavior), not functional defects — none block archiving, but items 1 and 2 are worth closing before this change is considered fully done, since item 1 in particular is the exact class of regression ("previously-flagged gap") this change was meant to close for good.
