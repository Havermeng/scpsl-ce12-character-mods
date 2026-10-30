# SCP:SL CE12 character mods

Public distribution: custom SCP-939 and SCP-3114, CE12.0.2 / Unity6000.3LTS / Built-in. Only mod additions/integration patches belong here. Never commit a whole game, DLLs, credentials or user projects.

Sources/Manager: shared editor installer. Sources/Content/*/manifest.json: components/patches; files/: hashed additions. Run python tools/build_packages.py and python tools/audit_packages.py. Runtime additions preserve original .meta GUIDs; both packages must contain byte-identical manager files. Install/uninstall must preserve the other mod, restore originals exactly and fail on conflicting manual edits.

## Status

2026-10-30: publication preparation. Engine37PASS, installer matrix43PASS; payload/GUID graphPASS; game2521C# and editor2C# compile0errors. Fresh Unity import/Mirror weave/gameplay remains pending. See docs/VALIDATION.md; compilation is not runtime acceptance. Legacy3114 1.x requires original uninstall first.
