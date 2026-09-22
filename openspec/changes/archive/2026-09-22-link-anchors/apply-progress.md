# Apply Progress: link-anchors

## Batch 1 (this batch) — Phases 1, 2, 3

**Mode**: Standard (Strict TDD explicitly disabled for this batch per orchestrator brief —
locked decision #5, "tests last", Phase 8 only). No tests written.

**Build**: `dotnet build MarkdownVault.sln` (whole solution, not just the host csproj — plugins
only get copied into `bin/<Config>/net8.0-windows/Plugins/<Name>/` as part of a solution build
per `AGENTS.md`). **Result: succeeded, 0 warnings, 0 errors.** This was run only after the
orchestrator explicitly lifted the "never build" rule mid-batch; `dotnet run`/`dotnet
MarkdownVault.dll` was NOT run (forbidden regardless — WebView2 fails silently under
`dotnet.exe`, AGENTS.md gotcha).

---

## Phase 1 — Verification spikes (all three settled)

Could not build to discover these (build was authorized only later in the batch), so each was
confirmed by reflecting directly against the installed `Markdig 1.1.2` assembly
(`~/.nuget/packages/markdig/1.1.2/lib/net8.0/Markdig.dll`) via a disposable scratch console app
in the session scratchpad (not part of the repo, deleted from consideration — nothing committed
to the project from it).

### 1.1 — Paragraph-parser `Closed` event: CONFIRMED, exactly as designed
`Markdig.Parsers.BlockParser` declares a public event `Closed` of delegate type
`ProcessBlockDelegate(BlockProcessor processor, Block block)`. `ParagraphBlockParser` inherits
it (does not redeclare). `MarkdownPipelineBuilder.BlockParsers.Find<ParagraphBlockParser>()`
returns the live instance from a freshly-built pipeline. **The `Descendants<ParagraphBlock>()`
fallback specified in the design was NOT needed** — implemented `BlockAnchorExtension.Setup`
using the `Closed` event directly, mirroring `AutoIdentifierExtension`'s own pattern.

### 1.2 — `AutoIdentifierOptions.GitHub` on accented headings: CONFIRMED, no transliteration
Parsing `## Instalación` with `UseAutoIdentifiers(AutoIdentifierOptions.GitHub)` produces
`id="instalación"` — lowercased, the accented character preserved verbatim (not stripped to
ASCII, not percent-encoded at this stage). The design's heading-text fallback in `AnchorLocator`
(match by `Id` first, then by plain heading text, ordinal-ignore-case) is kept as specified —
it isn't exercised by the accent case itself (id and text-lowercased already coincide for this
example), but it remains the correct safety net for genuine id/text divergence (duplicate
headings get a `-1`/`-2` suffix from Markdig; a heading containing inline formatting or extra
punctuation can diverge from a hand-typed anchor). No design change.

### 1.3 — Raw-`.html` preview path: CONFIRMED, full navigation, decision #11 unaffected
Traced `EditorGroupViewModel.cs:690-694` (sets `PreviewBodyHtml = string.Empty` and
`PreviewHtml = _markdownService.PrepareHtmlForPreview(Content, vaultRoot)` for `.html`/`.htm`)
into `MainWindow.xaml.cs`'s `PushPreview` (`:465-489`): `canPatch` requires a non-empty
`bodyHtml`, so a raw-`.html` file ALWAYS takes the `NavigateToString` full-navigation branch,
never the DOM-patch branch. Additional finding beyond what was asked: raw `.html` never gets
`window.__mvScrollToId` injected at all, because that script lives only in
`MarkdownService.WrapInPage` (the Markdown-render page shell) — `PrepareHtmlForPreview` only
inserts a `<base>` tag into the user's own HTML. This is consistent with, and further supports,
proposal decision #4 (anchors scoped to `.md`/`.markdown` in v1; a `.html` target takes the
broken-anchor path). No design change needed; noted for Phase 4 so nobody wires
`__mvScrollToId` expecting it to exist on the raw-HTML page.

---

## Phase 2 — Pure core

