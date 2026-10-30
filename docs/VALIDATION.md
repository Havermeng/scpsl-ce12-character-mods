# Validation / Проверки

## Current release gate

**Unity import, Mirror weaving and fresh in-game acceptance of this combined distribution are pending.** The source/install checks below passed; they are not a claim of new gameplay verification. Keep the release as a preview/draft until that check is completed.

## Passed on 2026-10-30

- **37 C# engine checks:** exact BOM/CRLF restoration, shared-patch deduplication, both installation orders, independent removal, manual-edit/preview-race protection, invalid paths, corrupted backups, payload tampering, actual GUID collisions and case-insensitive path handling.
- **43 installer scenarios using the real mod payloads:** both orders and independent removals; all 26 valid SCP-939 feature combinations with SCP-3114 present; all four SCP-3114 combinations with SCP-939 present; exact restoration of 958 target paths; recovery after two deliberate abrupt exits during writes. Large unchanged asset requirements used presence/GUID placeholders in this installer-only fixture; this was not a Unity asset-load test.
- **Actual game C# compiled against installed Unity references:** both defaults (2521 sources), 939 alone (2471), 3114 alone (2499), both cores (2519). Zero errors in each. Warnings from the reconstructed baseline remain; the both-default compile emitted 376 warnings. These are compiler checks without Unity/Mirror weaving or a player build.
- **Editor manager:** both new C# files compile against Unity 6000.3 editor references, zero errors.
- **916 payload hashes** and serialized asset GUID graphs: both defaults, both cores, all939, all3114; zero missing GUIDs or collisions. This does not prove imported local file IDs or rendered appearance.
- **Package boundary audit:** only installer/bootstrap Assets in the outer archive; hashed mod additions in the inner payload; vanilla integration as patches/translation rows; no game DLL/EXE, shell script or whole project; identical shared manager in both packages. Package checksums accompany release files.
- **Publication lint:** no copied loose game files, credential patterns or private user paths found in the staged public sources. This lexical check is not a legal or malware guarantee.

## Previous content verification

The source SCP-3114 1.1 package previously passed a real clean-copy Unity import/install/removal suite (116 checks plus restart/reimport checks). Earlier 1.0 content passed 133 installation checks, a zero-error Release build and 34 local native gameplay checks. Those are historical tests of earlier packages, not new 2.0 acceptance.

The integrated project's gift handover previously passed functional/visual checks in that project's human-model environment. This extracted CE12 package removes that hard dependency and therefore still needs its own rendered gameplay check.

No independent remote server/client acceptance or compatibility with arbitrary 12.0 forks is claimed. The currently verified environment is Windows / CE12.0.2 / Unity6000.3.3f1 / Built-in RP.
