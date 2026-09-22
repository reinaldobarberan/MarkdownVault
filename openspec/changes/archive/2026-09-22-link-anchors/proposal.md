# Proposal: Link anchors (`[[Nota#Sección]]`, `[[Nota#^a1b2c3]]`, `[[#intra]]`)

## Intent

Wikilinks always land at the top of the target note. Users want Obsidian-compatible jump targets: headings (`#Sección`), marked paragraphs (`#^a1b2c3`) and intra-document jumps (`[[#Conclusiones]]`, `[texto](#conclusiones)`).

This is not only a missing feature — it is an active data-corruption bug. `FileService.ResolveInternalLink` (`Services/FileService.cs:456-462`) appends `.md` to any target without a recognized extension, so `Nota#Sección` becomes `Nota#Sección.md`, is never found, and step 3 (`:474-482`) **creates that junk file on disk** seeded with `# Nota#Sección.md`. Every click route reaches it. The exploration confirmed two further defects beyond the original brief, both owned by this change.

## Scope

### In Scope
- **Heading anchors**, **block anchors** (`^id` markers), **intra-document anchors** — editor route and preview route, both.
- **Defect 1 (new)**: `MarkdownService.ConvertWikiLinks` (`:106-122`) does not special-case a leading `#`, so `[[#Conclusiones]]` renders today as `href="#Conclusiones.md"` — mis-rendered, not merely unimplemented.
- **Defect 2 (new)**: the editor's `StdLinkPattern` branch (`Views/EditorView.xaml.cs:561-562, 590`) passes `#conclusiones` unmodified into `ResolveInternalLink`, which appends `.md` itself → `[texto](#ancla)` **already creates junk files in the editor today**.
- **Defect 3 (free)**: the editor's `WikiLinkPattern` (`:559-560`) captures `[^\]]+`, so `[[Nota|Alias]]` yields the target `Nota|Alias`. The shared wikilink-target parser this change introduces splits `|` and `#` in one place, so this closes at zero extra cost.
- Marker creation via a new editor right-click item **"Copiar enlace a este párrafo"** — marks the paragraph under the caret and copies `[[Nota#^id]]`.
- Link picker extended to offer: whole note, its headings, and paragraphs that **already** carry a marker.
- Broken anchor → open the note from the top **and** report via `StatusSink` (`ViewModels/EditorGroupViewModel.cs:37`, wired at `MainViewModel.cs:164`).
- xUnit coverage **at the end**, over pure functions (anchor parsing, heading extraction, marker strip/lookup).