| File | Action | What was done |
|------|--------|----------------|
| `Helpers/LinkTarget.cs` | Created | `AnchorKind` enum + `readonly record struct LinkTarget(Note, Alias, Anchor, Kind)` with `Parse(raw)`. Split order: first unescaped `\|` across the WHOLE raw string (alias off the end), THEN first unescaped `#` within what's left (note/anchor). See "Spec discrepancy" note below — this order matches `design.md` and `tasks.md`, not the literal example string in `spec.md`. |
| `Helpers/MarkerId.cs` | Created | `Generate(existingIds, random = null)`: 6-char lowercase base-36, 5 collision retries, then 8 chars. `random` param added (not in the design's Interfaces list) purely to make Phase 8's "seeded" test requirement possible without touching this file again — production path uses a shared `Random` by default. |
| `Services/BlockAnchorExtension.cs` | Created | Two types in one file, per the design's File Changes table: `BlockMarker` (pure `TryStrip(string) → (Text, Id?)`, regex `(?<=\s)\^([A-Za-z0-9][A-Za-z0-9-]{5,63})$`) and `BlockAnchorExtension : IMarkdownExtension` (hooks `ParagraphBlockParser.Closed`, checks ONLY `paragraph.Inline.LastChild is LiteralInline` — structural exclusion of emphasis/link/code-span wrapping, no extra checks needed — strips the marker and sets `paragraph.GetAttributes().Id = "mv-b-" + id`). |
| `Helpers/AnchorLocator.cs` | Created | `internal static int? Find(MarkdownDocument, LinkTarget)`, matching the design's Interfaces section (kept `internal` as specified — see Phase 8 flag below). Two-pass heading lookup (id exact, then text ordinal-ignore-case), block lookup via namespaced id. |
| `Services/MarkdownService.cs` | Modified | Registered `new BlockAnchorExtension()` into `builder.Extensions` immediately after `UseAutoIdentifiers`, BEFORE the `_registry.MarkdownContributions` loop (Q6). Added `ParseNote(markdown)`, `GetHeadings(markdown)`, `GetBlockMarkers(markdown)`, private `PlainText(ContainerInline?)` helper. |

**Deviation from the Interfaces section, flagged, not silent**: `AnchorLocator.Find` is
`internal` per the design. `MarkdownVault.Tests` is a separate assembly — Phase 8 will need
either `[assembly: InternalsVisibleTo("MarkdownVault.Tests")]` added to `MarkdownVault.csproj`/
`AssemblyInfo`, or `Find` promoted to `public`. Not changed here since Phase 8 (tests) is out of
this batch's scope and this is a one-line addition when that phase starts.

**Spec discrepancy found, not resolved (flagging per instructions, not improvising a fix)**:
`specs/internal-link-navigation/spec.md`'s "Link Target Parsing" requirement gives the worked
example `Nota|Alias#Sección → path Nota, anchor Sección, alias Alias` — alias BEFORE the anchor
in the raw string. But `design.md` decision #1 ("split `\|` first, then the first `#` of the
note half") and `tasks.md` 2.1 ("splits `|` then first unescaped `#`") both describe — and the
design's own test corpus for `LinkTarget.Parse` explicitly includes `Nota#H|Alias` and "`#`
inside an alias" as distinct cases — the OPPOSITE, and Obsidian-syntax-correct, order:
`Note#Heading|Alias` (anchor before alias, the real `[[Note#Heading|Display]]` Obsidian
syntax). Applying spec.md's literal split order to its own example breaks the "`#` inside an
alias" case the design explicitly wants supported. **Implemented per design.md + tasks.md**
(both agree with each other and with actual Obsidian syntax); `spec.md`'s illustrative string
looks like a documentation typo (alias/anchor swapped), not a real behavioral requirement.
Verified against `LinkTarget.Parse`:
- `"Nota#H"` → Note=`Nota`, Anchor=`H`, Kind=Heading
- `"Nota#^id"` → Note=`Nota`, Anchor=`id`, Kind=Block
- `"#intra"` → Note=``, Anchor=`intra`, Kind=Heading (intra-document)
- `"Nota#H|Alias"` → Note=`Nota`, Anchor=`H`, Alias=`Alias`
- `"Nota|Foo#Bar"` → Note=`Nota`, Alias=`Foo#Bar` (the `#` inside the alias is NOT re-split)

This does not block anything in this batch (Phase 2/3 code is internally consistent with
itself), but `sdd-spec`/whoever owns `spec.md` should fix the worked example before Phase 8
tests get written against it literally.

---

## Phase 3 — Junk-file defect closure

| File | Action | What was done |
|------|--------|----------------|
| `Services/FileService.cs` | Modified | `ResolveInternalLink(string? root, string target, string currentFilePath)`: `target` now runs through `LinkTarget.Parse(target).Note` before the `.md`-append/resolution logic (defense in depth, decision #12 — covers a call site that forgets to split, degrading to "anchor ignored" rather than a junk file). Added a guard: if the resulting note half is empty (a bare intra-document anchor reaching this method, which callers should never do), it throws `InvalidOperationException` rather than resolving/creating a bogus path built from an empty name. The legacy single-arg overload (`:507-511`) delegates to this one, so it inherits the fix without its own change — doc comment updated to say so. |
| `Services/MarkdownService.cs` | Modified | `ConvertWikiLinks`: now calls `LinkTarget.Parse(target)` and rebuilds the href via a new `BuildWikiLinkHref` helper — `.md` appended only to a non-empty note half, anchor reattached as `#anchor` (or `#^id` for a block). Fixes Defect 1 from the proposal: `[[#Conclusiones]]` now renders `href="#Conclusiones"` instead of `href="#Conclusiones.md"`. Default display text (no explicit `\|alias`) now falls back to the note's filename-without-extension, or the anchor text itself for an intra-document target — previously it computed `Path.GetFileNameWithoutExtension` on the UNSPLIT target, which for `"Nota#Sección"` produced the display text `"Nota#Sección"` verbatim (extension-less string, so `GetFileNameWithoutExtension` was a no-op) instead of just `"Nota"`. |
| `Views/EditorView.xaml.cs` | Modified | `TextEditor_PreviewMouseLeftButtonDown`: removed the blind `if (!Path.HasExtension(target)) target += ".md"` step for wikilink targets (FileService now owns that, once, for every caller). Parses via `LinkTarget`, checks the image-extension filter against `parsedTarget.Note` (not the raw, possibly-anchored string), and — **new** — returns without navigating at all when the note half is empty (intra-document anchor). Still resolves/navigates on the note half only when non-empty. |
| `Views/MainWindow.xaml.cs` | Modified | `NavigationStarting` handler: added `using MarkdownVault.Helpers;`. After unescaping `relativePath`, parses it via `LinkTarget` before the image-extension check (checked against `parsedTarget.Note`) and before calling `ResolveInternalLink`/`NavigateToLinkAsync` — passes `parsedTarget.Note` instead of the raw anchored string, and returns early (no navigation) when the note half is empty. |

**Scope note, not a deviation**: per the batch brief, Phase 3 closes the junk-file defect only.
Neither click handler actually JUMPS to the resolved anchor position yet for a valid
`Nota#anchor` target that isn't intra-document — `NavigateToLinkAsync` still takes a single
`string` (Phase 4, task 4.1, adds the `anchor` parameter and the one-shot pending-anchor
consumption). This batch's editor/preview routes now: (a) never create a junk file for any
anchored target, heading or block, on either route; (b) never navigate anywhere for an
intra-document target; (c) still open the right note from the top for a non-intra-document
anchored target, exactly like today's anchor-less baseline, pending Phase 4's scroll-to-anchor
wiring. This matches the "Junk-File Defect Closure" phase title and does not pre-empt Phase 4's
design decisions (one-shot anchor, `Loaded`-tick consumption, etc.).

---

## Discoveries saved to engram (not the OpenSpec artifacts themselves — those stay file-only
per `artifact_store.mode = openspec`)

1. Markdig 1.1.2 `BlockParser.Closed` event confirmed to exist with the expected shape —
   reusable knowledge for any future host-owned Markdig extension in this codebase.
2. `AutoIdentifierOptions.GitHub` does not transliterate/strip accents — reusable knowledge for
   anything else that needs to predict a GitHub-style heading slug in this codebase.
3. The `spec.md` alias/anchor ordering discrepancy (see above) — so Phase 8 test-writing doesn't
   copy the wrong worked example verbatim.

---

## Batch 2 (this batch) — Phases 4, 5, 6

**Mode**: Standard (Strict TDD explicitly disabled for this batch per orchestrator brief —
locked decision #5, "tests last", Phase 8 only). No tests written.

**Build**: `dotnet build MarkdownVault.sln` (whole solution). **Result: succeeded, 0 warnings, 0
errors.** Also ran the existing suite as a regression check (not new test-writing, which stays
out of scope): `dotnet test tests/MarkdownVault.Tests/MarkdownVault.Tests.csproj` — **452/452
passed, 0 failed.** `dotnet run`/`dotnet MarkdownVault.dll` was NOT run (forbidden regardless —
WebView2 fails silently under `dotnet.exe`, AGENTS.md gotcha #1); manual verification (spec's
"Manual" row — both click routes, both preview routes, context-menu, zero-marker picker) was
NOT performed and remains outstanding.

---

## Phase 4 — Navigation Wiring (Editor + Preview)

| File | Action | What was done |
|------|--------|----------------|
| `ViewModels/EditorGroupViewModel.cs` | Modified | `NavigateToLinkAsync(string resolvedPath, string? anchor = null)` — default parameter keeps every pre-existing call site compiling. `anchor` is the raw text after `#`, sigil kept for a block (`"^a1b2c3"`), exactly as a caller's own `LinkTarget.Kind`/`Anchor` would encode it — round-tripped back into a `LinkTarget` via `LinkTarget.Parse("#" + anchor)` instead of adding a second `Kind` parameter (single source of truth, design decision #1). Added the one-shot `_pendingAnchor` field, `ConsumePendingAnchor()`, and the `AnchorNavigated` event (fired only when a target actually ended up on THIS group's active tab — see the two guards below). |
| `Views/EditorView.xaml.cs` | Modified | `OnActiveTabChanged`'s existing `Loaded` continuation now calls `ConsumePendingAnchorIfAny()` AFTER the caret/scroll restore. Added `ConsumePendingAnchorIfAny` (cross-note), `JumpToIntraDocumentAnchor` (intra-doc, synchronous, see "Scope note" below), and `AnchorDisplay`. `SelectAndReveal` changed from `private` to `internal` per design decision #6 (no other class ended up needing it this batch, but the design explicitly calls for it and it costs nothing). `TextEditor_PreviewMouseLeftButtonDown` now threads `parsedTarget.Anchor`/`Kind` through to `NavigateToLinkAsync` instead of discarding them. |
| `Services/MarkdownService.cs` | Modified | Added `window.__mvScrollToId(id)` beside `window.__mvSetBody` in `WrapInPage`'s script block — `document.getElementById(id)` + `scrollIntoView({block:'start'})`, returns `false` when absent. |
| `ViewModels/MainViewModel.cs` | Modified | Wired `group.AnchorNavigated` in `CreateGroup()` to eagerly resolve and stash a DOM id (`_pendingPreviewAnchorId`) via a new `ResolveAnchorDomId(markdown, target)` — safe to resolve eagerly because `group.Content` is already the destination note's text by the time `AnchorNavigated` fires (`OnActiveTabChanged` sets `Content`/calls `RefreshPreview()` synchronously before `NavigateToLinkAsync`'s `await OpenFileAsync` returns). Added `ConsumePendingPreviewAnchor()` (one-shot) and `DiscardPendingPreviewAnchor()` (for the blank-preview edge case). `ResolveAnchorDomId` made `internal` so `MainWindow` can reuse it directly for the intra-document case, which never goes through `AnchorNavigated` at all. |
| `Views/MainWindow.xaml.cs` | Modified | `NavigationCompleted` now also calls `ApplyPendingPreviewAnchorAsync()` (full-navigation route); the `canPatch` branch calls it right after `__mvSetBody` (patch route); the blank-preview branch calls `DiscardPendingPreviewAnchor()` instead. Added `ApplyPendingPreviewAnchorAsync`, `ScrollPreviewToDomIdAsync` (reads the `ExecuteScriptAsync` boolean, reports a miss via `_previewSource?.StatusSink`), and `JumpToIntraDocumentPreviewAnchorAsync`. `NavigationStarting`'s vault.local branch now threads the anchor through to `NavigateToLinkAsync`, mirroring `EditorView`. |

