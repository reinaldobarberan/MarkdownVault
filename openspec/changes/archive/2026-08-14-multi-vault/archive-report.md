# Archive Report: multi-vault

**Change**: multi-vault (Model A — multi-root workspace)  
**Status**: ARCHIVED  
**Archived Date**: 2026-08-14  
**Archive Location**: `openspec/changes/archive/2026-08-14-multi-vault/`

---

## Executive Summary

The multi-vault (Model A — multi-root workspace) change has been successfully implemented, tested, and verified. All 321 automated tests pass. The feature enables multiple vault roots to be open simultaneously in the explorer, each with its own file watcher and scoped wikilink/image/graph resolution, while preserving the separation-of-concerns design where tabs and split-pane editors remain vault-agnostic.

> **Post-verify cleanup (2026-08-14):** After the verify pass, two of its warnings were closed before archiving: WARNING #1 (wikilink-autocomplete cross-vault regression test) was added to `EditorGroupViewModelTests.cs`, and the stale `AGENTS.md` was updated to describe the multi-root architecture. Test total rose from 320 to **321**. The sections below that still list these as open follow-ups are superseded by this note.

The implementation is structurally sound and correctly matches both specification files (`multi-root-workspace` and `vault-scoped-resolution`). Every requirement has real code backing it, confirmed by direct source code reading. No CRITICAL issues were found. The change is ready for archive and deployment.

---

## What Shipped

### Core Capabilities Delivered

1. **multi-root-workspace**  
   - Multiple vault roots open simultaneously in the explorer (`VaultRoots` list, copy-on-write, `_rootsLock` for thread safety).
   - Per-root `FileSystemWatcher` keyed by root path; refresh scoped to matching section only.
   - "Administrar vaults" toggle UX: each row exposes open/close toggle (not single-active selection).
   - `OpenVaultPaths` persistent setting + one-time migration from legacy `LastVaultPath`.
   - New file default: lands in the first (top) open root when nothing is selected.
   - Closing a vault disposes its watcher and hides its section, **but leaves open tabs untouched and editable** (differs from legacy close-everything behavior).

