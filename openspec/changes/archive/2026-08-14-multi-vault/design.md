# Design: Multi-vault workspace (Model A — multi-root)

## Technical Approach

Turn `FileService`'s single `VaultRoot` + one `_watcher` into an ordered set of roots with one watcher each, plus a `GetOwningRoot(path)` resolver that every link/asset/preview/graph call site consults instead of the global field. View-models mutate their collections in place (`AddRoot`/`RemoveRoot`) rather than replacing them. All owning-root resolution flows from one longest-prefix helper, so "wikilinks resolve only within the file's vault" becomes a real invariant, not an accident of N=1.

## Architecture Decisions

### Roots storage & thread-safety
**Choice**: `VaultRoots` exposed as `IReadOnlyList<string>` backed by copy-on-write — `AddRoot`/`RemoveRoot` build a new list and swap the reference under `_rootsLock`; readers (`GetOwningRoot`, `GetVaultFiles`) read the reference and iterate a never-mutated snapshot. Watchers live in a `Dictionary<string,FileSystemWatcher>` guarded by the same lock.
**Alternatives**: `lock` around every read (contends with watcher-thread callbacks); `ConcurrentDictionary` (doesn't preserve order — top-vault semantics need order).
**Rationale**: watcher callbacks run on thread-pool threads while mutation happens on the UI thread; snapshot-swap gives lock-free reads and zero torn iteration.

### Owning-root resolution
**Choice**: `GetOwningRoot(path)` = the root that is the longest path-prefix of `path`; returns `null` on miss, never throws. `IsInsideVault(path)` = `GetOwningRoot(path) is not null` (or `true` when no roots open, preserving today's behavior).
**Rationale**: nested/overlapping roots (B inside A) resolve deterministically — the innermost root owns the file. A path under no open root returns `null`; callers fall back to today's single-root/temp behavior instead of crashing.

### Which root fired an event
**Choice**: each per-root watcher lambda closes over its `root`, so `VaultChanged` carries it: `event EventHandler<VaultChange>` where `record VaultChange(string Root, FileSystemEventArgs Args)`. `FileTreeViewModel` refreshes only that root's section. `FileChangedExternally` stays path-keyed (subscribers already filter by open-file path — no root needed).
**Alternatives**: single arg + subscriber infers root via `GetOwningRoot(e.FullPath)` — works but redoes a lookup the watcher already knew.

### Watcher disposal ordering (RemoveRoot)
**Choice**: under lock, swap `root` out of `VaultRoots` first, then `EnableRaisingEvents = false` and `Dispose()` the dict entry. An already-queued callback that fires after removal resolves to a now-unowned path and no-ops. Tabs from the closed root stay open (orphaned buffers, no live watcher) — save still works via path-keyed self-write tracking.

## Data Flow

    OpenVaultPath(p) ─→ FileService.AddRoot(p) ─→ new watcher[p]
           └─→ FileTree.AddRoot(p)  (tabs untouched)

    watcher[root] ─(thread pool)→ VaultChanged{root} ─→ FileTree refresh(root section)
    focused tab ─→ GetOwningRoot(path) ─→ Graph.BuildAsync(root) / PushPreview host / assets/

## File Changes

| File | Action | Change |
|------|--------|--------|
| `Services/FileService.cs` | Modify | `VaultRoots` + watcher dict; `AddRoot`/`RemoveRoot`; `GetOwningRoot`; `GetVaultFiles(root)`; root-param `ResolveInternalLink`/`FindInVault`/`IsInsideVault`/`CopyImageToAssets`/`BuildImageMarkdown`; `VaultChange` event; `Dispose` all watchers |
| `Services/GraphService.cs` | Modify | `BuildAsync(string root)` over `GetVaultFiles(root)` |
| `ViewModels/GraphViewModel.cs` | Modify | `BuildAsync(string root)` |
| `ViewModels/MainViewModel.cs` | Modify | `OpenVaultPath` adds root (no tab close); ctor migration+restore; `SyncGraphActiveFile`/`ToggleGraph` resolve focused tab's owning root; `VaultName` → per-root headers |
| `ViewModels/VaultsViewModel.cs` | Modify | `Activate`→`ToggleOpen`; `IsActive`→`IsOpen`, open-set semantics |
| `ViewModels/FileTreeViewModel.cs` | Modify | `AddRoot`/`RemoveRoot` in place; per-root `Refresh`; `TargetDirectory` falls back to first open root |
| `Views/MainWindow.xaml.cs` | Modify | `PushPreview` remaps `vault.local` to `GetOwningRoot(_previewSource.ActiveTab.FilePath)` (temp fallback) |
| `Services/MarkdownService.cs` | Modify | thread resolved owning root through existing `vaultRoot` param |
| `Models/AppSettings.cs` | Modify | `List<string> OpenVaultPaths` + `bool VaultPathsMigrated` |

## Interfaces / Contracts

```csharp
IReadOnlyList<string> VaultRoots { get; }
void AddRoot(string path);           // idempotent, dedup case-insensitive
void RemoveRoot(string path);        // stop+dispose watcher; tabs survive
string? GetOwningRoot(string path);  // longest-prefix; null on miss; never throws
List<string> GetVaultFiles(string root);
event EventHandler<VaultChange> VaultChanged;   // record VaultChange(string Root, FileSystemEventArgs Args)
```

## Testing Strategy

| Layer | What | Approach |
|-------|------|----------|
| Unit | `GetOwningRoot` nested/overlap/miss; `AddRoot`/`RemoveRoot` idempotency; scoped link/asset resolution stays within owning root | xUnit against temp dirs (tests-last, per project preference) |
| Unit | `VaultsViewModel.ToggleOpen` open-set; migration seeds once | delegate-injected VM, no WPF |
| Manual | Two vaults side-by-side; close-vault leaves tabs; preview image from vault B; graph follows focus | run app |

## Migration / Rollout

Additive JSON — old `settings.json` loads unchanged. Ctor: if `!VaultPathsMigrated` and `OpenVaultPaths` empty and `LastVaultPath` set → seed `OpenVaultPaths=[LastVaultPath]`, set flag, save (once). Restore: open each existing path in `OpenVaultPaths`.

## Open Questions

- [ ] None blocking. New-file-with-no-selection → top (first) open root, per proposal risk #1.
