# Vault-Scoped Resolution Specification

## Purpose

Wikilinks, image paste, preview rendering, and the graph resolve only within the owning vault root of the relevant file — never across the other open vaults.

## Requirements

### Requirement: Owning Root Resolution

The system MUST provide `GetOwningRoot(path)`, returning the open vault root that contains the given file path, or `null` if the path is outside all open roots.

#### Scenario: Path inside an open vault

- GIVEN vault A and vault B are open
- WHEN `GetOwningRoot` is called with a path under vault A
- THEN it returns vault A's root

#### Scenario: Path outside all open vaults

- GIVEN vault A and vault B are open
- WHEN `GetOwningRoot` is called with a path not under any open root
- THEN it returns `null`
- AND callers fall back to today's single-root behavior without throwing

### Requirement: Vault-Scoped Wikilink Resolution

Wikilink `[[x]]` resolution and autocomplete MUST search only within the owning vault root of the file containing the link.

#### Scenario: Wikilink resolves within same vault

- GIVEN a note in vault A contains `[[Target]]`
- AND a file named `Target.md` exists in both vault A and vault B
- WHEN the wikilink is resolved
- THEN it resolves to vault A's `Target.md` only

#### Scenario: Wikilink autocomplete never crosses vaults

- GIVEN the user is editing a note in vault A and types `[[`
- WHEN the autocomplete suggestion list is built
- THEN only files from vault A appear as suggestions
- AND no files from vault B appear

### Requirement: Vault-Scoped Image Paste

Pasting an image MUST save it into the `assets/` folder of the owning vault root of the active tab's file, and the inserted link MUST be relative to that vault.

#### Scenario: Paste into a vault-B note

- GIVEN the focused tab is editing a file owned by vault B
- WHEN the user pastes an image
- THEN the image is written under vault B's `assets/` folder
- AND the inserted markdown link is relative to vault B's `assets/`

### Requirement: Preview Host Scoped To Focused Tab

Before each preview render push, the system MUST remap the `vault.local` virtual host to the owning root of the currently focused tab.

#### Scenario: Switching focus between tabs from different vaults

- GIVEN tab 1 is open on a file from vault A, tab 2 on a file from vault B
- WHEN focus moves from tab 1 to tab 2
- THEN the next preview push remaps `vault.local` to vault B's root
- AND relative images in the vault-B note resolve correctly

### Requirement: Graph Scoped To Focused Vault

The graph view MUST show only the vault of the currently focused tab's owning root, and MUST rebuild via `GraphService.BuildAsync(root)` when focus moves to a note in a different vault.

#### Scenario: Graph follows focus across vaults

- GIVEN the graph is showing vault A's map because tab 1 (vault A) is focused
- WHEN the user switches focus to tab 2 (a note in vault B)
- THEN `GraphService.BuildAsync` is invoked against vault B's root
- AND the graph updates to show only vault B's notes and links

#### Scenario: No cross-vault edges appear

- GIVEN vault A and vault B each contain notes with matching titles
- WHEN the graph is built for vault A
- THEN it contains no nodes or edges from vault B
