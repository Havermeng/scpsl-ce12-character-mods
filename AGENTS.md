# SCP:SL CE12 character mods

Public distribution: custom SCP-939 and SCP-3114, CE12.0.2 / Unity6000.3LTS / Built-in. Only mod additions/integration patches belong here. Never commit a whole game, DLLs, credentials or user projects.

Sources/Manager: shared editor installer. Sources/Content/*/manifest.json: components/patches; files/: hashed additions. Run python tools/build_packages.py and python tools/audit_packages.py. Runtime additions preserve original .meta GUIDs; both packages must contain byte-identical manager files. Install/uninstall must preserve the other mod, restore originals exactly and fail on conflicting manual edits.

## Status

2026-10-30: public preview released at https://github.com/Havermeng/scpsl-ce12-character-mods/releases/tag/ce12-mods-v1.0.0-preview.1. Source tag9e169d5. CI40992874898 passed; anonymous downloads and SHA256 match for both packages. Engine37PASS, installer matrix43PASS; payload/GUID graphPASS; game2521C# and editor2C# compile0errors. Fresh Unity import/Mirror weave/gameplay remains pending. See docs/VALIDATION.md; compilation is not runtime acceptance. Legacy3114 1.x requires original uninstall first.

2026-10-30 Preview2 prepared:9391.0.1/3114v2.0.1, six original CE12 source duplicates reused through required GUIDs, no gameplay C# changes. Full Preview1 Unity import/weave/installer acceptance953paths+68native+9giftchecks passed; P2actual Unity font/glyph/material/3prefab+941hashinstall/uninstall checksPASS. Current904payloads/4GUIDgraphs0missing,lint0fail0warn,engine37PASS. Preview remains: no new standalone/remotepeer. Unity Search startup + RagdollHead teardown exceptions documented. Published Preview1 immutable. See docs/VALIDATION.md and docs/UNITY_VALIDATION_20261030.json.

2026-10-30 final: Preview2 PUBLIC/prerelease at releases/tag/ce12-mods-v1.0.0-preview.2, tag9e1413c. Both anonymous whole downloads and GitHub server digests match local SHA256; Preview1 assets unchanged. Final941-path restorePASS; installer/test assets removed and ownQAeditor16696 closed. Protected54main+720cleanSHAexact; usereditor20940 untouched. GitHub jobs41101745708/41101702856 still queued at handoff, not claimed passed; local checks above are complete.
