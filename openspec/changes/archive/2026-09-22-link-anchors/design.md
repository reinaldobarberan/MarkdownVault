# Design: Link anchors (`[[Nota#Sección]]`, `[[Nota#^a1b2c3]]`, `[[#intra]]`)

## Technical Approach

One pure parser (`LinkTarget`) splits every link target *before* any resolution, so `FileService.ResolveInternalLink` (`Services/FileService.cs:456`) never sees a `#` and cannot mint `Nota#Sección.md`. A host-owned Markdig extension gives marked paragraphs an `id` at parse time. The anchor then travels as a second argument through `NavigateToLinkAsync` and is consumed **once** at each destination: the editor resolves it to a character offset from the Markdig AST; the preview resolves it to a DOM id via new JS.

## Architecture Decisions

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 1 | Target parsing | `Helpers/LinkTarget.cs`, pure record `Parse(raw) → (Note, Alias, Anchor, Kind)`. Split `\|` first, then the first `#` of the note half. `Kind = Heading \| Block \| None`; empty `Note` = intra-document. | Single source of truth for `Views/EditorView.xaml.cs:559-562`, `Views/MainWindow.xaml.cs:361` and `Services/MarkdownService.cs:106`, which disagree today about `\|`. Closes defects 1-3 in one function. |
| 2 | HTML id for a block | Extension sets `Id = "mv-b-" + rawId`; the `.md` file keeps the bare `^a1b2c3`. | A `^` in a URL fragment is percent-encoded by the browser and would fight `Uri.UnescapeDataString` (`MainWindow.xaml.cs:361`). Namespacing also removes any collision with a GitHub heading slug. Invisible to Obsidian — only our rendered HTML is namespaced. |
| 3 | Extension hook | `Services/BlockAnchorExtension.cs` subscribes in `Setup(builder)` to the paragraph parser's `Closed` event, mirroring the confirmed `AutoIdentifierExtension.HeadingBlockParser_Closed`. Strip logic is a pure `BlockMarker.TryStrip(lastInlineLiteral) → (text, id?)`. Stateless — no per-document field. | AST-only, so code spans/Mermaid are structurally immune. Statelessness is required because `_pipeline = null` on plugin toggle (`MarkdownService.cs:23`) rebuilds and discards the instance. *(Exact parser type name unconfirmed — verify at apply.)* |
| 4 | **Q6 — pipeline order** | Register ours at `MarkdownService.cs:37`, i.e. **before** the `_registry.MarkdownContributions` loop (`:38-42`). | `Extensions` is an ordered list; `Setup` runs in list order at `Build()`, so our `Closed` handler subscribes first and strips the marker before any plugin handler sees the paragraph. Render-time plugin hooks are unaffected — the id already sits on `GetAttributes()`. **Residual:** a plugin that *replaces* a `ParagraphBlock` (a Callouts-style restructure) drops its `HtmlAttributes` and the id is lost — the anchor then degrades to the broken-anchor path, never to corruption. Not preventable from our side; §9 hot-unload is unaffected because our extension is host-owned. |
| 5 | Anchor handoff, editor | `NavigateToLinkAsync(path, string? anchor = null)` (`EditorGroupViewModel.cs:297`) stores a one-shot; `EditorView.OnActiveTabChanged` consumes it via `ConsumePendingAnchor()` **inside the existing `DispatcherPriority.Loaded` continuation** (`EditorView.xaml.cs:155-166`), *after* the `tab.CaretOffset`/`ScrollOffset` restore. | One continuation, one ordering — the anchor scroll runs last and wins, so tab-state restore cannot fight it. One-shot means a plain tab switch never scrolls. Default parameter keeps existing call sites compiling. |
| 6 | Anchor → offset | Pure `AnchorLocator.Find(MarkdownDocument, LinkTarget) → int?`. Heading: match `TryGetAttributes()?.Id`, then fall back to the heading's plain text (ordinal-ignore-case). Block: match the namespaced id. Offset = `block.Span.Start` → `SelectAndReveal(offset, 0)` (`EditorView.xaml.cs:361`, made internal). | Obsidian users write the visible heading text; `[t](#slug)` users write the slug. Two-pass covers both. `null` → broken-anchor path. |
| 7 | **Context-menu risk** (`EditorView.xaml.cs:497-520`) | Keep `ContextMenu = null` at the top, but turn the spellcheck early-`return`s into *skips*. Build one local `ContextMenu`, append "Copiar enlace a este párrafo" always, prepend the suggestion items + a `Separator` when a misspelling was found, and assign at a **single exit point**. Set the caret to the clicked offset first. | No XAML `ContextMenu` can survive the unconditional null-out. Single exit means the spellcheck path is preserved verbatim, just no longer terminal. |
| 8 | Marker creation | `FindBlockAtPosition(caretOffset)` → nearest paragraph; if it already carries a marker, **reuse** it (idempotent). Else `MarkerId.Generate()` = 6 lowercase base-36 chars, regenerated on collision against `GetBlockMarkers(buffer)` (5 attempts, then 8 chars). Insert `" ^id"` at `block.Span.End` via `TextEditor.Document.Insert` so undo works. Clipboard gets `[[Nota#^id]]`. | Never writes to a closed file (locked decision 3); never appends a second marker to the same paragraph. |
| 9 | **Q7 — preview one-shot** | New group event `AnchorNavigated` wired in the same factory as `LinkNavigated` (`MainViewModel.cs:208`) into `MainViewModel.ConsumePendingPreviewAnchor()`. `PushPreview` calls it **after** `__mvSetBody` returns (patch route, `MainWindow.xaml.cs:476-485`) and `NavigationCompleted` (`:347`) calls it for the full route, since `NavigateToString` (`:489`) is fire-and-forget. | Consumed-and-cleared, so debounced typing re-renders never steal the user's scroll. The View never reaches into the group. |
| 10 | Preview scroll JS | `window.__mvScrollToId(id)` beside `__mvSetBody` (`MarkdownService.cs:161`); returns `false` when the id is absent → C# reads the `ExecuteScriptAsync` result and routes to `StatusSink`. Id passed JSON-serialized, never concatenated. | A `#fragment` cannot auto-scroll a `NavigateToString` page. Unicode heading slugs make concatenation unsafe. |
| 11 | Broken / unsupported anchor | Open the note from the top, then `StatusSink` (`EditorGroupViewModel.cs:33` → `MainViewModel.cs:164`). Anchors on non-`.md/.markdown` targets take the same path (v1). | No new notification infrastructure. *Unconfirmed: the raw-`.html` preview path was never read — apply must confirm it takes full navigation.* |
| 12 | Defense in depth | `ResolveInternalLink` itself also truncates at the first `#` before the extension check (`FileService.cs:459-462`), covering the legacy overload (`:490-494`). | If a call site is missed, the worst case is "anchor ignored", never a junk file. |

