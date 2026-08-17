# Tasks: Multi-vault workspace (Model A — multi-root)

Tags: `[M]` mechanical · `[D#]` = proposal decision # (7-decision table).

## Phase 1: FileService Core (Foundation)

- [x] 1.1 `[M]` `Services/FileService.cs`: `VaultRoot: string?` → `IReadOnlyList<string> VaultRoots`, copy-on-write + watcher dict under `_rootsLock`.
- [x] 1.2 `[M]` `AddRoot(path)` — idempotent, case-insensitive dedup, one watcher per new root.
- [x] 1.3 `[D3]` `RemoveRoot(path)` — drop from `VaultRoots` first, then disable+dispose its watcher; tabs from that root stay open untouched.
- [x] 1.4 `[M]` `GetOwningRoot(path)` — longest-prefix match, `null` on miss; `IsInsideVault` built on it (true when no roots open).
- [x] 1.5 `[M]` `GetAllVaultFiles()` → `GetVaultFiles(string root)`, scoped to one root.
- [x] 1.6 `[D2]` Root param on `ResolveInternalLink`/`FindInVault`/`CopyImageToAssets`/`BuildImageMarkdown`, scoped to owning root.
- [x] 1.7 `[M]` `VaultChanged` → `EventHandler<VaultChange>`, `record VaultChange(string Root, FileSystemEventArgs Args)`; watchers close over their root.
- [x] 1.8 `[M]` `Dispose()` stops/disposes every watcher.

## Phase 2: AppSettings + Migration

- [x] 2.1 `[M]` `Models/AppSettings.cs`: add `List<string> OpenVaultPaths`, `bool VaultPathsMigrated`.
- [x] 2.2 `[D6]` `MainViewModel` ctor: if unmigrated, `OpenVaultPaths` empty and `LastVaultPath` set → seed from it, set flag, save once.
- [x] 2.3 `[M]` Ctor: `AddRoot` on `FileService`+`FileTreeViewModel` per `OpenVaultPaths` entry on startup.

## Phase 3: FileTreeViewModel

- [x] 3.1 `[M]` `AddRoot`/`RemoveRoot` mutate the root-section collection in place (no full rebuild).
- [x] 3.2 `[M]` Subscribe to `FileService.VaultChanged`; refresh only the matching `Root` section.
- [x] 3.3 `[D1]` `TargetDirectory` falls back to first `VaultRoots` entry when nothing selected; new-file dialog shows the resolved target.

## Phase 4: VaultsViewModel (Administrar vaults UX)

- [x] 4.1 `[D5]` `Activate`/`IsActive` (single-select) → `ToggleOpen`/`IsOpen` (open-set, several rows open at once).
- [x] 4.2 `[M]` `ToggleOpen` calls `AddRoot`/`RemoveRoot` on `FileService`+`FileTreeViewModel`, updates+saves `OpenVaultPaths`.

## Phase 5: MainViewModel Wiring

- [x] 5.1 `[M]` `OpenVaultPath` no longer closes tabs — calls `AddRoot` on `FileService`+`FileTreeViewModel` only.
- [x] 5.2 `[M]` `VaultName` becomes per-root header text for tree sections.
- [x] 5.3 `[D7]` `SyncGraphActiveFile`/`ToggleGraph` resolve focused tab's owning root, pass to `GraphViewModel`/`BuildAsync(root)` on focus change.

## Phase 6: Graph Scoping

- [x] 6.1 `[M]` `GraphService.BuildAsync()` → `BuildAsync(string root)`, iterates `GetVaultFiles(root)` only.
- [x] 6.2 `[D7]` `GraphViewModel` accepts a root, rebuilds via `BuildAsync(root)` on focus moving to a different vault; no cross-vault nodes/edges.

## Phase 7: Preview + MarkdownService

- [x] 7.1 `[D4]` `MainWindow.xaml.cs`: `PushPreview` calls `GetOwningRoot(ActiveTab.FilePath)` before each push, remaps `vault.local` host to it; fall back on `null`.
- [x] 7.2 `[M]` `MarkdownService.cs`: thread the resolved owning root through the existing `vaultRoot` param.

## Phase 8: Tests (tests-last — after implementation lands)

- [x] 8.1 `FileServiceTests.cs`: `GetOwningRoot` nested/overlap → innermost; outside-all-roots → `null`.
- [x] 8.2 Same file: `AddRoot`/`RemoveRoot` idempotency/dedup; `RemoveRoot` disposes watcher, drops root.
- [x] 8.3 Same file: scoped link/asset resolution never crosses roots (spec: "resolves within same vault", "paste into vault-B note").
- [x] 8.4 `VaultsViewModelTests.cs`: `ToggleOpen` open-set semantics; migration seeds once, closed vault stays closed. (Migration covered in new `VaultMigrationTests.cs`, exercised through the real `MainViewModel` ctor.)
- [x] 8.5 `GraphServiceTests.cs`: `BuildAsync(root)` yields no nodes/edges from a second vault with matching titles.
- [ ] 8.6 Manual: two vaults side by side diffing; close-vault leaves tabs editable; vault-B image renders; graph follows focus. (Not automatable — left for manual verification.)