**Deviation, not silent — `ResolveAnchorDomId` isn't in design.md's Interfaces list**: decision
#10 ("scroll the preview to an id") needs SOME way to turn a `LinkTarget` into the DOM id
`window.__mvScrollToId` expects, and `AnchorLocator.Find` (the only listed lookup) resolves to a
**source-text offset** using the Markdig AST types directly — a different representation
entirely, needed by the editor, not the preview. Rather than re-deriving `HeadingBlock`/
`ParagraphBlock` walking outside `Helpers/AnchorLocator.cs` (duplicating its two-pass match
logic in a second place), `MainViewModel.ResolveAnchorDomId` does its own small two-pass lookup
directly over the already-pure `MarkdownService.GetHeadings`/`GetBlockMarkers` tuples. No file
listed in the design's File Changes table was skipped; this is a small addition inside
`MainViewModel.cs`, which the table already lists as modified for this phase.

**Scope note — intra-document anchor jump implemented although not separately task-numbered**:
`spec.md`'s "Intra-Document Anchor Navigation" requirement (R5) is a MUST with its own scenario,
and Phase 3's own code comments (written by batch 1) explicitly say implementing it is
"Phase 4" work (`EditorView.xaml.cs` and `MainWindow.xaml.cs`, both said so verbatim before this
batch touched them). `tasks.md`'s Phase 4 (4.1-4.6) never gave it its own line item, so this
looks like a gap in the task breakdown rather than a deliberate exclusion. Implemented it as
task "4.7" (added to `tasks.md`) using the exact same building blocks already being wired for
4.1-4.6 (`AnchorLocator`, `SelectAndReveal`, `__mvScrollToId`) — low incremental risk, and
leaving a MUST-level spec requirement completely unimplemented with no future task assigned to
it seemed like the worse outcome. Both click handlers now resolve against the CURRENT
buffer/page synchronously (no tab switch, no `NavigateToLinkAsync` involved) and report a miss
via `StatusSink` exactly like the cross-note case.