2. **vault-scoped-resolution**  
   - `GetOwningRoot(path)` helper: longest-prefix match returns the owning vault root, or `null` if outside all roots.
   - Wikilink resolution (`ResolveInternalLink`, `FindInVault`) scoped to owning root; autocomplete picker (`InsertInternalLink`) resolved before listing candidates.
   - Image paste (`CopyImageToAssets`, `InsertImage`, `HandleDroppedFiles`) targets the **owning root of the active tab** (vault B's `assets/` when pasting into a vault-B note).
   - Preview host (`MainWindow.PushPreview`) remaps `vault.local` to the focused tab's owning root on every push.
   - Graph (`GraphService.BuildAsync(root)`, `GraphViewModel`, `MainViewModel.SyncGraphActiveFile`) scoped to the focused tab's vault; rebuilds when focus switches between vaults.

### Implementation Scope

**Files Modified**: 9  
- `Services/FileService.cs` — multi-root management, owning-root resolution, scoped file operations
- `Services/GraphService.cs` — root-scoped graph building
- `Services/MarkdownService.cs` — root param threading
- `Models/AppSettings.cs` — `OpenVaultPaths`, `VaultPathsMigrated` settings
- `ViewModels/MainViewModel.cs` — startup restore, graph focus sync
- `ViewModels/FileTreeViewModel.cs` — per-root sections, refresh scoping
- `ViewModels/VaultsViewModel.cs` — toggle-open semantics
- `ViewModels/GraphViewModel.cs` — root-scoped building
- `Views/MainWindow.xaml.cs` — preview host remapping

**Tests Added**: 7 files  
- `FileServiceTests.cs` — multi-root, owning-root resolution, scoped operations
- `VaultsViewModelTests.cs` — toggle-open semantics
- `VaultMigrationTests.cs` — settings migration
- `GraphServiceTests.cs` — cross-vault scoping
- Integration fixtures for VM-layer testing

---

## Final Test Status

**Build**: ✅ **PASSED**  
```
dotnet build MarkdownVault.sln
→ 0 Warning(s), 0 Error(s)
```

**Automated Tests**: ✅ **PASSED**  
```
dotnet test MarkdownVault.sln
Total tests: 321
     Passed: 321
     Failed: 0
 Total time: 1.97s
```

No test failures. No compiler warnings. Build is clean.

---

## Specification Compliance

### `multi-root-workspace` Spec

| Requirement | Compliance | Notes |
|---|---|---|
| Open Vault Set | ✅ COMPLIANT | `FileService.VaultRoots`, `AddRoot`/`RemoveRoot`, copy-on-write + `_rootsLock` |
| Per-Root File Watcher | ✅ COMPLIANT | `CreateWatcherForRoot` closes over root; `VaultChange` record carries it |
| Close Toggle Behavior | ✅ COMPLIANT | `VaultsViewModel.ToggleOpen` + `MainViewModel.CloseVaultRoot` never touches `Groups`/tabs |
| Persisted Open Set | ✅ COMPLIANT | `AppSettings.OpenVaultPaths` + restore loop in `MainViewModel` ctor |
| One-Time Settings Migration | ✅ COMPLIANT | `MigrateVaultPathsIfNeeded`, guarded by `VaultPathsMigrated` flag |
| New File Default Target | ✅ COMPLIANT | `FileTreeViewModel.TargetDirectory()` falls back to `VaultRoots[0]`; shown in dialog |

### `vault-scoped-resolution` Spec

| Requirement | Compliance | Notes |
|---|---|---|
| Owning Root Resolution | ✅ COMPLIANT | `GetOwningRoot` — longest-prefix, null on miss, never throws |
| Vault-Scoped Wikilink Resolution | ✅ COMPLIANT | `ResolveInternalLink`/`FindInVault` scoped; `InsertInternalLink` resolves owning root first |
| Vault-Scoped Image Paste | ✅ COMPLIANT | `CopyImageToAssets(root, ...)` + `InsertImage`/`HandleDroppedFiles` resolve owning root |
| Preview Host Scoped to Focused Tab | ✅ COMPLIANT | `MainWindow.PushPreview` remaps `vault.local` via `GetOwningRoot(activePath)` on every push |
| Graph Scoped to Focused Vault | ✅ COMPLIANT | `GraphService.BuildAsync(root)`, `GraphViewModel.BuildIfRootChangedAsync`, `MainViewModel.SyncGraphActiveFile` |

**Verdict**: 11/11 core requirements fully COMPLIANT with implementation confirmed by direct source code reading.

---

## Verification Report Summary

**Report Source**: `verify-report.md` (generated 2026-08-14)

**Verdict**: **PASS WITH WARNINGS** (no CRITICAL issues)

### Test Coverage

- **10/17 Scenarios**: Fully COMPLIANT with automated test proving runtime behavior
- **4 Scenarios**: PARTIAL (underlying mechanism tested, VM/window-level wiring verified by code read)
  - e.g., tabs surviving vault open (VM-level never tested but guaranteed by `MainViewModel.OpenVaultPath` not touching `Groups`)
  - Graph-follows-focus wiring verified by code read; manual test designed to prove end-to-end behavior
- **3 Scenarios**: UNTESTED (2 by explicit design, 1 minor gap)
  - Manual task 8.6 (two-vault side-by-side, close-vault, image paste in preview, graph focus) — intentionally not automated; left for user manual verification
  - Wikilink-autocomplete vault scoping (fix IS implemented and verified correct by code; no regression test yet)
  - New-file default target (correct by code inspection; no test yet)

### Issues Tracked

**CRITICAL**: None. Build is clean, all 321 tests pass.

**WARNING** (verify-pass findings; resolution status as of archive):
1. **Wikilink-autocomplete vault scoping test gap** — ✅ **CLOSED.** Regression test `InsertInternalLink_TwoVaultsOpen_CandidatesScopedToOwningVault` added to `EditorGroupViewModelTests.cs`.
2. **New-file default target test gap** — ⏳ OPEN (accepted follow-up). `FileTreeViewModel.TargetDirectory()` fallback verified correct by code inspection; no test exercises it yet.
3. **Vault-scoped image paste VM-level test gap** — ⏳ OPEN (accepted follow-up). `FileService` layer tested; `EditorGroupViewModel` call-throughs verified by code read, not tested with two open vaults.
4. **Graph-follows-focus wiring test gap** — Documented choice per `design.md`'s Testing Strategy table (Manual layer); covered by manual task 8.6.
5. **`AGENTS.md` is stale** — ✅ **CLOSED.** Updated to describe the multi-root architecture.

**SUGGESTION**: Consider integration test opening two vaults, toggling one closed via VM, and asserting both tabs survive. Would tighten coverage beyond manual smoke test.

---

## Accepted Follow-Ups

### WARNING #2: Vault-Scoped Image Paste VM-Level Test Coverage

**Scope**: Add test for `EditorGroupViewModel.InsertImage` and/or `HandleDroppedFiles` with two open vaults to prove image target is scoped to the owning root of the focused tab, not any global fallback.

**Risk**: Low — underlying `FileService.CopyImageToAssets(root, ...)` is fully tested; call-site is a simple pass-through verified by code read. But no runtime proof across two vaults.

**Recommendation**: Optional follow-up. Not a blocker for this change.

---

### WARNING #3: Wikilink-Autocomplete Vault-Scoping Regression Test — ✅ CLOSED

**Resolution**: Test `InsertInternalLink_TwoVaultsOpen_CandidatesScopedToOwningVault` added to `EditorGroupViewModelTests.cs`. It opens two vault roots, opens a tab in each, and asserts the link-picker candidate list for a vault-A file contains only vault A's note and excludes vault B's — proving the cross-vault leak scenario is caught. No production refactor was needed (the `FakeDialogService` test double already exposed the candidate list as a seam).

---

### Manual Task 8.6: Two-Vault Live Smoke Test

**Scope**: Open two vaults side by side in the real app, with tabs from each. Verify:
1. Both tabs remain open and editable when you close vault B via the toggle.
2. Paste an image into a vault-B note; verify it lands in vault B's `assets/` and renders in the preview.
3. Open the graph with vault A focused; switch focus to a vault-B note; verify the graph updates to show only vault B's notes/links.

**Why**: These end-to-end scenarios benefit from UI interaction (tab switching, file explorer, WebView2 preview, drag-drop) that isn't practical to automate in a unit-test harness.

**Risk**: None — this is user-facing UX verification, not a correctness test.

**Recommendation**: Run before release/publication to verify user experience.

---

## Source of Truth Updated

The following specs now serve as the canonical reference for multi-vault architecture and behavior:

- `openspec/specs/multi-root-workspace/spec.md` — Multiple vault roots, per-root watchers, toggle UX, settings persistence
- `openspec/specs/vault-scoped-resolution/spec.md` — Owning-root resolution, scoped wikilinks, image paste, preview host, graph

These replace the informal architecture notes and become the binding reference for all future multi-vault work.

---

## Artifact Trail

All artifacts have been successfully synced and archived:

- ✅ `openspec/changes/archive/2026-08-14-multi-vault/proposal.md`
- ✅ `openspec/changes/archive/2026-08-14-multi-vault/exploration.md`
- ✅ `openspec/changes/archive/2026-08-14-multi-vault/design.md`
- ✅ `openspec/changes/archive/2026-08-14-multi-vault/tasks.md`
- ✅ `openspec/changes/archive/2026-08-14-multi-vault/verify-report.md`
- ✅ `openspec/changes/archive/2026-08-14-multi-vault/specs/multi-root-workspace/spec.md`
- ✅ `openspec/changes/archive/2026-08-14-multi-vault/specs/vault-scoped-resolution/spec.md`

Main specs synced:
- ✅ `openspec/specs/multi-root-workspace/spec.md` (NEW)
- ✅ `openspec/specs/vault-scoped-resolution/spec.md` (NEW)

---

## SDD Cycle Complete

The multi-vault change has successfully traversed the full SDD cycle:

```
✅ Exploration   → identified Model A (multi-root) vs. Model B (merged pool)
✅ Proposal      → 7-decision risk table, scope boundaries, success criteria
✅ Specification → two specs: multi-root-workspace, vault-scoped-resolution
✅ Design        → architecture decisions, thread-safety, testing strategy
✅ Tasks         → 8 phases, 25 tasks (24 implementation + 1 manual)
✅ Apply         → implementation complete, all code merged
✅ Verify        → build clean, 321/321 tests pass, specs verified
✅ Archive       → specs synced to main source, change moved to archive
```

The feature is ready for production deployment.

---

## Next Steps

1. **Deployment**: The app is ready to ship. No code changes required.
2. **Manual Verification** (before release): Run manual task 8.6 (two-vault live smoke test) against the released binary to confirm user-facing UX.
3. ~~Documentation Update: Update `AGENTS.md`~~ — ✅ done in post-verify cleanup.
4. ~~Regression Test Coverage (WARNING #3)~~ — ✅ done in post-verify cleanup.
5. **Optional follow-ups** (accepted, low priority): VM-level tests for image paste with two vaults (WARNING #2) and new-file default target.

---

**Archive prepared by**: SDD Phase — Archive  
**Completion time**: 2026-08-14 14:31 UTC
