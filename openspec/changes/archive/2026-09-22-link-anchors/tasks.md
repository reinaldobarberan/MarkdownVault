# Tasks: Link anchors (`[[Nota#Sección]]`, `[[Nota#^id]]`, `[[#intra]]`)

Tags: `[M]` mechanical · `[D#]` = design decision # · `[Q#]` = open question # · `[R#]` = spec requirement #, in spec.md order.

## Phase 1: Verification Spikes

- [x] 1.1 Confirm Markdig 1.1.2 paragraph `Closed` event (cf. `AutoIdentifierExtension.HeadingBlockParser_Closed`) for `BlockAnchorExtension` [D3]; fallback `Descendants<ParagraphBlock>()`. **Confirmed via reflection against the installed assembly: `BlockParser.Closed` exists (delegate `ProcessBlockDelegate(BlockProcessor, Block)`), inherited by `ParagraphBlockParser`; `builder.BlockParsers.Find<ParagraphBlockParser>()` returns the live instance. Fallback NOT needed.**
- [x] 1.2 Verify `AutoIdentifierOptions.GitHub` slugs on accented headings; confirm Phase 2 text-fallback covers mismatches. **Confirmed: `## Instalación` → `id="instalación"` (lowercased, accent preserved verbatim, not stripped/transliterated). Heading-text fallback in `AnchorLocator` kept anyway for id/text divergence cases (duplicate-heading suffixes, punctuation) — never exercised by the accent case itself, but still the right safety net.**
- [x] 1.3 Trace the raw-`.html` preview path; confirm full navigation before Phase 4 [D11]. **Confirmed via `EditorGroupViewModel.cs:690-694` + `MainWindow.xaml.cs:467-489`: `.html`/`.htm` sets `PreviewBodyHtml = ""`, so `canPatch` is always false and it always takes `NavigateToString` (full navigation). It also never gets `window.__mvScrollToId` injected (that script lives only in `MarkdownService.WrapInPage`, used for rendered Markdown; raw `.html` goes through `PrepareHtmlForPreview` instead, which only adds a `<base>` tag). Consistent with proposal decision #4 (anchors v1 scoped to `.md`/`.markdown`) — no extra work needed for this path.**

## Phase 2: Pure Core (Foundation)

- [x] 2.1 `Helpers/LinkTarget.cs`: `Parse(raw)` splits `|` then first unescaped `#`; `Kind = None|Heading|Block`; empty note = intra-doc [D1, R2].
- [x] 2.2 `Helpers/MarkerId.cs`: `Generate()` 6-char base-36; collision-regenerate vs `GetBlockMarkers` (5 tries, then 8 chars) [D8].
- [x] 2.3 `Services/BlockAnchorExtension.cs`: pure `BlockMarker.TryStrip` — `\^[A-Za-z0-9][A-Za-z0-9-]{5,63}` last token, whitespace-preceded, outside code/emphasis/links [R7].
- [x] 2.4 Wire parser hook (per 1.1); namespace id `"mv-b-" + rawId`; strip marker from rendered text only [D2, R8].
- [x] 2.5 `Helpers/AnchorLocator.cs`: `Find(doc, target) → int?` — heading by `Id` then text (ordinal-ignore-case); block by namespaced id [D6].
- [x] 2.6 Add pure `GetHeadings`/`GetBlockMarkers`/`ParseNote` to `MarkdownService.cs`; register `BlockAnchorExtension` at `:37`, before `MarkdownContributions` (`:38-42`) [D4, Q6].

## Phase 3: Junk-File Defect Closure

- [x] 3.1 `FileService.cs:456-483,490-494`: truncate at `LinkTarget.Parse(raw).Note` before `.md` append [D12, R3].
- [x] 3.2 `MarkdownService.cs:106` `ConvertWikiLinks`: build href via `LinkTarget`.
- [x] 3.3 `EditorView.xaml.cs:568-613`: drop blind `.md` append; route via `LinkTarget`; empty-note jumps intra-doc, no navigation.
- [x] 3.4 `MainWindow.xaml.cs:350-397`: same `LinkTarget` split for preview.

## Phase 4: Navigation Wiring (Editor + Preview)

- [x] 4.1 `EditorGroupViewModel.cs:297`: `NavigateToLinkAsync(path, anchor=null)`; one-shot pending anchor + `AnchorNavigated` event; update picker call site (`:809-830`). **Picker call site note**: `InsertInternalLink()` at those lines needed NO code change — `IDialogService.PickInternalLinkMarkdown`'s signature stayed identical; anchor-awareness lives entirely inside `LinkPickerDialog` (Phase 6). See apply-progress.md.
- [x] 4.2 `EditorView.xaml.cs:155-166`: consume pending anchor inside the `Loaded` continuation, after caret/scroll restore [D5]; make `SelectAndReveal` (`:361`) internal.
- [x] 4.3 `null` from `AnchorLocator.Find` → open note at top, report via `StatusSink` (`EditorGroupViewModel.cs:37`) [R6].
- [x] 4.4 `MarkdownService.cs:161`: `window.__mvScrollToId(id)` beside `__mvSetBody`; `false` when id absent [D10].
- [x] 4.5 `MainViewModel.cs:208`: wire `AnchorNavigated` + `ConsumePendingPreviewAnchor()`; call after `__mvSetBody` (patch, `:476-485`) and `NavigationCompleted` (`:347`, full route) [D9, Q7].
- [x] 4.6 Read `ExecuteScriptAsync` bool result; `false` → `StatusSink`; reconcile with 1.3 [R6, R4].
- [x] 4.7 (not separately numbered by sdd-tasks, implemented anyway — see apply-progress.md "Scope note") Intra-document anchor jump (spec "Intra-Document Anchor Navigation", R5): both click routes now resolve against the CURRENT buffer/page and scroll in place, no file operation.