**Known, deliberate limitation — self-referencing anchor link (e.g. clicking `[[ThisNote#Heading]]`
while `ThisNote` is already the active tab in that group)**: `OpenFileAsync`'s `SwitchToTab`
early-returns when the target is already `ActiveTab` (`tab == ActiveTab`), so `ActiveTab`'s
setter never runs and `ActiveTabChanged` never fires — meaning the `Loaded`-tick that consumes
`_pendingAnchor` would never run either. Without a guard this would leave a **stale** pending
anchor sitting on the group until some LATER, unrelated tab switch wrongly replayed it against
the wrong note — worse than just not scrolling. `NavigateToLinkAsync` now detects this (captures
whether the target was already the active tab BEFORE calling `OpenFileAsync`) and drops the
pending anchor in that case, exactly like the pre-existing `RedirectIfOwnedElsewhere` (split
editor) case. Net effect: a self-referencing anchor link safely does nothing (no scroll, no
stale replay) instead of scrolling. Not exercised by any spec scenario; flagging in case a
future batch wants the extra polish of an immediate jump for this specific case.

---

## Phase 5 — Marker Creation (Context Menu)

| File | Action | What was done |
|------|--------|----------------|
| `Views/EditorView.xaml.cs` | Modified | `TextEditor_PreviewMouseRightButtonDown` rewritten per design decision #7: `ContextMenu = null` reset kept first; the spellcheck early-`return`s became a single `MisspelledWord?` skip; caret moves to the CLICKED offset first (word offset is applied afterward, only when a misspelling was found, same as before); one local `ContextMenu` built and assigned at a single exit point; suggestions + `Separator` prepended only on a misspelling, "Copiar enlace a este párrafo" always appended. `BuildSuggestionsMenu` (returned a whole `ContextMenu`) replaced with `BuildSuggestionItems` (yields `MenuItem`s to prepend into the shared menu) — same "(sin sugerencias)" fallback. Added `CopyParagraphLink(offset)` and `FindBlockAtPosition(doc, offset)`. |

**Correction to design.md, not a silent deviation — "insert at `block.Span.End`"**: verified via
a disposable scratch console app against the installed Markdig 1.1.2 assembly (same spike
technique as batch 1's Phase 1, nothing committed to the repo) that `SourceSpan.End` is the
**inclusive** index of a block's last character (`Length == End - Start + 1`, confirmed against
`Markdig.Markdown.Parse` output for a plain two-paragraph document). Inserting literally AT
`Span.End` would land the space+marker INSIDE the last word instead of after it. Implemented as
`TextEditor.Document.Insert(block.Span.End + 1, " ^" + id)` — one past the last character, which
is where " ^id" actually belongs. `AnchorLocator`'s existing `Span.Start` usage was unaffected
(a start offset has no such ambiguity).

