# Exploration: Multi-vault (Modelo A — workspace multi-raíz)

## Problem framing

Today MarkdownVault has exactly one vault open at a time. `FileService.VaultRoot`
is a single `string?`, `FileTreeViewModel.LoadVault` replaces the tree wholesale,
and `MainViewModel.OpenVaultPath` treats opening a new vault as a hard switch —
it closes every tab in every group before swapping the root. Switching vaults
means abandoning whatever you had open, which forces the same
open-a-different-environment friction the user is trying to escape from
Obsidian.

The requested feature (Modelo A, VS Code–style multi-root workspace) keeps
several vault roots open and visible **simultaneously** in one file explorer —
not merged into a single pooled namespace (Modelo B, explicitly rejected). The
payoff: one running instance where a tab from vault A (e.g. a project's docs)
and a tab from vault B (e.g. that project's source notes) can sit side by side,
diffed or split, without juggling two windows.

## Decisions already locked (not reopened here)

- **Model A** (multi-root workspace): N roots open and visible together, each
  root's contents stay logically separate.
- **Wikilinks `[[x]]` resolve only within the owning vault of the file**
  (closed namespace per vault). No cross-vault jump target is ever offered or
  silently followed.
- **Graph is one-per-vault**, never a merged graph across roots.

## Current state — verified per file

### Already vault-agnostic (confirmed, no change needed)

- **`Models/OpenTab.cs`** — stores only a `FilePath` string plus editor state
  (content, dirty, scroll, caret). No notion of "which vault" at all. Its
  `FilePathChanged` re-key path (used by Save-As, bug #273 fix) works on any
  path regardless of root.
- **`ViewModels/EditorGroupViewModel.cs`** — `OpenFileAsync` (line 208) opens
  purely by path via `_fileService.ReadFileAsync` (`File.ReadAllTextAsync`, no
  vault-boundary check whatsoever). Tab dedup (`Owns`/`Find`, lines 322-327),
  split-editor pane logic, `MoveTabToOtherGroup`, and the diff/"Comparar
  archivos" feature (`MainViewModel.CompareFiles`, lines 436-505) all operate
  on `OpenTab.Content` in memory — they never touch `VaultRoot`. Two tabs from
  two different vaults can already be diffed today with zero changes.
- **`Services/FileService.cs` self-write tracking** — `_selfWriteTimes` and
  `_selfWriteGuardUntil` (lines 17-27) are `Dictionary<string, DateTime>` keyed
  by full path, not by root. Safe as-is for N watchers.

### Single-vault coupling — confirmed, needs the delta below

| File | Current shape | Multi-root delta |
|---|---|---|
| `Services/FileService.cs` | `VaultRoot` is one `string?` (line 30); one `_watcher` field (line 12), (re)created wholesale in `OpenVault(path)` (lines 45-73), which **replaces** the previous watcher/root instead of adding one | `VaultRoot` → a collection of open roots (e.g. `IReadOnlyList<string> VaultRoots`); `_watcher` → `Dictionary<string, FileSystemWatcher>` keyed by root, with `AddRoot`/`RemoveRoot` instead of a replacing `OpenVault`. Needs a new `GetOwningRoot(string path)` helper — nothing in the class currently answers "which of my N roots contains this path," and several call sites below need exactly that. |
| `Services/FileService.cs` — `GetAllVaultFiles()` (line 268) | Lists files under the single `VaultRoot` | Per the closed-namespace decision, every caller of this method needs a **scoped** variant — `GetVaultFiles(string root)` — not a flattened list across all open roots. The internal-link picker and the graph both call this and must never see cross-root results. |
| `Services/FileService.cs` — `ResolveInternalLink` / `IsInsideVault` / `FindInVault` (lines 292-370) | All three close over the single `VaultRoot`; `FindInVault` searches that one root only, `IsInsideVault` guards the "outside the vault → throw" case against it | These must take the **owning root of `currentFilePath`** as an explicit parameter (or resolve it internally via the new `GetOwningRoot`), not the global field. This is the concrete mechanism that enforces "wikilinks resolve only within the owning vault" — today it's accidentally true only because there's exactly one root. |
| `Services/FileService.cs` — `CopyImageToAssets` / `BuildImageMarkdown` (lines 378-410) | `assets/` lands under the single `VaultRoot` (or a fallback dir); markdown path is built relative to that same single root | Must target the `assets/` folder under the **owning root of the file being edited**, not a global root — otherwise an image pasted into a vault-B note lands in vault A's `assets/` folder. |
| `ViewModels/FileTreeViewModel.cs` — `LoadVault(path)` (line 60) | Replaces `RootNodes` wholesale with one root | Needs `AddRoot(path)` / `RemoveRoot(path)` that mutate the existing `ObservableCollection<VaultFileNode> RootNodes` in place. `RootNodes` is already plural and `ApplyFilter`/search (lines 174-198) already iterate all roots — that part needs no change. |
| `ViewModels/FileTreeViewModel.cs` — `Refresh()` (line 202-206) | `if (_fileService.VaultRoot is not null) LoadVault(_fileService.VaultRoot)` — single-root reload, and it is wired to `_fileService.VaultChanged`, a **single** event with no indication of which root fired | With N watchers, `VaultChanged` needs to say (or the handler needs to determine) which root changed, or the simplest correct fix is a full rebuild of all roots on any change — acceptable given `BuildTree` is already a full-rebuild operation today, just needs to run once per root instead of once total. |
| `ViewModels/FileTreeViewModel.cs` — `TargetDirectory()` (line 208-214) | Falls back to the single `_fileService.VaultRoot` when nothing is selected (new file/folder with no selection) | **Open risk, no obvious single right answer** — see Risks below. |
| `ViewModels/VaultsViewModel.cs` — `Activate(path)` (line 101) | `_openVault(path)` + `ActivePath = path` — a **switch**: one active path at a time, `IsActive`/`IsRemovable` (VaultRowViewModel, lines 110-133) assume exactly one active row | Must become an **open-set toggle**: `ToggleOpen(path)` that adds/removes `path` from the open-roots set rather than replacing it. `IsActive` → `IsOpen`; `IsRemovable` → "not currently open" (same guard rationale, different set semantics). This is a real UI/semantics rewrite, not a rename. |
| `ViewModels/MainViewModel.cs` — `OpenVaultPath(path)` (line 600-616) | **The actual switch implementation.** Closes every tab in every group (line 607-608, deliberately, per its own comment: "Independent-vault switch: files from the previous vault don't belong to the new one"), then `_fileService.OpenVault(path)` and `FileTree.LoadVault(path)`, both single-root calls | This is the method that most directly contradicts Model A's goal. It must become "add a root to the open set" (no tab closing) instead of "replace the open root" (close everything). This is the highest-leverage single change in the whole feature. |
| `ViewModels/MainViewModel.cs` — `VaultName` (line 528-531) | `_fileService.VaultRoot` singular, used for a status-bar-style single vault name | No longer meaningful as one label. Needs to become either a list rendered in the explorer headers (VS Code shows each root's folder name as a tree-section header) or dropped in favor of that.  |
| `ViewModels/MainViewModel.cs` — `SyncGraphActiveFile()` (line 226-232) | Computes the active file's vault-relative path against the single `_fileService.VaultRoot` | Must resolve against the **owning root of `FocusedGroup.ActiveTab.FilePath`**, not a global root — otherwise the graph highlight breaks the moment the focused tab belongs to a non-"first" vault. |
| `Services/GraphService.cs` — `BuildAsync()` (line 33) | Takes no parameter; reads `_fileService.VaultRoot` and `_fileService.GetAllVaultFiles()` directly — implicitly single-vault | Needs a `BuildAsync(string vaultRoot)` (or equivalent) overload against the new scoped `GetVaultFiles(root)`. Per the locked decision, this returns one graph for one root — never merged. |
| `ViewModels/GraphViewModel.cs` | One instance, owned once by `MainViewModel.Graph` (line 37: `new GraphViewModel(new GraphService(fileService))`), workbench-wide by design (Phase 2/Decision 2 — promoted out of per-pane) | With N roots and "one graph per vault" locked, this becomes either (a) one `GraphViewModel` per open root with a selector UI (tab strip / dropdown to pick which vault's graph is showing), or (b) a single `GraphViewModel` that is rebuilt against whichever root is selected, mirroring how `ShowGraph`/`ToggleGraph` works today but adding a "which vault" input. Needs its own small design decision — not just mechanical. |
| `Models/AppSettings.cs` | `LastVaultPath` (single, drives startup reopen) + `KnownVaultPaths` (history for the "Administrar vaults" picker, NOT the same as "currently open") | Needs a new field, e.g. `OpenVaultPaths: List<string>` — the set of roots open when the app last closed, restored on the next launch (replacing the single `LastVaultPath` restore in `MainViewModel`'s ctor, line 110-114). `KnownVaultPaths` (history) stays as-is; it already supports multiple entries. |

### Not verified in this pass but load-bearing — new finding, not in the original brief

**`Views/MainWindow.xaml` (line 415) + `Views/MainWindow.xaml.cs` (`PushPreview`,
lines 407-423) + `Services/MarkdownService.cs` (`RenderToHtml` /
`PrepareHtmlForPreview`, lines 52, 220)**

The claim "the editor layer is already vault-agnostic" is true for the
AvalonEdit/tab layer but **not fully true for the preview layer**:

- There is exactly **one `WebView2` control (`PreviewWebView`) for the whole
  window**, not one per pane — confirmed in `MainWindow.xaml` (single
  `<wv2:WebView2 x:Name="PreviewWebView"/>` inside the outer `DockPanel`,
  outside both `EditorView` instances for panes A/B). It renders only
  `FocusedGroup`'s content, even when split-editor has two panes visible from
  two different vaults.
- WebView2 resolves every relative image/asset path in the rendered HTML
  through a **single virtual host mapping**: `SetVirtualHostNameToFolderMapping("vault.local", vaultRoot, ...)`.
  `PushPreview()` (line 407-423) re-maps this on every preview push, but keys
  it off `App.FileService?.VaultRoot` — the same single global field this
  whole change eliminates.
- `MarkdownService.RenderToHtml`/`PrepareHtmlForPreview` take a single
  `string? vaultRoot` parameter (used only to decide whether to inject the
  `http://vault.local/` base href) — also single-root shaped.

**Why this matters concretely:** once `FileService.VaultRoot` becomes a set of
roots, `PushPreview` cannot keep reading "the" vault root — it must resolve the
owning root of whichever tab is currently driving the preview
(`FocusedGroup.ActiveTab.FilePath`) via the same `GetOwningRoot` helper needed
elsewhere, and remap the virtual host to that root before rendering. This is a
contained fix (one call site, `PushPreview`, plus threading the resolved root
through `MarkdownService`'s existing `vaultRoot` parameter) but it is a real
piece of work the original brief did not call out, and skipping it would mean
relative images silently render broken (or worse, render an unrelated vault's
image) whenever the focused tab isn't from whichever vault happens to be
mapped at that moment.

### Persistence migration — verified, low risk

`Services/SettingsService.cs` uses plain `System.Text.Json` deserialization
with no custom converters or `[JsonRequired]` attributes (`Load()`, lines
25-37). Adding a new `List<string> OpenVaultPaths` property to `AppSettings`
is automatically backward-compatible: old `settings.json` files simply don't
have the key and it defaults to an empty list via the property initializer.
Migration logic then only needs one line in `MainViewModel`'s ctor: if
`OpenVaultPaths` is empty and `LastVaultPath` is set, seed
`OpenVaultPaths = [LastVaultPath]` — this reproduces today's single-vault
startup behavior exactly as the N=1 case of the new model.

## What does NOT need to change

- `Models/OpenTab.cs` — no change.
- `ViewModels/EditorGroupViewModel.cs` tab/split/diff mechanics — no change
  (`OpenFileAsync`, `SwitchToTab`, `CloseTab`, `MoveTabToOtherGroup`,
  `CompareFiles` all already operate on bare paths / in-memory content).
- `FileService`'s self-write tracking dictionaries — already keyed by path.
- `FileTreeViewModel`'s search/filter (`ApplyFilter`) and `RevealFile` — already
  iterate `RootNodes` as a collection.
- `SettingsService`'s JSON load/save mechanics — no schema-version machinery
  needed, plain additive field is enough.

## Open risks

1. **Which root owns a newly-created file when nothing is selected in the
   tree?** `FileTreeViewModel.TargetDirectory()` currently falls back to the
   single `VaultRoot`. With N roots and no selection, "create file" is
   ambiguous — needs an explicit decision (e.g. disable create-with-no-target,
   default to the first/primary root, or require a root selection first).
2. **Image-paste target root.** `CopyImageToAssets`'s fallback-directory logic
   must resolve against the *owning root of the file being edited*, not a
   global root — must be threaded through carefully so a paste into vault B
   never writes into vault A's `assets/` folder.
3. **Watcher lifecycle for N roots.** Moving from one `FileSystemWatcher` to a
   dictionary of watchers needs explicit add/remove/dispose semantics when a
   vault is closed (removed from the open set) while files from it might
   still be open in tabs — decide whether closing a root force-closes its
   tabs (today's behavior) or leaves them open as "orphaned" read/write
   buffers with no live watcher.
4. **Preview virtual-host remap** (see finding above) — must move from a
   single global `VaultRoot` read to a per-active-tab owning-root resolution
   in `PushPreview`, or previews for non-"first" vaults will resolve relative
   images against the wrong folder.
5. **Persistence migration** — low risk (verified above), but the seed-once
   logic must run exactly once per settings-file upgrade, not every launch,
   to avoid re-adding a root the user deliberately closed.
6. **"Administrar vaults" UI semantics change** — `VaultsViewModel.Activate`
   (switch) → `ToggleOpen` (add/remove from open set) is a genuine UX change,
   not a rename: today exactly one row is bold/active and cannot be removed;
   the new form needs a way to show/toggle multiple simultaneously-open rows.
   Per the user's own "decisiones en lenguaje simple" preference, whoever
   proposes this to the user should describe the concrete before/after
   behavior (what you click, what happens to your open tabs) rather than
   "single-active vs. open-set" jargon.
7. **Graph-per-vault selector.** `MainViewModel.Graph` is currently one
   shared instance. Needs a decision on whether opening the graph view shows
   the graph for the focused group's owning vault (simplest, mirrors "one
   graph per vault, not merged") or exposes an explicit vault picker in the
   graph view itself.

## Ready for Proposal

**Yes.** The affected-file map above is complete and verified against the
actual source (not just the orchestrator's prior summary — one additional
load-bearing coupling was found and documented: the single-vault-root virtual
host mapping in the WebView2 preview pipeline, `MainWindow.xaml.cs`
`PushPreview` + `MarkdownService`). The seven open risks above should each get
an explicit call in `sdd-propose`/`sdd-design`, in particular #1 (new-file
target root), #3 (watcher/tab lifecycle on close), and #6 (Administrar vaults
UX) since those are user-facing behavior decisions, not just mechanical
refactors.