## Phase 5: Marker Creation (Context Menu)

- [x] 5.1 `EditorView.xaml.cs:497-520`: spellcheck early-`return`s become skips; build one local `ContextMenu`; single exit point [D7]. Always append "Copiar enlace a este párrafo"; prepend suggestions + `Separator` only on misspelling; caret to clicked offset first.
- [x] 5.2 `FindBlockAtPosition` → nearest paragraph; reuse existing marker or `MarkerId.Generate()`; insert `" ^id"` at `block.Span.End` via `Document.Insert`; copy `[[Nota#^id]]` [R9]. **Corrected at apply time**: inserted at `block.Span.End + 1`, not literally `Span.End` — see apply-progress.md ("Span.End is inclusive").

## Phase 6: Link Picker — Anchor-Aware Second Step

- [x] 6.1 `IDialogService.cs:25`, `WpfDialogService.cs:57`, `LinkPickerDialog.xaml(.cs)`: after picking a note, list whole note/headings/marked paragraphs from the same owning root; never write a marker to an unfocused file [R9, R10].
- [x] 6.2 Zero-marker case: note + headings plus a plain-language hint on adding one — never hidden/empty.

## Phase 7: Performance Measurement (Q5)

- [x] 7.1 Temporary `Stopwatch` around `Markdig.Markdown.Parse`, p50 of 10 warm runs, on the largest dev-vault note and a synthetic ~500KB note; `Debug.WriteLine`. **Measured via a scratch harness (scratchpad only, never committed) that mirrors `MarkdownService.GetPipeline()` exactly (`UseAdvancedExtensions` + `UseAutoIdentifiers(GitHub)` + the real `BlockAnchorExtension`). Results (2 runs, Release build): largest real note (`docs/plugins/GUIA-PLUGINS.md`, 54078 bytes) p50 4.5-6.3ms; sample-vault note (462 bytes) p50 0.03ms; synthetic ~500KB/12973-line note p50 42.2-42.8ms.**
- [x] 7.2 Apply pre-decided threshold: <50ms ship as-is; 50-150ms add single-entry memo `(filePath, content-version)`; >150ms move parse to `Task.Run`, awaited before the `Loaded` reveal tick; remove scaffolding after. **All three measurements land under the 50ms line — shipped as-is, no memo, no `Task.Run`. No code change was this task's output.**

## Phase 8: Tests (xUnit, pure boundaries — LAST)

- [x] 8.1 `LinkTargetTests.cs`: note/heading/block/intra/alias/empty/edge cases. **15 facts.**
- [x] 8.2 `BlockMarkerTests.cs`: valid marker, rejected shapes (`x^2`, bold, code span). **11 facts — includes a REAL defect found and fixed (see apply-progress.md): the extension's marker recognition was silently inert against the actual pipeline.**
- [x] 8.3 `MarkerIdTests.cs`: shape + seeded collision regeneration. **4 facts, deterministic via a `QueueRandom` test double.**
- [x] 8.4 `AnchorLocatorTests.cs`: heading by id/text, block by id, miss → `null`; real `Markdig.Markdown.Parse`, no WPF. **10 facts — also covers `MarkdownService.GetHeadings`/`GetBlockMarkers`/`ParseNote` directly, per the orchestrator's explicit scope.**
- [x] 8.5 Manual: both click routes, both preview routes, context-menu vs. misspelling, zero-marker picker, marker inside fenced Mermaid. **NOT performed — genuinely requires the real `.exe` (WebView2, AvalonEdit caret/selection, ContextMenu). Flagged as outstanding in apply-progress.md and the final report; nothing here was faked.**

### Additional Phase 8 coverage beyond the four listed files (not separately task-numbered, added per the orchestrator's explicit scope)

- [x] The central "anchored link must never create a junk file" regression (spec R3), with a real temp-directory `FileService`, in `FileServiceTests.cs` (5 new facts: heading anchor found, block anchor found, anchor creates only the plain note, intra-doc anchor throws instead of creating a bogus file, legacy single-arg overload inherits the same guard).
- [x] `EditorGroupViewModel`'s new anchor-navigation surface (`NavigateToLinkAsync` with an anchor, `ConsumePendingAnchor`'s one-shot contract, `AnchorNavigated`, and the self-referencing-link guard from Phase 4's apply-progress.md) in `EditorGroupViewModelTests.cs` (5 new facts) — this is VM-level, not WPF, and was already covered headlessly by this same file's pre-existing conventions.
- [x] `InternalsVisibleTo("MarkdownVault.Tests")` — already present in `MarkdownVault.csproj` (line ~48, pre-existing for an unrelated test hook). Batches 1-2 flagged this as still needed for `AnchorLocator.Find`/`MainViewModel.ResolveAnchorDomId`; verified at Phase 8 that no csproj change was actually required.
