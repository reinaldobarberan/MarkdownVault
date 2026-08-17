# Proposal: Multi-vault workspace (Model A — multi-root)

## Intent

Today only one vault is open at a time. Opening another is a hard switch: every tab in every group is closed, the single `FileService.VaultRoot` is swapped, and the tree is rebuilt. Users can't keep notes from two projects side by side. Model A keeps N vault roots open and visible together in one explorer, each a closed world (no merged pool). Wikilinks and graphs stay scoped to the file's owning vault.

## Scope

### In Scope
- Several vault roots open at once in the explorer, each its own section.
- `FileService`: N open roots, one watcher per root, `GetOwningRoot(path)` helper, scoped file listing.
- Owning-root resolution for wikilinks, image assets, WebView2 preview host, and graph.
- "Administrar vaults" becomes an open/close toggle (multiple open at once).
- `AppSettings.OpenVaultPaths` + one-time migration; restore open set on launch.

### Out of Scope
- Editor/tabs/split/diff — already vault-agnostic (verified: `OpenTab`, `EditorGroupViewModel`, `CompareFiles`). Untouched.
- Merged cross-vault pool (Model B), cross-vault wikilinks, merged graph — rejected by locked decisions.
- Per-pane preview WebView (still one shared preview; only its host mapping changes).

## Capabilities

### New Capabilities
- `multi-root-workspace`: open/close multiple roots as an open-set; per-root watchers; tab lifecycle on close; startup restore + settings migration; "Administrar vaults" toggle UX.
- `vault-scoped-resolution`: resolve the owning root of a file and scope wikilinks, `assets/`, preview host, and graph to it.

### Modified Capabilities
- None (no existing `openspec/specs/`).

## Approach — the 7 open-risk decisions (plain language)

| # | Risk | Decision |
|---|------|----------|
| 1 | New file, nothing selected | Lands in the **top vault** of the explorer (first open root). The new-file box shows which vault it will go to, so it's never a surprise. |
| 2 | Image-paste target | Image goes into the `assets/` folder of the **same vault as the note you're editing** (owning root of the active tab), never a global one. |
| 3 | Closing a vault while its tabs are open | Closing a vault **hides its files from the sidebar but leaves your open tabs open** and editable; they just stop auto-refreshing from disk (its watcher is disposed). Differs from today's close-everything. |
| 4 | Preview image host | Before each preview push, remap `vault.local` to the **owning root of the focused tab**, so relative images always resolve against that note's own vault. |
| 5 | "Administrar vaults" UX | Each row gets an **open/close toggle**; several can be open at once. Opening adds a sidebar section without touching your tabs; closing removes the section (tabs stay, per #3). No more single bold "active" row. |
| 6 | Settings migration | Add `OpenVaultPaths`. A one-time `VaultPathsMigrated` flag seeds it from `LastVaultPath` **once**, then saves — so a vault you deliberately closed never comes back on the next launch. |
| 7 | Graph per vault | The graph shows the map of the **focused note's vault**; switch to a note in another vault and the graph follows. Rebuild `GraphService.BuildAsync(root)` against that root. Explicit picker deferred. |

Core model change: `VaultRoot: string?` → `VaultRoots` list with `AddRoot`/`RemoveRoot` + `Dictionary<string,FileSystemWatcher>` + `GetOwningRoot(path)`; `GetAllVaultFiles()` → scoped `GetVaultFiles(root)`; link/asset/preview helpers take the owning root. `MainViewModel.OpenVaultPath` stops closing tabs — it adds a root. `VaultsViewModel.Activate` → `ToggleOpen`.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Services/FileService.cs` | Modified | Roots list, watcher dict, `GetOwningRoot`, scoped listing, root-scoped link/asset resolution |
| `Services/GraphService.cs` | Modified | `BuildAsync(string vaultRoot)` |
| `ViewModels/MainViewModel.cs` | Modified | `OpenVaultPath` add-root (no tab close), `VaultName`, `SyncGraphActiveFile`, graph-per-focus |
| `ViewModels/VaultsViewModel.cs` | Modified | `ToggleOpen`, `IsOpen`/removable semantics |
| `ViewModels/FileTreeViewModel.cs` | Modified | `AddRoot`/`RemoveRoot` in place, `TargetDirectory` first-root fallback, per-root refresh |
| `ViewModels/GraphViewModel.cs` | Modified | Rebuild against focused vault's root |
| `Views/MainWindow.xaml.cs` | Modified | `PushPreview` remaps host to focused tab's owning root |
| `Services/MarkdownService.cs` | Modified | Thread resolved owning root through existing `vaultRoot` param |
| `Models/AppSettings.cs` | Modified | `OpenVaultPaths` + `VaultPathsMigrated` |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Orphaned tabs (no watcher) confuse on-disk edits (#3) | Med | Save still works; dirty-check on close unchanged; document behavior in UI |
| Owning-root lookup misses (path outside all roots) | Med | `GetOwningRoot` returns null → fall back to today's single-root behavior, never throw |
| Migration re-seeds a closed vault (#6) | Low | One-time `VaultPathsMigrated` flag, verified additive JSON load |
| Preview shows wrong vault's image (#4) | Med | Remap host on every push keyed to focused tab |

## Rollback Plan

Pure additive settings field (old `settings.json` loads unchanged). Revert by restoring single `VaultRoot`/one-watcher `FileService`, `LastVaultPath` startup restore, and switch-style `OpenVaultPath`/`Activate`. No data migration to undo — `OpenVaultPaths` is ignored by the reverted code.

## Dependencies

- None external. Existing WebView2 virtual-host API and `System.Text.Json` settings already in place.

## Success Criteria

- [ ] Two vaults open at once; a tab from each sits side by side and diffs, with no tab closed on open.
- [ ] Wikilink from a vault-A note never offers a vault-B target; graph shows only the focused note's vault.
- [ ] Image pasted into a vault-B note lands in vault B's `assets/`; preview renders it correctly.
- [ ] Closing a vault leaves its open tabs editable; reopening restores its sidebar section.
- [ ] Old `settings.json` migrates once to `OpenVaultPaths`; a closed vault stays closed next launch.
