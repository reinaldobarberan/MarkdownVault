# Exploration: Link Anchors (heading + block anchors for `[[Nota#...]]`)

## Problem framing

Today `[[Nota]]` wikilinks open another note but always land at the top of the
file. The user wants two additional jump targets, both Obsidian-compatible:

- **Heading anchors**: `[[Nota#Instalación]]` → jump to a heading.
- **Block anchors**: `[[Nota#^a1b2c3]]` → jump to a specific paragraph, marked
  in the source with a trailing `^a1b2c3`.

Plus intra-document jumps (`[[#Conclusiones]]`, `[texto](#conclusiones)`) that
must scroll the CURRENT note without navigating anywhere. This exploration
verifies the orchestrator's prior file:line claims against the real source,
maps every touch point across both click routes (editor, preview) and both
anchor kinds, and gives the slug-consistency and marker-rendering questions
the weight they need — both are real architectural decisions, not mechanical
wiring.

## Decisions already locked (not reopened here)

1. Scope = heading anchors **and** block/paragraph anchors, Obsidian-syntax-compatible.
2. Broken anchor → open the note from the top **and** show a status-bar message. Never fail silently, never block navigation.
3. MarkdownVault writes `^id` markers **only** into the note currently open and focused in the editor — never into a closed file. Creation flow = editor context-menu action that marks the paragraph under the caret and copies `[[Nota#^id]]` to the clipboard. The link picker offers whole-note / headings / **already-marked** paragraphs only — it never mints a marker in the target note.
4. Intra-document anchors (`[[#X]]`, `[text](#x)`) jump within the current note, no file navigation.
5. Tests last (xUnit, after implementation), but parsing/slugging/marker-lookup must be pure functions, isolated from WPF event args, so they're testable without a UI.

## Current state — verified per file

### Wikilink → HTML conversion (confirmed, and a defect found beyond the brief)

`Services/MarkdownService.cs:74` `CodeSpanRegex` (`` ```...``` `` or `` `...` ``,
compiled) and `MarkdownService.cs:88-104` `PreprocessWikiLinks` split the raw
markdown into code-span / prose segments and run `ConvertWikiLinks` (line
106-122) only on prose. Any new preprocessing for `^id` markers or
intra-document anchors **must** plug into this same split, not run over the
raw string, or it will corrupt fenced Mermaid diagrams whose node shape is
literally `[[subroutine]]` (the comment on `CodeSpanRegex` says this
explicitly).

`ConvertWikiLinks` (line 106-122) does:
```csharp
var href = System.IO.Path.HasExtension(target) ? target : target + ".md";
```
This is confirmed and is worse than the brief states: it does not special-case
a target starting with `#`. So **today**, `[[#Conclusiones]]` already renders
to `href="#Conclusiones.md"` — broken twice over (wrong href AND it isn't
routed as an in-page scroll at all). This is a second, independent defect the
brief didn't call out; intra-document wikilinks are not merely "unimplemented",
they actively mis-render.

`Services/MarkdownService.cs:36` confirmed: `UseAutoIdentifiers(AutoIdentifierOptions.GitHub)`
is on the pipeline, so `<h2 id="...">` etc. already exist in preview HTML for
every rendered document.

### `ResolveInternalLink` — the defect, traced end-to-end (confirmed, worse than a single symptom)

`Services/FileService.cs:456-483` `ResolveInternalLink(string? root, string target, string currentFilePath)`:
```csharp
var normalized = target.Replace('\\', '/').Trim();
if (!SupportedExtensions.Note.Contains(Path.GetExtension(normalized)))
    normalized += ".md";
```
For `target = "Nota#Sección"`, `Path.GetExtension` returns `""` (no dot), so
`normalized` becomes `"Nota#Sección.md"`. Step 1 (relative resolution) fails,
step 2 (`FindInVault`, line ~ search-by-name) fails because no file is
literally named `Nota#Sección.md`, and step 3 (line 474-482) **creates** that
exact junk file next to the current note, seeded with `# Nota#Sección.md`.
Confirmed exactly as the brief claims — this is the sharp edge the whole
feature must close, and every caller of `ResolveInternalLink` funnels into it,
so fixing it once (splitting `target` on the first unescaped `#` before this
method ever sees it, or teaching the method itself to split) fixes both click
routes at once. There's also a **legacy single-arg overload** at line 490-494
(`ResolveInternalLink(target, currentFilePath)`, resolves the owning root
itself) still used by both click handlers below — the fix must land in
whichever layer both overloads funnel through, or one route gets fixed and the
other doesn't.

