# Changelog

## Preview 2 — 2026-10-30

- SCP-939 1.0.1: updated validation and installation documentation; gameplay content unchanged.
- SCP-3114 2.0.1: reuse CE12's identical font sources and normal texture instead of bundling six duplicates; preserve TMP assets and material behavior. Smaller payload and explicit base-asset requirements.
- Fresh Unity import/weaving, actual installer/component/removal checks and 68 + 9 native local-host checks now documented. The six changed asset references and exact 941-path restoration were checked separately for Preview 2.
- Still Preview: no new standalone-player or independent remote-peer acceptance; known editor/teardown exceptions are retained in VALIDATION.md.

## First combined distribution — 2026-10-30

### SCP-939 1.0.0

- Extracted the existing custom SCP-939 into a separate installer.
- Added independent outfit, interaction, gift, date/dinner, dialogue/affection and custom-WAV switches.
- Removed hard dependencies on separate cinema, dummy, building and Human14 frameworks; optional adapters can use existing integrations.
- Kept CE12 arm-mesh bindings without importing unrelated character models.

### SCP-3114 2.0.0

- Migrated the previous 1.1.0 package to the common reversible installer.
- Preserved core/maid/natural-spawn selection and native content.
- Legacy 1.x requires its original uninstall first.

### Shared manager

- Independent installation order, component selection and removal.
- Shared patches recomposed from immutable local backups.
- Hash/GUID/source conflicts stop before overwriting; durable crash recovery.
- English/Russian instructions, credits, editable distribution sources and checksums.
