# Internal Link Navigation Specification

## Purpose

Defines how `[[wikilink]]` and `[text](path)` targets are parsed and
resolved — to a file, a heading, a block, or a point in the current
document — and how editor/preview navigate there. Captures today's implicit
baseline resolution (undocumented until now) alongside new anchor behavior:
the central new rule, "anchored links must never create a junk file," has
no baseline to attach to without it.

## Requirements

### Requirement: Baseline Internal Link Resolution
The system MUST resolve a bare note-path target via, in order: (1) relative
to the current file's directory, (2) by name/path anywhere in the owning
vault root, (3) if still not found, create a new note next to the current
file seeded with `# {name}`. Scoped to the owning root; never searches or
creates outside it. (Baseline, `Services/FileService.cs:456-483`.)

#### Scenario: Target found vs. not found
- GIVEN a target resolves to an existing file (relative or by vault search)
- WHEN the link is resolved
- THEN that file opens and no new file is created
- GIVEN instead no file matches by either method
- WHEN the link is resolved
- THEN a new note is created next to the current file, seeded with `# {name}`

### Requirement: Link Target Parsing
The system MUST strip a trailing `|alias` from the target first, then split
what remains on the FIRST unescaped `#` into a path part and an anchor part,
before any file resolution runs (`Nota#Sección|Alias` → path `Nota`, anchor
`Sección`, alias `Alias`). Order matters: the alias comes LAST, after the
anchor, per Obsidian syntax. The anchor part is classified: block (`^` + id,
e.g. `^a1b2c3`), heading (any other non-empty text), or none (no `#`). An
empty path (target starts with `#`) is classified intra-document.

#### Scenario: Heading vs. block anchor on another note
- GIVEN target `Nota#Instalación`
- WHEN parsed
- THEN path `Nota`, kind heading, value `Instalación`
- GIVEN target `Nota#^a1b2c3` instead
- WHEN parsed
- THEN path `Nota`, kind block, value `a1b2c3`

#### Scenario: Intra-document anchor
- GIVEN target `#Conclusiones` (from `[[#Conclusiones]]` or `[text](#conclusiones)`)
- WHEN parsed
- THEN path is empty, kind is intra-document

### Requirement: Anchored Links Must Never Create a Junk File
Only the path part of a parsed target MUST reach baseline resolution
(create-if-missing included). The system MUST NOT append the anchor text to
the filename or create a file named after path+anchor (e.g.
`Nota#Sección.md`) — in the editor and the preview alike. Today both
independently append `.md` to the UNSPLIT target before resolution
(`Services/FileService.cs:461-462`; `Views/EditorView.xaml.cs:586`; preview
reaches the same call via `Views/MainWindow.xaml.cs:373-374`).

#### Scenario: Anchor never produces a junk file, found or created
- GIVEN target `Nota#Sección` and `Nota.md` exists
- WHEN resolved
- THEN `Nota.md` opens and no `Nota#Sección.md`-shaped file is created
- GIVEN target `Nueva#Sección` and no `Nueva.md` exists instead
- WHEN resolved
- THEN only `Nueva.md` is created (baseline), never a file named after the anchor

### Requirement: Cross-Route Anchor Navigation
Clicking a heading or block anchor link MUST produce the same outcome from
the editor route and the preview route: the target file opens (or is
created per baseline) and the view scrolls to the resolved position.

#### Scenario: Same heading anchor, both routes
- GIVEN `[[Nota#Instalación]]` exists in a note
- WHEN clicked from the editor, and separately from the preview
- THEN both routes open `Nota` and land at the `Instalación` heading

### Requirement: Intra-Document Anchor Navigation
A target classified intra-document MUST scroll the current note to the
matching heading or block, without opening, navigating, or creating any
file — from both the editor and the preview.

#### Scenario: Intra-document jump, both routes
- GIVEN the current note contains a heading/block matching `[[#Conclusiones]]`
  or `[text](#conclusiones)`
- WHEN clicked in the editor, and separately in the preview
- THEN each scrolls/selects within the SAME buffer/page
- AND neither route opens a tab, navigates, or creates a file

### Requirement: AST Positions Are Never Live-Document Positions
The Markdown AST used to resolve anchors and paragraphs is parsed from a
REWRITTEN copy of the note (wikilinks are expanded into standard Markdown
links before parsing), so its character offsets do NOT index the text the
user has on screen. Any position taken from that AST and applied to the live
document — to scroll, to select, or to insert a marker — MUST travel as a
LINE NUMBER, never as a character offset. Line numbers survive the rewrite
because it substitutes within a line and never adds or removes newlines; the
wikilink pattern MUST exclude newlines so that stays a guarantee rather than
an accident of typical input.