### Both click routes hit the identical defect, via different string paths

- **Editor** (`Views/EditorView.xaml.cs:568-613` `TextEditor_PreviewMouseLeftButtonDown`):
  `WikiLinkPattern` (`:559-560`, `\[\[([^\]]+)\]\]`) captures the FULL inner
  text of `[[Nota#Sección]]` as one group, i.e. `"Nota#Sección"`. Line 586:
  `if (!Path.HasExtension(target)) target += ".md";` — same append-`.md`
  mistake, independently, before `ResolveInternalLink` is even called (line
  607). `StdLinkPattern` (`:561-562`) has no such append, but a target of
  `"#conclusiones"` from `[text](#conclusiones)` still flows unmodified into
  `ResolveInternalLink("#conclusiones", ...)` at line 607, which appends
  `.md` itself → same junk-file creation. **So today, `[text](#anchor)`
  intra-document links in the editor already create junk files too** — not
  just wikilinks with a `#`.
- **Preview** (`Views/MainWindow.xaml.cs:350-397` `NavigationStarting`): the
  handler does raw substring slicing on `args.Uri` (`args.Uri["http://vault.local/".Length..]`,
  line 360-361), **not** URL-aware fragment parsing. Because `ConvertWikiLinks`
  already baked `"Nota#Sección.md"` into the `href` attribute at render time,
  the browser's own URL resolution splits it into path `Nota` + fragment
  `Sección.md` — but `args.Uri` as WebView2 reports it recombines them back
  into one string for the substring slice, so `relativePath` again ends up as
  the literal `"Nota#Sección.md"` and lands in the same `ResolveInternalLink`
  call (line 373-374). Net effect: **both routes converge on the same string
  shape before hitting `ResolveInternalLink`**, so a fix that intercepts
  `target`/`relativePath` right before that call (split on first `#`, route
  the anchor half separately) closes the bug in both places with one function.

### Anchor-free navigation infrastructure (confirmed, reusable as-is)

- `ViewModels/EditorGroupViewModel.cs:297-307` `NavigateToLinkAsync(string resolvedPath)` —
  confirmed single-`string` signature. Pushes the current file onto a back
  stack, calls `OpenFileAsync(resolvedPath)`, fires `LinkNavigated`. **Needs a
  second parameter for the anchor** (e.g. `string? anchor`), threaded through
  to whatever reveals the target position after the file loads.
- Status channel: `ViewModels/EditorGroupViewModel.cs:37` `StatusSink` (an
  `Action<string>?`, null in tests) is wired at
  `ViewModels/MainViewModel.cs:164`: `group.StatusSink = msg => StatusMessage = msg;`.
  Confirmed — no new notification plumbing needed for decision #2 ("broken
  anchor" message).
- Editor reveal: `Views/EditorView.xaml.cs:361-370` `SelectAndReveal(offset, length)`
  calls `TextEditor.Select(offset, length)` then `TextEditor.ScrollTo(line, column)`.
  Currently private and used only by Find/Replace — it's the right shape to
  reuse for "scroll the editor to a heading/block", but it needs `offset`
  in **document character units**, and it is not currently reachable from the
  link-navigation code path (different class, no `OpenTab`-driven caret
  restore hook wired for it — see next point).
- Per-tab caret/scroll restore: `Views/EditorView.xaml.cs:141-170`
  `OnActiveTabChanged` restores `tab.CaretOffset`/`tab.ScrollOffset` via
  `Dispatcher.InvokeAsync(..., DispatcherPriority.Loaded)` **after** content is
  loaded. This is the correct pattern to imitate for "open note, then jump to
  anchor" — an anchor-jump must likewise wait for the `Loaded` dispatcher tick
  after the tab's content is set, not fire synchronously in
  `NavigateToLinkAsync`, or the document will still be empty/mid-layout when
  `SelectAndReveal` runs.