**Idempotency**: `CopyParagraphLink` checks the resolved paragraph's own `TryGetAttributes()?.Id`
first — if it already starts with `BlockAnchorExtension.IdPrefix` ("mv-b-"), that id is reused
verbatim instead of generating and inserting a second marker. Matches design decision #8 exactly
("if it already carries a marker, reuse it").

---

## Phase 6 — Link Picker, Anchor-Aware Second Step

| File | Action | What was done |
|------|--------|----------------|
| `Services/FileService.cs` | Modified | Added `ReadFile(string path)` — a synchronous sibling of `ReadFileAsync`, same reasoning as the pre-existing `WriteFile`/`WriteFileAsync` pair (a synchronous WPF dialog has nothing to `await` this from). No self-write guarding needed (that's a write-side concern). |
| `Services/IDialogService.cs` | Modified | Doc-comment only on `PickInternalLinkMarkdown` — notes the anchor-aware second step; the method's **signature is unchanged**. |
| `Services/WpfDialogService.cs` | Modified | `PickInternalLinkMarkdown` now passes `App.FileService`/`App.MarkdownService` into `LinkPickerDialog`'s two new optional constructor parameters. This class is already documented as "the untestable edge" (same spirit as `MainWindow.xaml.cs`'s `Plugins_Click` reaching `App.PluginManager` directly), so reaching the app-wide statics here — rather than threading them through `App.xaml.cs`'s `new WpfDialogService()` call and every layer in between — kept this phase's blast radius to exactly the files the design's own table names. |
| `Views/LinkPickerDialog.xaml` / `.xaml.cs` | Modified | Added a second step, shown after picking a file: whole note ("Nota completa"), then headings (`MarkdownService.GetHeadings`), then already-marked paragraphs (`GetBlockMarkers`, each row shows a ≤60-char preview of the paragraph's own text, read via `ParseNote` + a small local `ParagraphPreview` helper — NOT added to `MarkdownService`, since the design's public `GetBlockMarkers` signature is `(Id, Line)` only and Phase 8 test-writing may target it literally). Zero markers → a plain-language, non-selectable hint row (`IsSelectable = false`, grayed via `ListBox.ItemContainerStyle` binding `IsEnabled`) instead of a hidden/empty section. `BuildLinkMarkdown` now takes the chosen anchor and appends `#anchor` (heading text) or `#^id` (block) to both the wikilink and standard-link forms. The dialog reads the picked note from DISK (via the new `FileService.ReadFile`), never from a live editor buffer, and never writes anything — satisfying "never write a marker into a file that isn't the focused editor buffer" by construction, not by a runtime check. |

**"Update picker call site (:809-830)" (tasks.md 4.1 / design.md's File Changes table), resolved
here, not in Phase 4**: `EditorGroupViewModel.InsertInternalLink()` (those exact lines) calls
`_dialogService.PickInternalLinkMarkdown(...)` and inserts whatever markdown string comes back —
it needed **zero code changes**, because `IDialogService.PickInternalLinkMarkdown`'s signature
never changed. The anchor-aware markdown (`[[Nota#Sección]]` / `[[Nota#^id]]`) is produced
entirely inside `LinkPickerDialog.ResultMarkdown`, same as the plain `[[Nota]]` case always was.
Both `tasks.md` and `design.md` list this line range under the `EditorGroupViewModel.cs`
file-bucket for the SAME string ("picker call site"), which reads as if 4.1 alone should have
touched it — but the signature it would need to react to only changes as a consequence of THIS
phase's work, so implementing it here (once, when the actual anchor plumbing exists) rather than
touching that call site twice (once for nothing in Phase 4, once for real in Phase 6) seemed
like the more honest sequencing. Verified compiling and behaviorally unchanged either way.

---

## Discoveries saved to engram (not the OpenSpec artifacts themselves — those stay file-only
per `artifact_store.mode = openspec`)

1. Markdig 1.1.2 `SourceSpan.End` is the INCLUSIVE index of a block's last character
   (`Length == End - Start + 1`) — reusable knowledge for any future code in this codebase that
   inserts text relative to a Markdig block's span; `Span.End + 1` is "right after the block",
   never `Span.End` itself.
2. The `spec.md` alias/anchor ordering discrepancy flagged in batch 1 is still present in
   `spec.md` (not owned by this batch to fix) — repeating the flag here so Phase 8 test-writing
   doesn't copy the wrong worked example verbatim.

---

## Batch 3 (this batch, FINAL) — Phases 7, 8

**Mode**: Standard (Strict TDD explicitly disabled per orchestrator brief for Phases 1-7; Phase 8
is where tests were always meant to land, per the locked "tests last" decision — not TDD, tests
written directly against the already-implemented Phases 1-7 code, then run to green).