### Out of Scope
- Writing `^id` into any file that is not the focused editor buffer (locked decision #3).
- Anchors into `.html`/`.htm`/`.mermaid`/`.mmd` targets — v1 is `.md`/`.markdown` only (see decision 4).
- Anchor autocomplete while typing `#` inside `[[ ]]`, anchor-aware graph edges, backlinks-by-anchor.

## Capabilities

### New Capabilities
- `internal-link-navigation`: **confirmed** — neither `vault-scoped-resolution` nor `multi-root-workspace` covers link syntax or navigation; they cover *scoping*. Today's wikilink resolution exists only in code. The spec must capture the **baseline** (3-step resolve, create-if-missing) alongside the new anchor behavior — otherwise "must not create `Nota#Sección.md`" has no requirement to attach to.

### Modified Capabilities
- None. Anchor splitting happens **before** root-scoped resolution, so vault confinement is untouched.

## Approach — the open questions, decided

| # | Question | Decision |
|---|----------|----------|
| 1 | Marker shape | Generate Obsidian's own shape: `^` + 6 lowercase base-36 chars. **Recognize** `\^[A-Za-z0-9][A-Za-z0-9-]{5,63}` only at the literal end of a block, preceded by whitespace. Rules out `x^2` and `ver anexo^3`; a hand-written short Obsidian id is simply not picked up — safe degradation, not corruption. |
| 2 | **Regex vs. Markdig extension** | **RECOMMEND the extension (Approach B).** A host-owned `IMarkdownExtension` that walks `Descendants<ParagraphBlock>()` at document-close, strips a trailing marker inline and sets `block.GetAttributes().Id`. It never sees raw text, so code-span/fenced-block immunity is **structural**, not a regex to get right — that is the whole point, given `CodeSpanRegex` (`MarkdownService.cs:70-75`) exists precisely because Mermaid's `[[subroutine]]` gets mangled otherwise. It is also the extension point the codebase already uses (`MarkdownService.cs:38-42`). Cost: ~1 new class vs. ~1 regex. Invisible to the user either way, so this is an engineering call, not a user decision. Renders via `GenericAttributes` (already bundled by `UseAdvancedExtensions()`, `:35`) — paragraphs already emit `WriteAttributes`. |
| 3 | `**bold ^id**` | **Not recognized** — matches Obsidian (the id must be the block's literal last token, outside all formatting). The marker then renders as visible literal text, which *tells* the user it is wrong. Our own generator always writes outside formatting. |
| 4 | `.html`/`.mermaid` targets | Anchors apply to `.md`/`.markdown` only in v1. Anchor on any other note type → open from top + status message (reuses the broken-anchor path). *Unconfirmed, per exploration: the raw-HTML preview path was not read.* |
| 5 | Parse cost on the UI thread | **Unresolved — for `sdd-design`, no profiling done.** The editor already holds the note text in memory (no disk read). Add caching keyed by content only if design measures a problem. |
| 6 | Pipeline ordering | Register the marker extension **before** the `_registry.MarkdownContributions` loop (`:38-42`), so ids are assigned against the original paragraph structure before a plugin (e.g. Callouts) restructures it. Residual interaction risk stays a design concern. |
| 7 | "Pending anchor" state | One-shot field on `MainWindow` beside `_lastPreviewPath`/`_lastPreviewDark`: set on link navigation, **consumed and cleared** by the next completed `PushPreview` (`:417-493`) on either route. Typing-driven re-renders therefore never steal the user's scroll. |
| 8 | Picker with zero marked paragraphs | Show the note and its headings normally, plus one grey line: *"Esta nota no tiene párrafos marcados. Abrila y usá 'Copiar enlace a este párrafo'."* Never a hidden or empty section. |

**Slug consistency (the exploration's highest-risk unknown) is closed**: do not reimplement GitHub slugs and do not scrape rendered HTML. Parse with `Markdig.Markdown.Parse` against the **same pipeline instance** `MarkdownService` already builds, then `Descendants<HeadingBlock>()` + `TryGetAttributes()?.Id`. Zero drift, composes with plugin extensions for free.

**Preview scroll** needs new JS: a `window.__mvScrollToId(id)` next to `__mvSetBody` (`MarkdownService.cs:153-178`), called after `__mvSetBody` on the patch route and after `NavigationCompleted` (`MainWindow.xaml.cs:347`) on the full-navigation route. A `#fragment` cannot auto-scroll — `NavigateToString` has no navigable URL.

**Editor scroll** reuses `SelectAndReveal` (`Views/EditorView.xaml.cs:361-370`), fired on the `DispatcherPriority.Loaded` tick exactly as `OnActiveTabChanged` (`:141-170`) does, never synchronously inside `NavigateToLinkAsync`.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Helpers/LinkTarget.cs` | New | **Pure**: split wikilink inner text into note / `\|`alias / `#`anchor; classify anchor (heading, `^block`, intra-doc). Single source of truth for both click routes. |
| `Services/BlockAnchorExtension.cs` | New | Markdig extension: strip trailing marker inline, set `GetAttributes().Id`. Core strip logic written as a pure function. |
| `Services/MarkdownService.cs` | Modified | `:106-122` leading-`#` fix; register extension at `:38-42`; `__mvScrollToId` at `:153-178`; new pure `GetHeadings(text)` / `GetBlockMarkers(text)` returning `(Id, Text, Line)`. |
| `Services/FileService.cs` | Modified | `:456-483` + legacy overload `:490-494` — anchor half never reaches resolution; `.md` append only on a bare note name. |
| `Views/EditorView.xaml.cs` | Modified | `:568-613` drop blind `.md` append, route anchor, intra-doc jump without navigation; expose `SelectAndReveal`; `:497-520` add the "Copiar enlace a este párrafo" item. |
| `Views/MainWindow.xaml.cs` | Modified | `:350-397` fragment-aware split; `:417-493` pending-anchor one-shot + scroll on both routes. |
| `ViewModels/EditorGroupViewModel.cs` | Modified | `:297-307` `NavigateToLinkAsync(path, string? anchor)`; broken-anchor message via `StatusSink`; `:809-830` picker call site. |
| `Services/IDialogService.cs` `:25`, `Views/LinkPickerDialog.*` | Modified | Picker offers headings + already-marked paragraphs of the selected note, read through the **same owning root** (`GetOwningRoot`), no new cross-vault surface. |
| `tests/MarkdownVault.Tests/` | New | Anchor parsing, heading extraction, marker strip/lookup — written **last**. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Right-click menu collision: `TextEditor_PreviewMouseRightButtonDown` (`EditorView.xaml.cs:497-520`) sets `ContextMenu = null` on **every** right-click and returns early off a misspelling — a XAML-declared menu would be destroyed | **High** | Build the new item **inside that handler**, appended to the suggestions menu or as the sole item. Discovered this pass; not in the exploration. |
| Marker false-positive on real prose | Low | ≥6 chars, block-final, whitespace-preceded (decision 1). |
| Markdig extension is new architecture here; plugin-order interaction | Med | Register before plugin contributions; pure, unit-tested strip logic; worst case the marker renders literally — visible, never corrupting. |
| `Markdig.Parse` on the UI thread for a large note | Med, **unprofiled** | Measure in `sdd-design` (decision 5). Editor path uses the in-memory buffer. |
| Anchor jump fires before the document is loaded | Med | `DispatcherPriority.Loaded` tick, imitating `OnActiveTabChanged`. |
| `ResolveInternalLink` fix changes note-creation behavior | Low | Only the `#` half is stripped first; `[[Nueva]]` still creates as today. |

## Rollback Plan

All code changes are additive or local reversions: restore the `.md` append in `FileService.cs:460-462` and `EditorView.xaml.cs:586`, drop the extension registration at `MarkdownService.cs:38-42`, drop `__mvScrollToId` and the pending-anchor field, revert `NavigateToLinkAsync` to its single-`string` signature. No settings or schema migration to undo.

**One non-revertible residue, stated honestly**: `^id` markers already written into notes stay on disk. They are plain text, Obsidian understands them natively, and reverted MarkdownVault renders them as visible literal text — ugly, never corrupt, and removable by hand.

## Dependencies

None external. Markdig 1.1.2, WebView2 and AvalonEdit are already referenced; all APIs used were verified against the installed package.

## Success Criteria

- [ ] `[[Nota#Sección]]` and `[[Nota#^a1b2c3]]` jump to the right place from **both** the editor and the preview — and **never create a junk file**.
- [ ] `[[#Conclusiones]]` and `[texto](#conclusiones)` scroll the current note without navigating or creating anything.
- [ ] A broken anchor opens the note from the top **and** shows a status-bar message — never silent, never blocked.
- [ ] "Copiar enlace a este párrafo" marks the paragraph under the caret and puts `[[Nota#^id]]` on the clipboard; the marker is invisible in the preview and the note stays readable in Obsidian.
- [ ] The picker never writes a marker into a closed file; with zero marked paragraphs it says so in plain language.
- [ ] A `^id`-shaped sequence inside a fenced code block or a Mermaid diagram is untouched.
- [ ] Anchors resolve only within the note's own vault; cross-vault behavior is unchanged.
