# Multi-Root Workspace Specification

## Purpose

Several vault roots can be open at once, each shown as its own section in the explorer, instead of today's single active vault that closes everything on switch.

## Requirements

### Requirement: Open Vault Set

The system MUST maintain a set of currently open vault roots (`VaultRoots`) instead of a single `VaultRoot`, and opening a new root MUST NOT close existing tabs.

#### Scenario: Opening a vault adds a root without closing tabs

- GIVEN vault A is already open with tabs from A
- WHEN the user opens vault B via "Administrar vaults"
- THEN vault B is added to the explorer as a new section
- AND all previously open tabs remain open and editable

#### Scenario: Opening an already-open vault is a no-op

- GIVEN vault A is already open
- WHEN the user attempts to open vault A again
- THEN no duplicate section is created and no watcher is duplicated

### Requirement: Per-Root File Watcher

The system MUST create and own one `FileSystemWatcher` per open vault root, keyed by root path, and file-change refreshes MUST be scoped to that root's section.

#### Scenario: Watcher created on open

- GIVEN vault B is not open
- WHEN the user opens vault B
- THEN a `FileSystemWatcher` is created scoped to vault B's directory
- AND file changes in B refresh only B's section of the tree

### Requirement: Close Toggle Behavior

Each row in "Administrar vaults" MUST expose an open/close toggle (not single-active selection). Closing MUST hide the vault's section and dispose its watcher, and MUST NOT close its open tabs.

#### Scenario: Closing a vault with open tabs

- GIVEN vault B is open with two files edited in tabs
- WHEN the user closes vault B via the toggle
- THEN vault B's section disappears from the explorer
- AND vault B's `FileSystemWatcher` is disposed
- AND the two open tabs from vault B remain open and editable
- AND those tabs stop auto-refreshing from disk changes

#### Scenario: Closing the last open vault

- GIVEN only vault A is open
- WHEN the user closes vault A
- THEN the explorer shows no vault sections
- AND any open tabs remain editable

### Requirement: Persisted Open Set

The system MUST persist the set of open vault root paths (`OpenVaultPaths`) and restore it on next launch.

#### Scenario: Restore open set on launch

- GIVEN vaults A and B were open when the app was last closed
- WHEN the app launches
- THEN vaults A and B are both opened and their sections shown
- AND their watchers are created

### Requirement: One-Time Settings Migration

The system MUST migrate the legacy single `LastVaultPath` setting into `OpenVaultPaths` exactly once, guarded by a `VaultPathsMigrated` flag, and MUST NOT re-add a vault the user has since closed.

#### Scenario: First launch after upgrade

- GIVEN an existing `settings.json` has `LastVaultPath` set and no `VaultPathsMigrated` flag
- WHEN the app launches
- THEN `OpenVaultPaths` is seeded with `LastVaultPath`
- AND `VaultPathsMigrated` is set to true and saved

#### Scenario: Deliberately closed vault stays closed

- GIVEN migration already ran once and the user later closed the migrated vault
- WHEN the app relaunches
- THEN the closed vault is not reopened
- AND `OpenVaultPaths` reflects only the vaults the user left open

### Requirement: New File Default Target

When creating a new file with nothing selected in the tree, the system MUST place it in the top (first) open vault root and MUST show that target in the new-file dialog.

#### Scenario: New file, nothing selected

- GIVEN vaults A and B are open, A listed first
- WHEN the user creates a new file without selecting a tree item
- THEN the new-file dialog shows vault A as the target
- AND the file is created under vault A