**Build**: `dotnet build MarkdownVault.sln` (whole solution). **Result: succeeded, 0 warnings, 0
errors.** **Test**: `dotnet test tests/MarkdownVault.Tests/MarkdownVault.Tests.csproj` — **502/502
passed, 0 failed** (452 pre-existing + 50 new, zero regressions). `dotnet run`/`dotnet
MarkdownVault.dll` was NOT run (forbidden regardless — AGENTS.md gotcha #1); manual verification
(spec's own "Manual" row, task 8.5) was **NOT performed** and remains outstanding — see the
Status section below.

---

## Phase 7 — Performance Measurement (Q5)

Measured with a disposable scratch console app in the session scratchpad (never part of the
repo, deleted from consideration after use — same discipline as batches 1-2's Markdig spikes).
The harness rebuilt `MarkdownService.GetPipeline()`'s EXACT pipeline —
`UseAdvancedExtensions()` + `UseAutoIdentifiers(AutoIdentifierOptions.GitHub)` + a byte-for-byte
copy of the real `Services/BlockAnchorExtension.cs` (namespace changed only) — rather than a
bare Markdig pipeline, so the number includes this change's own extension cost, not just
Markdig's baseline.

**Dev vault**: the "SampleVault" sibling directory
(`C:\MIs Archivos\Agente\Documentacion\SampleVault`, 1 note, 462 bytes) is the project's own
dedicated sample/test vault — used here rather than any of the developer's real personal/work
vaults (`OpenVaultPaths` in `%AppData%/MarkdownVault/settings.json` lists several, containing
real client/business content that has no reason to be touched for a performance measurement).
Since that note is trivially small, `docs/plugins/GUIA-PLUGINS.md` (54078 bytes, 1151 lines) —
the largest `.md` file in THIS repo, which the project's own `AGENTS.md`-driven workflow treats
as living documentation — stood in as a realistic "largest real note" data point.

**Measured (Release build, `Stopwatch`, p50 of 10 warm runs after 2 discard warm-up runs), two
independent runs**:

| Note | Size | Lines | p50 (run 1) | p50 (run 2) |
|------|------|-------|-------------|-------------|
| SampleVault/documento.md | 462 B | 14 | 0.030 ms | 0.032 ms |
| docs/plugins/GUIA-PLUGINS.md | 54,078 B | 1,151 | 6.325 ms | 4.528 ms |
| Synthetic (generated, headings+paragraphs+lists+code, seeded) | 500,068 B | 12,973 | 42.185 ms | 42.814 ms |

**7.2 — threshold applied**: every measurement, including the synthetic ~500KB/13k-line note
at the top of the design's own target range, lands under the **< 50ms** line. Per the
pre-decided, non-re-litigated thresholds: **shipped as-is, no memo, no `Task.Run`.** This task's
entire output is the measurement itself plus this record — no production code changed as a
result of Phase 7.

---

## Phase 8 — xUnit Coverage

### The central discovery: a real, production-breaking defect in batches 1-2's `BlockAnchorExtension`

Writing `AnchorLocatorTests.Find_block_by_marker_id` and
`BlockMarkerTests.Recognized_marker_is_stripped_from_rendered_text_but_block_stays_addressable`
against the REAL `MarkdownService` pipeline (not a synthetic one) failed: a paragraph like
`"Un párrafo marcado ^a1b2c3"` never got an id, and the marker was never stripped from the
rendered HTML. **The entire block-marker feature (Phase 2's core extension, Phase 5's context
menu, Phase 6's picker second step) was silently inert against the actual app pipeline since
batch 1** — every marker written by "Copiar enlace a este párrafo" would have rendered as
literal, unstripped `^a1b2c3` text with no addressable anchor, and every `Nota#^id` link would
have silently taken the broken-anchor path. Two independent, compounding bugs, both fixed in
`Services/BlockAnchorExtension.cs`:

1. **Wrong lifecycle hook.** The original code hooked `ParagraphBlockParser.Closed`, mirroring
   `AutoIdentifierExtension.HeadingBlockParser_Closed` BY NAME only (batch 1 confirmed via
   reflection that the event exists — it never confirmed the handler actually strips anything
   end to end). Reflecting further at Phase 8 showed `AutoIdentifierExtension` does its REAL work
   in a separate `HeadingBlock_ProcessInlinesEnd` method — because `BlockParser.Closed` fires
   during the BLOCK-parsing phase, strictly BEFORE inline parsing runs. `paragraph.Inline` was
   `null` every time the old handler ran; it returned immediately, every time. **Fixed** by
   subscribing to `MarkdownPipelineBuilder.DocumentProcessed` instead (fires once per document,
   after blocks AND inlines both exist). Event-handler subscription order is preserved
   (`Setup()` still runs before the plugin loop, Q6), so the ordering guarantee the design relies
   on is unaffected.
2. **Wrong assumption about inline structure, exposed once #1 was fixed.** Even with
   `paragraph.Inline` populated, `"texto ^a1b2c3"` does NOT parse as one `LiteralInline`.
   `UseAdvancedExtensions()` enables EmphasisExtra's Superscript/Subscript delimiters
   (`^text^`/`~text~`), so the inline parser tokenizes a bare, unmatched `^` into its OWN
   `LiteralInline` — splitting the trailing text into THREE sibling literals ("texto ", "^",
   "a1b2c3"), never one. Checking only `LastChild` ("a1b2c3", no caret) meant
   `BlockMarker.TryStrip` never matched. **Fixed** by walking the whole trailing run of
   consecutive `LiteralInline` siblings (stopping at the first non-literal, which is exactly what
   preserves the bold/link/code-span structural exclusions unchanged), concatenating their text,
   running `TryStrip` on the concatenation, then redistributing the surviving prefix back across
   the run (full nodes kept, the boundary node truncated, fully-consumed trailing nodes detached
   via `Inline.Remove()`).

Both fixes are isolated to `Services/BlockAnchorExtension.cs` — `BlockMarker.TryStrip` itself
(the pure regex layer), `MarkdownService`, `AnchorLocator`, and every other file from batches 1-2
needed zero changes. Verified against the harness with a full case matrix (plain marker,
hyphenated id, bold-wrapped, inline code span, fenced code block, no marker, too-short, caret at
paragraph start, marker not at the paragraph's end, two independent paragraphs each with their
own marker) before porting the fix into the real file, then confirmed again via the actual xUnit
suite (`BlockMarkerTests`, `AnchorLocatorTests`) — all green.

This is exactly the kind of defect the "tests last" convention risks (batches 1-2's own
verification method — a disposable scratch console app driving plain strings against the
installed Markdig assembly — could never have caught it, because it never drove the real
`DocumentProcessed`/inline-parsing timing or the real `UseAdvancedExtensions()` pipeline). It was
NOT downgraded, weakened, or left failing — the underlying code was fixed and the tests that
caught it were kept exactly as originally written.

### Files changed this phase

| File | Action | What was done |
|------|--------|----------------|
| `Services/BlockAnchorExtension.cs` | Modified (defect fix) | See above — `DocumentProcessed` hook instead of `ParagraphBlockParser.Closed`; trailing-literal-run walk instead of `LastChild`-only; new `ApplyStrip` helper. |
| `tests/MarkdownVault.Tests/LinkTargetTests.cs` | Created | 15 facts: bare note, heading anchor, block anchor (sigil stripped), intra-document (heading and block), alias-only, heading-then-alias (Obsidian order), block-then-alias, empty string, `#` inside an alias, whitespace trimming, lone-caret edge case, escaped `#`/`\|`, empty alias after `\|`. |
| `tests/MarkdownVault.Tests/BlockMarkerTests.cs` | Created | 11 facts: `TryStrip` pure cases (valid marker, hyphenated id, `x^2`, `ver anexo^3`, whitespace-preceded-but-too-short, no marker, empty string) plus structural-exclusion cases against the REAL pipeline via `MarkdownService` (bold, inline code span, fenced code block, marker-invisible-but-addressable round-trip). |
| `tests/MarkdownVault.Tests/MarkerIdTests.cs` | Created | 4 facts: shape, determinism for a given seed, 5-retry-then-widen-to-8 collision behavior, and "stop at the first non-colliding candidate" — the last two via a small `QueueRandom : Random` test double that replays a scripted `Next(int)` sequence. |
| `tests/MarkdownVault.Tests/AnchorLocatorTests.cs` | Created | 10 facts: heading by exact id, heading by case-insensitive text fallback, heading miss, block by id, block miss, `AnchorKind.None` → `null`, plus direct coverage of `MarkdownService.GetHeadings`/`GetBlockMarkers`/`ParseNote` (multiple headings in order, namespace-prefix stripping, empty-markers case, same-pipeline-instance guarantee). |
| `tests/MarkdownVault.Tests/FileServiceTests.cs` | Modified | +5 facts covering spec requirement R3 ("Anchored Links Must Never Create a Junk File") with a real temp directory: heading anchor opens the existing note, block anchor opens the existing note, an anchored target on a MISSING note creates only the plain note, an intra-document anchor throws instead of creating a bogus file, and the legacy single-arg `ResolveInternalLink` overload inherits the same guard. Every positive case asserts `Directory.GetFiles(root, "*#*", ...)` is empty. |
| `tests/MarkdownVault.Tests/EditorGroupViewModelTests.cs` | Modified | +5 facts covering the VM-level (non-WPF) half of anchor navigation: `NavigateToLinkAsync` with a heading anchor sets a one-shot pending anchor; with a block anchor the sigil-stripped `Kind`/`Anchor` round-trip correctly; without an anchor no pending anchor is set; `AnchorNavigated` fires with the parsed target on a cross-note navigation; the self-referencing-link case (documented as a deliberate limitation in this file's Phase 4 section) neither fires `AnchorNavigated` nor leaves a stale pending anchor. |

### `InternalsVisibleTo` — already present, no csproj change needed

Batches 1-2 flagged `InternalsVisibleTo("MarkdownVault.Tests")` as a Phase-8 prerequisite for
`AnchorLocator.Find` and `MainViewModel.ResolveAnchorDomId` (both `internal`). Checked
`MarkdownVault.csproj` at the start of this phase: the entry already exists (added previously for
an unrelated test hook — `LoadUnloadForTest`-style access). **No csproj change was required.**
`AnchorLocatorTests.cs` calls `AnchorLocator.Find` directly and it compiles and runs clean.

### Spec traceability (11 requirements / 17 scenarios)

| Requirement | Scenario(s) | Covered by |
|---|---|---|
| Baseline Internal Link Resolution | Target found / not found | Pre-existing `FileServiceTests` (unchanged) + this batch's junk-file facts (found/created cases) |
| Link Target Parsing | Heading vs. block anchor; intra-document | `LinkTargetTests` |
| Anchored Links Must Never Create a Junk File | Found, and created, never named after the anchor | `FileServiceTests` (5 new facts) |
| Cross-Route Anchor Navigation | Same heading anchor, both routes | **Manual only** — `SelectAndReveal`/`ExecuteScriptAsync` are WPF/WebView2, per design.md's own Testing Strategy table |
| Intra-Document Anchor Navigation | Jump, both routes | **Manual only** — same reason; the VM-level pieces feeding it (`NavigateToLinkAsync`, `AnchorLocator.Find`) ARE covered |
| Broken Anchor Handling | Heading anchor not found | `AnchorLocatorTests` (miss → `null`) covers the pure lookup; the `StatusSink` report + "open at top" behavior is wired in `EditorView.xaml.cs`/`MainWindow.xaml.cs` (WPF) — manual only |
| Block Marker Recognition | Recognized vs. not (bold, fenced code) | `BlockMarkerTests` — INCLUDES the defect fix above |
| Marker Invisibility With Addressable Anchor | Hidden but addressable | `BlockMarkerTests.Recognized_marker_is_stripped_from_rendered_text_but_block_stays_addressable` |
| Marker-Writing Boundary | Written into focused note; closed file never modified; zero-marked-paragraphs hint | **Manual only** — `LinkPickerDialog`, the context-menu action, and `TextEditor.Document.Insert` are WPF; no headless-testable seam exists for these three scenarios (see below) |
| Vault-Scoped Anchor Resolution Preserved | Anchor does not bypass vault scoping | Pre-existing `FileServiceTests.ResolveInternalLink_owning_root_never_crosses_into_another_open_root` (unchanged; anchor-splitting happens strictly before this check, per `LinkTargetTests` + `FileServiceTests`) |

### What genuinely cannot be covered headlessly (stated explicitly, not hidden behind a hollow test)

Per design.md's own Testing Strategy table ("`SelectAndReveal`, `ExecuteScriptAsync` and
`ContextMenu` construction stay manual-verification only") and this project's own established
convention (no WPF `Window`/`TextEditor`/`CoreWebView2` is ever instantiated in
`tests/MarkdownVault.Tests/` — confirmed by grep before writing anything):
- **`EditorView.xaml.cs`**: `ConsumePendingAnchorIfAny`, `JumpToIntraDocumentAnchor`,
  `SelectAndReveal`, the context-menu build in `TextEditor_PreviewMouseRightButtonDown`, and
  `FindBlockAtPosition`+`CopyParagraphLink` all need a live `AvalonEdit.TextEditor` and/or
  `ContextMenu`.
- **`MainWindow.xaml.cs`**: `ScrollPreviewToDomIdAsync`, `ApplyPendingPreviewAnchorAsync`,
  `JumpToIntraDocumentPreviewAnchorAsync` all need a live `CoreWebView2`/`ExecuteScriptAsync`.
- **`LinkPickerDialog.xaml(.cs)`**: the whole second-step UI (whole-note/headings/marked-
  paragraphs list, the zero-marker hint row) is a WPF `Window`; nothing in
  `tests/MarkdownVault.Tests/` opens one, and adding the first would be a bigger, separate
  decision than this batch's scope. `MarkdownService.GetHeadings`/`GetBlockMarkers` — the DATA
  this dialog lists — are already covered directly in `AnchorLocatorTests.cs`.

These three files/scenarios are exactly what task 8.5 ("Manual") exists for, and were called out
as such rather than papered over with a test that instantiates nothing and asserts nothing
meaningful.

---

## Discoveries saved to engram (not the OpenSpec artifacts themselves — those stay file-only
per `artifact_store.mode = openspec`)

1. **The `BlockAnchorExtension` defect** (see above, full detail) — reusable knowledge for any
   future Markdig extension in this codebase that reacts to inline content: (a) a per-block
   `BlockParser.Closed` handler runs BEFORE inline parsing — anything needing `block.Inline`
   populated must use `MarkdownPipelineBuilder.DocumentProcessed` (whole-document, after
   everything) or a `ProcessInlinesEnd`-style per-block inline hook, never `Closed`; (b) with
   `UseAdvancedExtensions()` in the pipeline, a bare `^` or `~` character NEVER survives as part
   of a larger literal run — EmphasisExtra's Superscript/Subscript delimiter scanning isolates it
   into its own `LiteralInline` even when unmatched, so "last child is one literal" is never a
   safe assumption for text containing either character.
2. The `spec.md` alias/anchor ordering discrepancy (flagged batches 1 and 2) is STILL present in
   `spec.md` — this batch's tests assert the design.md/tasks.md/Obsidian-correct order per that
   flag, not spec.md's literal worked example. Whoever owns `spec.md` should still fix the typo.

---

## Status

**All 27 tasks across all 3 batches complete**: 9/9 (Batch 1, Phases 1-3) + 10/10 (Batch 2,
Phases 4-6, including the added 4.7) + 8/8 (Batch 3, Phases 7-8, including the additional
non-task-numbered Phase 8 coverage) = **27/27**.

- `dotnet build MarkdownVault.sln`: **succeeded, 0 warnings, 0 errors.**
- `dotnet test tests/MarkdownVault.Tests/MarkdownVault.Tests.csproj`: **502/502 passed, 0
  failed** (452 pre-existing + 50 new; zero regressions).
- Phase 7 measurement: largest real note 4.5-6.3ms, synthetic ~500KB note 42.2-42.8ms — both
  under the 50ms line; shipped as-is per the pre-decided threshold, no code change.
- **A real defect from batches 1-2 was found and fixed this batch** (see above) — the
  block-marker feature was silently non-functional against the real pipeline until now.
- **Outstanding, explicitly NOT done**: task 8.5's manual verification — both click routes, both
  preview routes, the context-menu item vs. a misspelling, the picker with zero markers, and a
  marker inside a fenced Mermaid block — all still need a real run of the `.exe` (never `dotnet
  <dll>` — AGENTS.md gotcha #1). This is the one remaining gap before `link-anchors` is fully
  user-verified; everything else in scope for `sdd-apply` is complete and green.