- Preview click routing, DOM patch vs. full navigation
  (`Views/MainWindow.xaml.cs:417-493` `PushPreview`, read in full this pass):
  confirmed two routes exist — **DOM patch** (`canPatch`, line 469-485: calls
  `window.__mvSetBody(json)` via `ExecuteScriptAsync`, used when only the
  content changed) and **full navigation** (`NavigateToString(html)`, line
  489, used on file/theme/plugin change or first load). **Neither route has
  any anchor/scroll-to-id logic today** — no `window.__mvScrollToId` or
  equivalent exists in the `<script>` block built in
  `MarkdownService.cs:153-178`. This must be added new. Also confirmed:
  `NavigateToString` loads content with no real navigable URL (the
  `NavigationStarting` handler explicitly allows `about:`-prefixed URIs to
  pass through unintercepted, line 353), so **a `#fragment` cannot be relied
  on to auto-scroll on load** the way it would for a real HTTP navigation —
  the anchor jump must be done by an explicit script call after
  `NavigationCompleted` (full-nav route) or immediately after `__mvSetBody`
  (patch route), not by appending `#id` to anything passed to
  `NavigateToString`.

## The highest-risk unknown: slug consistency, resolved with hard evidence

The concern: the editor side must compute the **same** identifier Markdig's
`AutoIdentifierOptions.GitHub` computes for a heading, or a link that resolves
in the preview will fail in the editor.