## Q5 — parse cost: **NOT RESOLVED. Requires measurement at apply time.**

No profiling was done and none is inventable here. What the design *can* fix is exposure:

- **Every parse is user-gesture-driven, one per gesture.** Anchor click → parse the destination's in-memory buffer once. Link-picker selection → one read + one parse. "Copiar enlace" → one parse of the active buffer. **Zero parses in the preview render path** (the extension runs inside the render Markdig already performs) and **zero in any timer/debounce path** (`_previewTimer` is untouched).
- **Measurement `sdd-apply` MUST take**: `Stopwatch` around `Markdig.Markdown.Parse(text, GetPipeline())`, p50 of 10 warm runs, on (a) the largest note in the dev vault and (b) a synthetic ~500 KB / 10k-line note; `Debug.WriteLine` the result.
- **Thresholds, decided in advance so apply does not have to re-litigate**: < 50 ms → ship as-is, no cache. 50-150 ms → add a single-entry memo `(filePath, content-version) → (headings, markers)`, invalidated on buffer change. > 150 ms → move the parse to `Task.Run` and `await` it *before* the `DispatcherPriority.Loaded` reveal tick; the tick already defers, so the async gap is harmless.

## Data Flow

    [[Nota#^a1b2c3]] --click--> LinkTarget.Parse --note--> ResolveInternalLink --> path
                                      |
                                    anchor
                                      +--> NavigateToLinkAsync(path, anchor)
                                      |      +-> OpenFileAsync -> ActiveTabChanged
                                      |            +-(Loaded tick)-> caret/scroll restore
                                      |                  +-> ConsumePendingAnchor
                                      |                        +-> AnchorLocator.Find
                                      |                              +-> SelectAndReveal | StatusSink
                                      +--> AnchorNavigated -> MainViewModel pending
                                             +-> PushPreview / NavigationCompleted
                                                   +-> __mvScrollToId(id) | StatusSink

## File Changes

| File | Action | Change |
|------|--------|--------|
| `Helpers/LinkTarget.cs` | Create | Pure parse + classify. Sole splitter of `\|` and `#`. |
| `Helpers/AnchorLocator.cs` | Create | Pure `Find(MarkdownDocument, LinkTarget) → int?`. |
| `Helpers/MarkerId.cs` | Create | Pure generate + collision check. |
| `Services/BlockAnchorExtension.cs` | Create | Markdig extension + pure `BlockMarker.TryStrip`. |
| `Services/MarkdownService.cs` | Modify | Register extension at `:37`; `ConvertWikiLinks` href via `LinkTarget` (`:106-122`); `__mvScrollToId` at `:161`; new pure `GetHeadings(text)` / `GetBlockMarkers(text)` / `ParseNote(text)`. |
| `Services/FileService.cs` | Modify | `:456-483` + `:490-494` — truncate at `#` before the `.md` append. |
| `Views/EditorView.xaml.cs` | Modify | `:497-520` single-exit context menu + new item; `:568-613` drop the blind `.md` append, route the anchor, intra-doc jump without navigation; `:155-166` consume pending anchor; `SelectAndReveal` → internal. |
| `Views/MainWindow.xaml.cs` | Modify | `:350-397` `LinkTarget`-aware split; `:347` + `:476-492` pending-anchor scroll on both routes. |
| `ViewModels/EditorGroupViewModel.cs` | Modify | `:297` `NavigateToLinkAsync(path, anchor)`; `ConsumePendingAnchor`; `AnchorNavigated` event; `:809-830` picker call site. |
| `ViewModels/MainViewModel.cs` | Modify | `:208` wire `AnchorNavigated`; pending-preview-anchor one-shot. |
| `Views/LinkPickerDialog.xaml(.cs)`, `Services/IDialogService.cs:25`, `Services/WpfDialogService.cs:57` | Modify | Second step: after picking a note, list *whole note / headings / already-marked paragraphs*, read through the **same owning root**. Zero markers → the grey hint line, never a hidden section. |
| `tests/MarkdownVault.Tests/` | Create | Written **last**. |

## Interfaces

```csharp
public readonly record struct LinkTarget(string Note, string? Alias, string? Anchor, AnchorKind Kind)
{
    public static LinkTarget Parse(string raw);          // pure, never throws
}
public enum AnchorKind { None, Heading, Block }

internal static int? Find(MarkdownDocument doc, LinkTarget t);      // AnchorLocator
public IReadOnlyList<(string Id, string Text, int Line)> GetHeadings(string markdown);
public IReadOnlyList<(string Id, int Line)> GetBlockMarkers(string markdown);
public async Task NavigateToLinkAsync(string resolvedPath, string? anchor = null);
```

## Testing Strategy — tests LAST, over the pure boundaries only

| Layer | What | Approach |
|-------|------|----------|
| Unit | `LinkTarget.Parse`: `Nota`, `Nota#H`, `Nota#^id`, `#intra`, `Nota\|Alias`, `Nota#H\|Alias`, empty, `#` inside an alias | xUnit, strings only |
| Unit | `BlockMarker.TryStrip`: valid marker, `x^2`, `ver anexo^3`, `**bold ^id**` (rejected), marker inside a code span | xUnit over inline literals |
| Unit | `MarkerId` shape + collision regeneration | xUnit, seeded |
| Unit | `GetHeadings` / `GetBlockMarkers` / `AnchorLocator.Find` — heading by id, heading by text, block by id, miss → `null` | xUnit against `Markdig.Markdown.Parse`, no WPF |
| Manual | Both click routes, both preview routes, context-menu item vs. a misspelling, picker with zero markers, marker inside a fenced Mermaid block | Run the app |

Every pure helper takes `string` or a Markdig object — no `MouseButtonEventArgs`, no `TextEditor`, no `CoreWebView2`. `SelectAndReveal`, `ExecuteScriptAsync` and `ContextMenu` construction stay manual-verification only.

## Migration / Rollout

No migration. All changes are additive or locally revertible (see the proposal's rollback plan). The only non-revertible residue is `^id` text already written into notes — plain text, Obsidian-native, never corrupt.

## Open Questions

- [ ] **Q5 parse cost — UNRESOLVED.** Blocked on the measurement specified above. Thresholds and the fallback are pre-decided, so this does not block `sdd-tasks`.
- [ ] Paragraph-parser `Closed` event name/type in Markdig 1.1.2 — inferred from the confirmed `AutoIdentifierExtension` shape, **not** directly verified. If absent, fall back to a document-processed walk of `Descendants<ParagraphBlock>()`; the pure strip logic is unaffected either way.
- [ ] `AutoIdentifierOptions.GitHub` handling of accented headings (`## Instalación` → `instalación` or `instalacion`?) — unverified. The heading-text fallback in decision 6 makes this non-blocking.
- [ ] Raw-`.html` preview path never read (carried over from the exploration). Confirm it takes full navigation before relying on decision 11.
