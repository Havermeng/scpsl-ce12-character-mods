# Validation / Проверки

## Current release gate

**Fresh Unity import, compilation/Mirror weaving and local-host gameplay checks passed.** Keep this distribution as Preview: no new standalone player or independent remote-peer acceptance is claimed. The editor and teardown exceptions below remain documented.

## Passed on 2026-10-30

- **37 C# engine checks:** exact BOM/CRLF restoration, shared-patch deduplication, both installation orders, independent removal, manual-edit/preview-race protection, invalid paths, corrupted backups, payload tampering, actual GUID collisions and case-insensitive path handling.
- **43 installer scenarios using the Preview 1 payloads:** both orders and independent removals; all 26 valid SCP-939 feature combinations with SCP-3114 present; all four SCP-3114 combinations with SCP-939 present; exact restoration of 958 target paths; recovery after two deliberate abrupt exits during writes. Large unchanged asset requirements used presence/GUID placeholders in this installer-only fixture; this was not a Unity asset-load test.
- **Actual game C# compiled against installed Unity references:** both defaults (2521 sources), 939 alone (2471), 3114 alone (2499), both cores (2519). Zero errors in each. Warnings from the reconstructed baseline remain; the both-default compile emitted 376 warnings. These are compiler checks without Unity/Mirror weaving or a player build.
- **Editor manager:** both new C# files compile against Unity 6000.3 editor references, zero errors.
- **904 current payload hashes** and serialized asset GUID graphs: both defaults, both cores, all939, all3114; zero missing GUIDs or collisions. This does not prove imported local file IDs or rendered appearance.
- **Package boundary audit:** only installer/bootstrap Assets in the outer archive; hashed mod additions in the inner payload; vanilla integration as patches/translation rows; no game DLL/EXE, shell script or whole project; identical shared manager in both packages. Package checksums accompany release files.
- **Publication lint:** no copied loose game files, credential patterns or private user paths found in the staged public sources. This lexical check is not a legal or malware guarantee.

## Preview 1 full Unity acceptance — 2026-10-30

The exact two released archives were imported into a separate full CE12.0.2 source copy on Windows, Unity6000.3.3f1, Built-in RP. This copy was reconstructed to the original baseline before testing; the user's main and clean projects were not modified.

- Actual Unity compilation and Mirror weaving completed with zero C# errors. Both packages auto-installed after a full removal and reimport; all 953 default-install target hashes matched.
- A real Unity-manager full uninstall restored all 953 target paths exactly (original hashes or absence for added files). Independent removal worked in both directions. Disabling 939 features reset their resource cache. The actual editor window selection controller preserved the other mod's saved settings even when its checkboxes had unsaved edits.
- Both dressed-role prefabs loaded without missing scripts. Native character models and hands rendered. These scoped checks do not clear unrelated baseline assets.
- **68/68 local-host checks passed** in Facility: native 3114 slap, timed disguise, reveal, strangle/restraints, stolen identity/inventory, bone-owner inspection, first-person animator/FOV and maid death; 939 on vanilla CE12 human models, pet/hug/hand-hold, accepted/refused/cancelled gifts, date seating, dialogue, death and local role.
- A separate focused native handover run passed **9/9 checks**, including actual presentation, finite geometry and authoritative item removal. The main game camera and an observer were inspected. The observer temporarily lifted local-owner culling for its diagnostic render; it is not an independent peer.
- The fixture used a loopback host and connection test actors. Platform/menu bootstrap was excluded in the test scene, and Steam was not initialized. The distributed mods do not change authentication or make the base project standalone.
- Temporary test scripts/scenes were archived and removed from the source copy. Final state: clean Edit Mode, zero C# errors, all 953 installed hashes intact.

### Observed log limitations

- UnityEditor.Search.SearchDatabase raised a startup indexing exception in this fresh editor. It is retained separately from gameplay exceptions; this release does not claim a completely error-free editor log.
- The 68 gameplay checks and their final gameplay-error gate passed. During subsequent Play teardown, DeathAnimations.RagdollHead.OnAnimationEnded logged a MissingReferenceException while removing a corpse. This was not observed during the gameplay checks. The focused no-death handover run had no gameplay or teardown exceptions. No claim is made that this was independently reproduced without the mods.
- Baseline missing-script warnings outside the added role prefabs remain outside this scope. No performance/FPS, arbitrary-fork, platform-startup, new player-build or remote-peer result is implied.

## Preview 2 follow-up — SCP-939 1.0.1 / SCP-3114 2.0.1

- Removed six source-file duplicates from SCP-3114: five licensed font files and one normal texture now reference the byte-identical assets already present in CE12. Their paths and GUIDs are explicit installer requirements. The mod keeps its own TMP font assets, fallbacks, materials and appearance; gameplay C# is unchanged.
- Both updated packages were imported and installed in the same isolated Unity copy: zero C# errors; all 941 default target hashes exact. All three role prefabs loaded with zero missing scripts. Five TMP source fonts resolved, FontEngine loaded every face, Latin glyphs were present and the three Roboto faces also provided Cyrillic. The blood material resolved the base normal texture. Normal-map/sRGB/filter/mipmap/compression settings match the removed duplicate.
- Actual Unity uninstall restored all 941 paths exactly to their original hashes or absence. The copy returned to unmodified game sources, without the temporary test fixture. The full 68 + 9 gameplay checks above apply to Preview 1; this follow-up tests the changed asset references and does not claim a repeated full gameplay run.
- Current serialized GUID graphs pass for both defaults, both cores, all939 and all3114: zero missing GUIDs/collisions. The transactional engine again passed 37 checks. Publication lint against the reconstructed original Assets baseline passed with zero failures and warnings. Published Preview 1 assets remain immutable.

## Previous content verification

The source SCP-3114 1.1 package previously passed a real clean-copy Unity import/install/removal suite (116 checks plus restart/reimport checks). Earlier 1.0 content passed 133 installation checks, a zero-error Release build and 34 local native gameplay checks. Those are historical tests of earlier packages, not new 2.0 acceptance.

The integrated project's gift handover previously passed functional/visual checks in that project's human-model environment. The extracted package removes that hard dependency; the fresh vanilla-human handover check above now supplements the earlier integrated-project tests.

No independent remote server/client acceptance or compatibility with arbitrary 12.0 forks is claimed. The currently verified environment is Windows / CE12.0.2 / Unity6000.3.3f1 / Built-in RP.
