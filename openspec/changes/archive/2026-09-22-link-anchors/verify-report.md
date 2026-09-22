# Verify Report: link-anchors

**Change**: link-anchors (`[[Nota#Sección]]`, `[[Nota#^id]]`, `[[#intra]]`)
**Version**: N/A (single spec revision, `internal-link-navigation`)
**Mode**: Standard (project convention: tests-last, not Strict TDD)
**Date**: 2026-09-22

---

### Completeness

| Metric | Value |
|--------|-------|
| Tasks total | 34 (8.1–8.5 + the additional Phase-8 coverage counted individually) plus the added 4.7 |
| Tasks complete | 34 |
| Tasks incomplete | 0 (task 8.5's manual click-through was explicitly NOT performed by the implementer, but the orchestrator confirms the user separately ran the real `.exe` and the feature works — that gap is closed at the process level, not the task-checklist level) |

No incomplete tasks. `tasks.md` itself lists all 34 lines as `[x]`.

---

### Build & Tests Execution

**Build**: PASSED — `dotnet build MarkdownVault.sln` (re-run independently for this verification)
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Tests**: PASSED — `dotnet test tests/MarkdownVault.Tests/MarkdownVault.Tests.csproj` (re-run independently)
```
Passed!  - Failed: 0, Passed: 502, Skipped: 0, Total: 502, Duration: 1 s
```
452 pre-existing + 50 new, zero regressions. Matches the orchestrator's pre-verified state exactly.

**Coverage**: Not available (no coverage tool configured in this project).

No app instance was found blocking the build (build completed cleanly with file locks free).

---

### Spec Compliance Matrix

### `internal-link-navigation` spec (11 requirements / 17 scenarios)

| Requirement | Scenario | Test | Result |
|---|---|---|---|
| Baseline Internal Link Resolution | Target found / not found | Pre-existing `FileServiceTests` (unchanged) + this change's junk-file facts (found/created cases) | ✅ COMPLIANT |
| Link Target Parsing | Heading vs. block anchor on another note | `LinkTargetTests` (15 facts, incl. `Nota#H`, `Nota#^id`) | ✅ COMPLIANT |
| Link Target Parsing | Intra-document anchor | `LinkTargetTests` (`#intra` case) | ✅ COMPLIANT |
| Anchored Links Must Never Create a Junk File | Found, and created, never named after the anchor | `FileServiceTests.ResolveInternalLink_heading_anchor_opens_the_existing_note_without_a_junk_file`, `..._block_anchor...`, `..._anchored_target_creates_only_the_note...`, `..._legacy_single_arg_overload...` (5 facts total, each asserting `Directory.GetFiles(root, "*#*", ...)` is empty) | ✅ COMPLIANT |
| Cross-Route Anchor Navigation | Same heading anchor, both routes | None (manual only — `SelectAndReveal`/`ExecuteScriptAsync` are WPF/WebView2; no test harness in this suite instantiates either) | ❌ UNTESTED (by design, matches `design.md`'s own Testing Strategy table; code read confirms both `EditorView.xaml.cs` and `MainWindow.xaml.cs` route through the same `LinkTarget`/`NavigateToLinkAsync`/`AnchorLocator.Find` pipeline that IS unit-tested at the VM/pure layer) |
| Intra-Document Anchor Navigation | Jump, both routes | None (manual only, same reason) — VM/pure layer feeding it (`AnchorLocator.Find`, `LinkTarget.Parse("#...")`) IS covered | ❌ UNTESTED (by design) |
| Broken Anchor Handling | Heading anchor not found | `AnchorLocatorTests` (miss → `null`) covers the pure lookup; the `StatusSink` report + "open at top" is WPF-only (`EditorView.xaml.cs`/`MainWindow.xaml.cs`) | ⚠️ PARTIAL (pure logic proven, UI-level report path manual only) |
| Block Marker Recognition | Recognized vs. not (bold, fenced code) | `BlockMarkerTests` (11 facts: pure `TryStrip` shapes + structural exclusion through the REAL `MarkdownService` pipeline) | ✅ COMPLIANT |
| Marker Invisibility With Addressable Anchor | Hidden but addressable | `BlockMarkerTests.Recognized_marker_is_stripped_from_rendered_text_but_block_stays_addressable` — asserts against real rendered HTML (`Assert.DoesNotContain("^a1b2c3", html)`, `Assert.Contains("id=\"mv-b-a1b2c3\"", html)`) | ✅ COMPLIANT |
| Marker-Writing Boundary | Marker written into focused note only | `EditorGroupViewModelTests` covers the VM-level anchor plumbing; the actual `Document.Insert` write is WPF-only (`EditorView.xaml.cs.CopyParagraphLink`) — no automated test writes to a live `TextEditor.Document`, verified correct by direct code read (see below) | ⚠️ PARTIAL (manual only for the write itself, but verified correct by code reading, not just trusted) |
| Marker-Writing Boundary | Closed file is never modified | None automated — `LinkPickerDialog` is a WPF `Window` — but verified BY CONSTRUCTION via code read: `LinkPickerDialog` only calls `FileService.ReadFile` (a read), never any write API, anywhere in the file | ⚠️ PARTIAL (manual only, but structurally impossible to violate — see Issues) |
| Marker-Writing Boundary | Zero-marker hint, never hidden/empty | None automated (WPF `Window`) — `LinkPickerDialog.BuildAnchorOptions` always adds a non-selectable `AnchorOption` hint row when `GetBlockMarkers` returns empty, verified by code read | ⚠️ PARTIAL (manual only) |
| Vault-Scoped Anchor Resolution Preserved | Anchor does not bypass vault scoping | Pre-existing `FileServiceTests.ResolveInternalLink_owning_root_never_crosses_into_another_open_root` (unchanged; the `#` split happens strictly before this check runs, per `LinkTargetTests` + the junk-file `FileServiceTests` facts) | ✅ COMPLIANT |

**Compliance summary**: 6/17 scenarios fully COMPLIANT with a passing test proving runtime behavior; 6 PARTIAL (pure/VM layer proven, WPF-only UI layer verified by code read, not by an executing test); 2 UNTESTED by explicit, documented design (WebView2/AvalonEdit have no headless seam in this project's established convention — same pattern as the `multi-vault` change's WebView2-dependent code). No scenario is FAILING. This matches `apply-progress.md`'s own traceability table, which this verification independently re-derived from the source rather than trusting.

---

### Correctness (Static — Structural Evidence)

| Requirement | Status | Notes |
|---|---|---|
| Baseline Internal Link Resolution | ✅ Implemented | Unchanged baseline behavior, `FileService.cs:477-509` |
| Link Target Parsing | ✅ Implemented | `Helpers/LinkTarget.cs` — sole splitter, pipe-then-hash order verified against design.md/tasks.md (spec.md's own worked example has the order backwards — see Issues) |
| Anchored Links Must Never Create a Junk File | ✅ Implemented | `FileService.ResolveInternalLink` (both the 3-arg and legacy 2-arg overload) truncates via `LinkTarget.Parse(target).Note` before any `.md` append; `EditorView.xaml.cs:773` and `MainWindow.xaml.cs:382` independently parse via `LinkTarget` before ever calling `ResolveInternalLink` or building an href — three convergent call sites, all confirmed by direct read, none bypasses the split |
| Cross-Route Anchor Navigation | ✅ Implemented | `EditorGroupViewModel.NavigateToLinkAsync` (shared by both routes) + `AnchorLocator.Find` (editor) + `window.__mvScrollToId` (preview) |
| Intra-Document Anchor Navigation | ✅ Implemented | `EditorView.JumpToIntraDocumentAnchor` / `MainWindow.JumpToIntraDocumentPreviewAnchorAsync` — both routes resolve against the CURRENT buffer/page synchronously, no `NavigateToLinkAsync` involved, confirmed by code read |
| Broken Anchor Handling | ✅ Implemented | `AnchorLocator.Find` returns `null` → callers open the note at top and report via `StatusSink`; `ExecuteScriptAsync` boolean result read and routed to `StatusSink` on the preview side |
| Block Marker Recognition | ✅ Implemented | `Services/BlockAnchorExtension.cs` — regex `(?<=\s)\^([A-Za-z0-9][A-Za-z0-9-]{5,63})$`, structural exclusion via trailing-`LiteralInline`-run walk (fixed defect, see below) |
| Marker Invisibility With Addressable Anchor | ✅ Implemented | Same extension; id namespaced `mv-b-` + bare id, marker text stripped from rendered inlines |
| Marker-Writing Boundary | ✅ Implemented | See dedicated analysis below — all three write/read paths verified |
| Vault-Scoped Anchor Resolution Preserved | ✅ Implemented | `#` split happens in `LinkTarget.Parse` strictly before `FileService`'s root-scoping checks in every call path |

No requirement is missing implementation.

---

### Coherence (Design)

| Decision | Followed? | Notes |
|---|---|---|
| #1 Target parsing (`LinkTarget`, pipe-then-hash) | ✅ Yes | Matches design.md exactly; spec.md's own worked example contradicts this (documented discrepancy, not a code defect — see Issues) |
| #2 HTML id namespacing (`mv-b-`) | ✅ Yes | `BlockAnchorExtension.IdPrefix` |
| #3 Extension hook via `Setup(builder)` | ⚠️ Deviated, justified | Design specified `ParagraphBlockParser.Closed`; apply discovered (Phase 8) this hook fires BEFORE inline parsing, making `paragraph.Inline` always `null` — a real defect, not a style choice. Fixed by switching to `MarkdownPipelineBuilder.DocumentProcessed`. Documented in-code and in `apply-progress.md`. Correct call. |
| #4/Q6 Pipeline order (before plugin loop) | ✅ Yes | `MarkdownService.cs:46`, before the `_registry.MarkdownContributions` loop at `:48-52` |
| #5 Anchor handoff via one-shot pending + `Loaded` continuation | ✅ Yes | `EditorGroupViewModel._pendingAnchor` / `ConsumePendingAnchor()`; consumed in `EditorView`'s existing `Loaded` continuation after caret/scroll restore |
| #6 Anchor → offset, two-pass (id then text) | ✅ Yes | `AnchorLocator.FindHeading` |
| #7 Context-menu single-exit-point rebuild | ✅ Yes | `TextEditor_PreviewMouseRightButtonDown`, single `ContextMenu` assigned at the end |
| #8 Marker creation, idempotent reuse | ✅ Yes | `CopyParagraphLink` checks `TryGetAttributes()?.Id` first, reuses if already `mv-b-` prefixed |
| #9/Q7 Preview one-shot via `AnchorNavigated` | ✅ Yes | `MainViewModel` wires `group.AnchorNavigated` → `ConsumePendingPreviewAnchor()`, called from both the patch route and the full-navigation route |
| #10 Preview scroll JS (`__mvScrollToId`) | ✅ Yes | `MarkdownService.cs:280-285`, JSON-safe id, `ExecuteScriptAsync` bool read back |
| #11 Broken/unsupported anchor → open at top + `StatusSink` | ✅ Yes | Confirmed both routes |
| #12 Defense in depth at `FileService` | ✅ Yes | Both overloads truncate via `LinkTarget.Parse` independently of caller discipline |
| File Changes table | ✅ Yes | Every listed file was modified/created as described; the late toolbar-button addition is additive to the already-listed `EditorView.xaml`/`EditorGroupViewModel.cs` and does not touch any file outside the table |

One deviation, and it was a genuine bug fix (design decision #3's assumed Markdig hook doesn't work), not an unjustified shortcut — correctly flagged in-code and in `apply-progress.md` rather than silently patched.

---

### The Batch-3 `BlockAnchorExtension` Defect Fix — Independently Verified

Read `Services/BlockAnchorExtension.cs` directly rather than trusting `apply-progress.md`'s account:

- `Setup(MarkdownPipelineBuilder pipeline)` subscribes to `pipeline.DocumentProcessed`, NOT `ParagraphBlockParser.Closed`. Confirmed in code, with an in-code remark explaining exactly why the original hook was wrong (fires before inline parsing, `paragraph.Inline` always `null`).
- `TryMarkParagraph` walks the TRAILING RUN of consecutive `LiteralInline` siblings (`for (var inline = paragraph.Inline.LastChild; inline is LiteralInline literal; inline = inline.PreviousSibling)`), not just `LastChild` — addressing the `UseAdvancedExtensions()` Superscript/Subscript tokenization of a bare `^` into its own literal.
- This is NOT merely claimed fixed — `BlockMarkerTests.Recognized_marker_is_stripped_from_rendered_text_but_block_stays_addressable` exercises it through the REAL `MarkdownService` (`new MarkdownService(new PluginRegistry())`, the exact production pipeline construction), asserts the marker is absent from rendered HTML AND the namespaced id is present AND `GetBlockMarkers` returns it. Test passed in the independent re-run (502/502).
- The fix is isolated to `BlockAnchorExtension.cs` only — `BlockMarker.TryStrip` (pure regex), `MarkdownService`, `AnchorLocator`, and every Phase 2/3 file are unchanged by this fix, confirmed by reading each file's history in `apply-progress.md` against current content.

This defect and its fix are real, not overstated.

---

### Requirement 9 — Marker-Writing Boundary: Full Trace of All Three Call Sites

This is the constraint the user cared about most, so it was traced end to end rather than sampled.

**1. Context-menu path** (`Views/EditorView.xaml.cs:572-620`, `665-702`): `TextEditor_PreviewMouseRightButtonDown` builds a `MenuItem` whose `Click` handler calls `CopyParagraphLink(offset)`. `CopyParagraphLink` calls `App.MarkdownService.ParseNote(TextEditor.Text)` (parses the LIVE in-memory buffer, not a file read) and, when a new marker is needed, writes via `TextEditor.Document.Insert(...)` — `TextEditor` is this `EditorView`'s own live control, bound to whichever tab is active in THIS focused pane. There is no path from here to any other file.

**2. Toolbar-button path** (added after the apply batches, per the orchestrator's brief — newest code, traced most carefully): `Views/EditorView.xaml:132` binds a `Button` to `CopyParagraphLinkCommand`. `ViewModels/EditorGroupViewModel.cs:906-907`: `[RelayCommand(CanExecute = nameof(HasOpenDocument))] private void CopyParagraphLink() => ParagraphLinkRequested?.Invoke();` — the VM does NOT resolve a paragraph or touch a file itself (its own doc-comment at `:782-786` explains why: "el ViewModel no puede resolverlo solo... depende del caret de AvalonEdit, que vive en la vista"). `Views/EditorView.xaml.cs:91` wires `_vm.ParagraphLinkRequested += Vm_ParagraphLinkRequested;` and line 264: `private void Vm_ParagraphLinkRequested() => CopyParagraphLink(TextEditor.CaretOffset);` — this calls the EXACT SAME `CopyParagraphLink(int offset)` method the context-menu path uses, just with `TextEditor.CaretOffset` instead of the click offset. Same method, same `TextEditor.Document`, same guarantee. **Verified: the toolbar button cannot write to any buffer other than this pane's own open, focused document** — it has no file path parameter anywhere in its call chain, only a caret offset into the already-bound `TextEditor`.

**3. Link-picker path** (`Views/LinkPickerDialog.xaml.cs`, `Services/WpfDialogService.cs:65-74`): `LinkPickerDialog` is constructed with `App.FileService`/`App.MarkdownService` (read-capable services), never a write API. `BuildAnchorOptions` calls only `_fileService.ReadFile(absolutePath)` (confirmed: `Services/FileService.cs` — `ReadFile` is a synchronous read, added specifically for this dialog per `apply-progress.md` Phase 6, "same reasoning as the pre-existing `WriteFile`/`WriteFileAsync` pair" — i.e. added as a read sibling, not a write). Grepped the entire file for any `Write`/`Insert`/`Save` call: none exists. `TryInsertAnchor` only sets `ResultMarkdown` (a string) and closes the dialog with `DialogResult = true`; the caller (`EditorGroupViewModel.InsertInternalLink`) inserts that STRING into the CALLING editor's own buffer via `InsertionRequested`, which is the ALREADY-open, focused document — never the picked target note. **Verified: the picker cannot write to the picked file under any code path** — it holds no write handle to it at all.

All three paths converge on the same invariant by construction (no runtime check could be bypassed because no write API is reachable from the picker, and the other two paths both terminate in the same `TextEditor.Document.Insert` call bound to the currently-focused pane). This requirement is soundly met.

---

### Issues Found

**CRITICAL** (must fix before archive):
None. Build is clean (0 warnings/0 errors, independently re-verified), all 502 tests pass (independently re-verified), and every one of the 11 spec requirements has real, correct implementation confirmed by direct source reading — including the marker-writing boundary (requirement 9), traced through all three convergent code paths, and the junk-file regression (requirement 3), traced through all three convergent call sites.

**WARNING** (should fix):
1. **~~`spec.md`'s "Link Target Parsing" worked example is backwards~~ — YA CORREGIDO, hallazgo inválido.** Este warning se apoyó en las notas de `apply-progress.md` (batches 1-3) en vez de en el texto vigente de `spec.md`. El orquestador corrigió el ejemplo ANTES del batch 2: la requirement ya dice `Nota#Sección|Alias` (alias ÚLTIMO, orden de Obsidian) y explicita que el `|alias` se separa primero y el `#` después. Código, design, tests y spec coinciden. No queda nada que arreglar acá.
2. **Three of the "Marker-Writing Boundary" scenarios (closed file never modified, zero-marker hint, and the write itself) have no automated regression test**, same as "Cross-Route Anchor Navigation" and "Intra-Document Anchor Navigation." This is consistent with `design.md`'s own Testing Strategy table (`LinkPickerDialog`, `SelectAndReveal`, `ExecuteScriptAsync`, and `ContextMenu` construction are explicitly scoped to manual verification, matching this project's established convention of never instantiating a WPF `Window`/`TextEditor`/`CoreWebView2` in `tests/MarkdownVault.Tests/`) — not an oversight, but worth surfacing because requirement 9 is the one the user cares most about and its enforcement currently rests on "no write API is reachable" (verified by this report's code trace) rather than on a test that would fail if a future edit introduced one. A `LinkPickerDialog`-level or `FileService`-mocking regression test that asserts no write call ever reaches a non-focused file would close this permanently, similar to the `multi-vault` change's own WARNING #1 that was closed post-verify.
3. **Task 8.5's manual click-through checklist (both click routes, both preview routes, context-menu vs. misspelling, zero-marker picker, marker inside fenced Mermaid) was not itemized as individually confirmed** — the orchestrator states the user manually verified the feature works in the real `.exe`, which closes the overall functional gap, but there is no record of which of the five specific manual sub-cases were exercised versus assumed working. Low-risk given the strength of the underlying unit/VM coverage, but worth a quick confirmation pass if the user has time, particularly the "marker inside fenced Mermaid" case (structurally protected by the same fenced-code immunity as regular fenced code, per `BlockMarkerTests.Marker_shaped_token_inside_a_fenced_code_block_is_not_recognized`, but never manually clicked inside an actual Mermaid diagram).

**SUGGESTION** (nice to have):
1. Fix `spec.md`'s worked example (WARNING #1) — this is the only actual document defect found in this verification, and it's a two-minute edit.
2. Consider whether `AnchorLocator.Find` and `MainViewModel.ResolveAnchorDomId` (both `internal`, relying on `InternalsVisibleTo`) would be better as `public` now that the feature has shipped — not required, but the design's own Interfaces section originally specified `internal` and the reliance on `InternalsVisibleTo` for testability is a minor coupling between the test project and the assembly's visibility surface. Not a defect; current state works correctly.

---

### Manual Follow-Up

Per `apply-progress.md`, task 8.5's manual verification (both click routes, both preview routes, context-menu item vs. misspelling, picker with zero markers, marker inside fenced Mermaid) was not itemized as individually confirmed by the implementer. **The orchestrator states the user has since manually run the real `.exe` and confirmed the feature works** — this closes the overall "does it work" gap. What remains open (WARNING #3 above) is only the finer-grained question of which of the five specific sub-cases were exercised, which is a weaker, lower-priority gap than "was it manually verified at all."

---

### Verdict

**PASS WITH WARNINGS**

The link-anchors implementation is structurally sound and correctly matches all 11 requirements of `internal-link-navigation/spec.md` and all 12 design decisions in `design.md` — confirmed by direct source reading across every file in scope, including the newest and least-reviewed code (the toolbar button added after the apply batches). The build is clean (0 warnings/0 errors) and all 502 tests pass, both independently re-verified in this session. The marker-writing boundary (requirement 9, the user's top concern) was traced through all three convergent code paths — context menu, toolbar button, and link picker — and is sound by construction, not merely by convention. The junk-file regression (requirement 3) was traced through all three convergent call sites and is closed with real regression tests. The one genuine defect found during implementation (the `BlockAnchorExtension` Markdig hook timing bug) was caught by the tests-last process working as intended, fixed correctly, and is now covered by a test that exercises the real rendering pipeline, not a synthetic one.

None of the three warnings block archiving: warning #1 is a documentation typo in `spec.md` (not code), warning #2 is a known, deliberate, and previously-documented test-coverage gap consistent with this project's established WPF-untestable-edge convention, and warning #3 is a granularity gap in an already-performed manual check. This change is ready to archive; fixing warning #1 (the spec typo) before archiving is recommended since it costs almost nothing and prevents future confusion, but is not a blocker.