#### Scenario: Anchor after a wikilink resolves to the right place
- GIVEN a note whose FIRST line contains a wikilink
- AND a heading or marked paragraph further down
- WHEN an anchor link to it is clicked from either route
- THEN the view lands on that heading/paragraph, not on a shifted position

#### Scenario: Marker is written into the paragraph the user picked
- GIVEN a note whose FIRST line contains a wikilink
- WHEN the user asks to copy a link to a paragraph further down
  (context menu or toolbar button)
- THEN the `^id` marker is written into THAT paragraph
- AND no position out of the document's range is ever requested

### Requirement: Broken Anchor Handling
When the path resolves to a file but the anchor is not found in it, the
system MUST open that note from the top and report the failure via the
status channel (`StatusSink`, `ViewModels/EditorGroupViewModel.cs:37`). It
MUST NOT fail silently and MUST NOT block navigation.

#### Scenario: Heading anchor not found
- GIVEN `Nota#NoExiste` where `Nota.md` exists but has no such heading
- WHEN resolved
- THEN `Nota.md` opens at the top and a status-bar message reports the miss

### Requirement: Block Marker Recognition
The system MUST recognize a block marker only as the literal last token of
a block, immediately preceded by whitespace, matching
`\^[A-Za-z0-9][A-Za-z0-9-]{5,63}`, outside all inline formatting
(bold/italic/links). It MUST NOT recognize a marker-shaped sequence inside a
fenced/inline code span or inside emphasis/link formatting (proposal
decisions #1, #3 — new behavior, no existing code to cite).

#### Scenario: Recognized vs. not recognized
- GIVEN a paragraph ending `...texto final ^a1b2c3`
- WHEN parsed
- THEN it is addressable as block `a1b2c3`
- GIVEN instead `**bold ^a1b2c3**`, or a `^xxxxxx`-shaped token inside a
  fenced code block or Mermaid diagram
- WHEN parsed
- THEN no marker is registered and the text/code is left untouched

### Requirement: Marker Invisibility With Addressable Anchor
A recognized marker MUST be stripped from the rendered preview text while
the enclosing block still carries the marker's id as an addressable anchor
(e.g. an HTML `id`); the raw markdown source stays unchanged and
Obsidian-readable.

#### Scenario: Marker hidden but addressable
- GIVEN a paragraph ending `^a1b2c3` recognized as a marker
- WHEN rendered to preview
- THEN the visible text omits `^a1b2c3`
- AND the block carries id `a1b2c3`, reachable via `Nota#^a1b2c3`

### Requirement: Marker-Writing Boundary
The system MUST write a new `^id` marker ONLY into the note currently open
AND focused in the editor, via an explicit context-menu action on the
paragraph under the caret. It MUST NOT write a marker into any file that is
not the focused editor buffer, including a target note selected in the link
picker.

#### Scenario: Marker written into the focused note only
- GIVEN the caret is in a paragraph of the open, focused note and the user
  invokes "Copiar enlace a este párrafo"
- WHEN the action runs
- THEN a `^id` marker is appended in that open buffer and `[[Nota#^id]]` is
  copied to the clipboard

#### Scenario: Closed file is never modified
- GIVEN the link picker targets a note that is NOT the focused editor buffer
- WHEN the user picks that note to insert a link
- THEN the picker offers only the whole note, its headings, and paragraphs
  that ALREADY carry a marker
- AND no marker is written into that note's file on disk

#### Scenario: Target note has no marked paragraphs
- GIVEN the picker's target note has zero existing block markers
- WHEN the picker lists its anchors
- THEN it shows the note and headings normally, plus a plain-language line
  stating there are no marked paragraphs and how to add one — never a
  hidden or empty section

### Requirement: Vault-Scoped Anchor Resolution Preserved
The `#` split MUST happen before root-scoped resolution runs, so only the
path part is ever checked against the owning vault root. Anchor presence or
kind MUST NOT change vault scoping (see
`openspec/specs/vault-scoped-resolution/spec.md`).

#### Scenario: Anchor does not bypass vault scoping
- GIVEN a target whose path part would resolve outside the current owning
  root, with an anchor suffix attached
- WHEN resolved
- THEN it is refused exactly as it would be without the anchor