I inspected the actual referenced package, `Markdig 1.1.2`
(`~/.nuget/packages/markdig/1.1.2/lib/net8.0/Markdig.xml` — the shipped XML
doc, cross-checked against `AGENTS.md`'s stated version) rather than guessing
from general Markdig knowledge. Confirmed public API surface:

- `Markdig.Syntax.MarkdownObjectExtensions.Descendants<T>(MarkdownObject)` —
  generic tree-walk, works on a parsed `MarkdownDocument` (a `ContainerBlock`).
- `Markdig.Renderers.Html.HtmlAttributesExtensions.TryGetAttributes(IMarkdownObject)`
  → `HtmlAttributes` with a public `Id` property.
- `Markdig.Syntax.BlockExtensions.FindBlockAtPosition(Block, int)` — given a
  character offset, returns the enclosing block. This is exactly the primitive
  needed for "find the paragraph under the caret" for the marker-creation
  context-menu action (decision #3).
- `Markdig.Syntax.MarkdownObject.Line` and `.Span` (public) — source position
  of any parsed node.

**Conclusion: do not reimplement GitHub's slug algorithm, and do not
"render and scrape."** Parse the note's markdown text with `Markdig.Markdown.Parse(text, pipeline)`
using the **same pipeline instance** `MarkdownService` already builds (same
`UseAutoIdentifiers(GitHub)`, same plugin extensions), then walk
`document.Descendants<HeadingBlock>()` and read
`heading.TryGetAttributes()?.Id`. This is the literal code path the preview
already exercises — zero drift risk, and it composes automatically with
whatever plugins are active (a plugin-added Markdig extension that also
manipulates headings is picked up for free, because it's the identical
pipeline). The only new work is exposing a pure function on `MarkdownService`
(or a small new pure helper) that takes markdown text and returns
`IReadOnlyList<(string Id, string Text, int Line)>` for the headings —
directly satisfies decision #5's "pure function" testability requirement.

This same mechanism (`FindBlockAtPosition` + `Descendants<ParagraphBlock>()`)
is also the cleanest way to implement the marker-creation context menu: find
the block at the caret offset, walk up to its nearest paragraph-level
ancestor, and use `block.Span` to know exactly where in the source text to
append `^id`.

**Not verified, flagged as an open question for `sdd-design`:** whether
`Markdig.Markdown.Parse` on a full note on every anchor lookup (autocomplete,
link-picker open, editor click) is fast enough to call synchronously on the
UI thread for a large note, or whether it needs caching/debouncing similar to
the existing preview-render debounce (`EditorGroupViewModel`'s
`_previewTimer`). No profiling was done in this pass — say so rather than
assume.

## `^id` marker rendering: two viable approaches, with a real trade-off

The marker must vanish from rendered prose but leave an addressable HTML id.

**Approach A — regex preprocessing, same pattern as `PreprocessWikiLinks`.**
Add a `BlockMarkerRegex` that, like `WikiLinkRegex`, runs only on the
prose-segments `PreprocessWikiLinks` already isolates from `CodeSpanRegex`.
Strip a trailing `\s\^[id-shape]$` from the last line of a paragraph and emit
an inline anchor (`<a id="…"></a>` or `<span id="…"></span>`) at that point —
CommonMark's core parser (no extension needed) passes raw inline HTML through
verbatim, so Markdig renders it as-is.
- Pros: mechanically identical to the wikilink pattern already in the file;
  small diff; easy to reason about in isolation.
- Cons: it is raw-text regex work, so it inherits the exact class of risk
  `CodeSpanRegex` exists to prevent — it must be threaded through the SAME
  segment-splitting `PreprocessWikiLinks` uses, or a marker-shaped caret
  sequence inside a fenced code block (a real risk per decision area #6) gets
  mangled. This is avoidable but is manual work, not free.

**Approach B — a real Markdig extension, mirroring `AutoIdentifierExtension`
itself.** Confirmed via the same XML doc that `AutoIdentifierExtension` is
implemented as an `IMarkdownExtension` that hooks `HeadingBlockParser`'s
`Closed` event (`AutoIdentifierExtension.HeadingBlockParser_Closed`, per the
doc member list) — i.e. it runs on the **parsed AST**, after code spans are
already separate `CodeInline` nodes, not on raw text. A block-marker
extension in the same style (hook a paragraph-block-closed event, or walk
`document.Descendants<ParagraphBlock>()` at document-closed time, inspect the
last inline for a trailing `^id`-shaped literal, strip it from the inline
tree, and call `block.GetAttributes().Id = id`) gets fenced/inline-code
exclusion **for free**, structurally, because it never sees raw text at all —
it only sees already-parsed inlines. I also confirmed the render path already
supports "arbitrary block gets an `id` and it renders": `UseAdvancedExtensions()`
(already called in `MarkdownService.cs:35`) bundles Markdig's own
`GenericAttributes` extension (`{#id}` syntax, confirmed present in the XML
doc — `Markdig.Extensions.GenericAttributes.GenericAttributesParser`), which
proves `ParagraphRenderer.Write` already emits `WriteAttributes` for
paragraphs, not just headings. So `block.GetAttributes().Id = id` on a
`ParagraphBlock` is known-good, not a guess.
- Pros: structurally immune to the code-span false-positive problem (item 6
  below); consistent with how the codebase already extends Markdig (this is
  the exact extension point `IMarkdownContribution.CreateMarkdigExtension()`
  plugins use, per `docs/plugins/GUIA-PLUGINS.md:662-667`); composes
  predictably with plugin-contributed extensions because it lives in the same
  `builder.Extensions` list (`MarkdownService.cs:38-42`) instead of running
  before the pipeline even starts.
- Cons: more code than a regex (needs to walk/mutate the inline tree, handle
  the edge case of a marker sitting inside trailing emphasis/a link — see next
  section); genuinely new to this codebase (no existing "we wrote a Markdig
  extension" precedent to copy from — Mermaid/Callouts/Highlight are all
  simpler asset-injection or already-built extensions).

**My read:** Approach B is the more correct answer specifically *because* it
turns the "avoid corrupting code blocks" concern (explicitly called out in
the brief) from "a regex I have to get right" into "a property of operating
on the AST." Given decision #5 requires pure, testable functions anyway, the
extension's core logic (given a paragraph's inline list, find/strip a
trailing marker) can be written and tested as a pure function independent of
Markdig's event-subscription plumbing. This should go to `sdd-design` as a
recommendation, not a locked decision — it is genuinely new architecture for
this codebase and deserves the design phase's attention specifically on the
"marker inside inline formatting" edge case (e.g. `**bold text ^id**` — does
the id have to be outside all emphasis, matching how Obsidian itself
requires the block id to be the literal last token in the raw line?).

## Preview scroll-to-anchor: needs new JS, on both routes

Confirmed no existing scroll-to-id helper. `MarkdownService.cs:153-178`
defines exactly one page-lifecycle script: `window.__mvSetBody` (DOM patch)
plus a `DOMContentLoaded` listener that wraps tables. A new
`window.__mvScrollToId = function(id) { var el = document.getElementById(id); if (el) el.scrollIntoView({block:'start'}); }`
belongs right next to `__mvSetBody`, defined once, callable from both routes:

- **DOM-patch route** (`MainWindow.xaml.cs:476-485`): after
  `window.__mvSetBody(json)` succeeds, immediately follow with
  `window.__mvScrollToId(json)` in the same `ExecuteScriptAsync` call (or a
  chained one) — the content is already live in the DOM at that point.
- **Full-navigation route** (`MainWindow.xaml.cs:487-492`): `NavigateToString`
  is fire-and-forget; the scroll call must wait for the existing
  `NavigationCompleted` handler (`MainWindow.xaml.cs:347`, currently just sets
  `_previewLoaded = true`) before calling `ExecuteScriptAsync`, or the
  `#mv-content` div won't exist yet.

**Open question, not resolved in this pass:** `PushPreview` currently has no
notion of "the user just navigated to an anchor" vs. "the debounced preview
re-rendered because the user kept typing." Scrolling on every keystroke-driven
re-render would fight the user's own scroll position. This needs an explicit
one-shot "pending anchor" piece of state (set on navigation, consumed and
cleared the next time a push actually completes) analogous to
`_lastPreviewPath`/`_lastPreviewDark` — a real design decision, not
mechanical plumbing.

## Existing-content risk: `^` at end of a line

Legitimate existing uses of a trailing caret exist (math exponents like
`x^2`, or a stray `^` from pasted text) and Markdig's own footnote syntax
(`[^label]`, part of `UseAdvancedExtensions()`, confirmed bundled) uses a
visually similar but syntactically distinct `[^...]` form — bracketed, so it
won't collide with a bare trailing `^id` pattern if the marker regex/AST-check
requires the caret to be OUTSIDE any bracket/link context (true by
construction for Approach B, since footnote references become a distinct
`FootnoteLink` inline node, not a literal `^` character, by the time our code
walks the tree).

The real ambiguity is a short, non-footnote, non-generated caret sequence at
a line's literal end (e.g. a paragraph ending in `...ver anexo^3` as a manual
citation mark). Mitigation, to propose (not decide) in `sdd-propose`: since
decision #3 already restricts marker **creation** to this app's own
context-menu action, that action can generate ids in a fixed, recognizable
shape (Obsidian's own convention: 6 lowercase base-36 characters, e.g.
`^a1b2c3`). Recognizing ONLY that shape (or a slightly wider `^[a-z0-9]{6,}`)
as a block marker — not any bare trailing caret — sharply reduces accidental
matches against real prose while staying Obsidian-compatible (Obsidian
generates the same shape by default, and a hand-authored Obsidian id of a
different shape simply won't be picked up by MarkdownVault yet, which is a
safe degradation, not a corruption).

## Multi-vault interaction

Read `openspec/specs/vault-scoped-resolution/spec.md` and
`openspec/specs/multi-root-workspace/spec.md` in full. Neither spec mentions
anchors, and nothing here conflicts with them:

- `ResolveInternalLink`'s root-scoping (`FileService.cs:456`) is orthogonal to
  the `#anchor` suffix — the fix (splitting the target on `#` before vault
  resolution) sits **before** the root-scoped search, so a heading/block
  anchor on a cross-vault-forbidden link still correctly refuses to cross
  vaults; only the note-name half of the target is ever resolved against a
  root.
- The link picker (`Views/LinkPickerDialog.xaml.cs`) already receives
  `vaultFiles`/`vaultRoot` scoped to the current tab's owning vault (per
  `EditorGroupViewModel.cs:811-813`, confirmed by reading the call site: "the
  link picker only offers notes from THIS tab's own owning vault"). Extending
  it to also list a target note's headings/marked-paragraphs must read that
  target note's content **from the same owning root** — no new cross-vault
  surface is introduced as long as the "read target note for
  headings/markers" step reuses `GetOwningRoot`/`ResolveInternalLink`
  plumbing rather than a fresh unscoped file read.
- Graph scoping (`GraphService.BuildAsync(root)`) is untouched by this
  change — anchors don't add graph edges beyond what wikilinks already add.

No update to either spec's requirements is needed; this change is additive to
whatever capability governs internal-link resolution.

## Which capability spec this belongs to

Neither `vault-scoped-resolution` nor `multi-root-workspace` covers link
syntax or navigation behavior — they cover vault **scoping**. There is no
existing spec for "what `[[x]]` resolves to and how navigation happens." This
change should introduce a **new capability spec**, something like
`internal-link-navigation` (or `link-anchors` matching the change name),
covering: wikilink/standard-link target resolution (including the `#`
split), heading anchors, block anchors, intra-document anchors, broken-anchor
behavior, and the marker-writing boundary. `sdd-spec` should decide the exact
capability name and whether any of today's *implicit* wikilink-resolution
behavior (undocumented in any spec today) should be captured as baseline
requirements alongside the new ones, since right now that behavior exists
only in code and comments, not in `openspec/specs/`.

## Open questions for `sdd-propose` (not decided here)

1. **Marker shape/length** — exact regex for what counts as a recognized
   block id (`^[a-z0-9]{6,}` vs. something looser), to balance
   Obsidian-compatibility against false-positive risk on existing carets.
2. **Regex-preprocessing vs. Markdig-extension** for marker stripping/id
   injection (Approach A vs. B above) — a real architecture call, not
   mechanical.
3. **Marker inside inline formatting** (`**bold ^id**`) — supported or
   explicitly rejected (falls back to "note not found" / no marker
   recognized)?
4. **Anchor scope on `.html`/`.mermaid` targets** — `SupportedExtensions.Note`
   includes `.html`/`.htm`/`.mermaid`/`.mmd` alongside `.md`, but heading
   auto-ids only apply to Markdig-rendered content; raw `.html` files appear
   to take a different, non-Markdig preview path per the comment at
   `MainWindow.xaml.cs` ("Raw .html files have no body fragment and always
   take the full-navigation path"). Should heading/block anchors be scoped to
   `.md`/`.markdown` only for v1? This was not independently verified in this
   pass (the raw-HTML preview code path itself wasn't read) — flagging as
   unconfirmed rather than assuming.
5. **Performance of on-demand Markdig parsing** for autocomplete/link-picker
   heading lookups on large notes/vaults — no profiling done; may need
   caching keyed by file content hash, similar in spirit to existing
   debounce patterns.
6. **Pipeline ordering** if Approach B (Markdig extension) is chosen: should
   the block-marker extension run before or after
   `_registry.MarkdownContributions` (`MarkdownService.cs:38-42`, currently
   appended after core extensions)? A plugin like Callouts that also
   restructures paragraph content could interact with marker stripping
   depending on order — not evaluated in depth here.
7. **"Pending anchor" state lifecycle** in `PushPreview` (see scroll-to-anchor
   section) — exact shape of the one-shot flag needs a design decision.
8. **New-file-in-picker interaction with decision #3**: the link picker shows
   "paragraphs that already have a marker" for a target note — what does the
   UI show when the target note has ZERO marked paragraphs (empty section,
   hidden section, or a hint to open that note and mark one)? A UX-language
   question per the user's own "decisiones en lenguaje simple" preference —
   whoever writes the proposal should phrase this concretely, not as
   "empty-state handling."

## Ready for Proposal

**Yes.** Every claim in the orchestrator's brief was verified against the
actual source and is confirmed accurate except two places where the real
defect is broader than stated: (a) `ConvertWikiLinks` in
`MarkdownService.cs` mishandles a leading `#` (intra-document wikilinks
mis-render today, not just "unimplemented"), and (b) the SAME append-`.md`
defect independently exists in the editor's `StdLinkPattern` branch for
`[text](#anchor)` links, not only in the `WikiLinkPattern` branch. The
slug-consistency question (the brief's "highest-risk unknown") is resolved
with concrete, cited public-API evidence from the actual installed Markdig
1.1.2 package: reuse `Markdig.Markdown.Parse` + `Descendants<HeadingBlock>()` +
`TryGetAttributes()?.Id` against the same pipeline `MarkdownService` already
owns — no slug reimplementation needed. The marker-rendering question has two
concretely evidenced options with a real trade-off (regex-preprocessing vs. a
proper Markdig extension in the style of `AutoIdentifierExtension`); I lean
towards the extension approach but left it for `sdd-propose`/`sdd-design` to
decide since it is new architecture for this codebase. Eight concrete open
questions are listed above for the proposal phase to resolve or explicitly
defer.
