# Install and uninstall

1. Start with a compiling CE 12.0.2 Unity source project, its normal dependencies, Unity 6000.3 LTS and Built-in RP.
2. Back up the project, stop Play and wait for compilation.
3. Uninstall legacy SCP-3114 Manager 1.x with its original manager first.
4. Import either or both entire .unitypackages. First import installs default components.
5. Open SCP → Mods → Character Mod Manager. Each mod has independent component selection and Apply/Uninstall buttons.

Uninstall the desired mod, wait for compilation, then optionally remove its installer files. The other mod is preserved. Reinstall using Install if you retained the installer, or reimport the package.

To remove everything, uninstall both, remove both inactive installers, then delete Assets/ScpCeModManager in Unity's Project panel. Assets/ScpCeMods is then empty. Archive/delete ScpCeModBackups only after all mods are removed and the restored project is verified. Scp3114ModBackups belongs to the legacy 1.x manager.

For **Edited after install**, preserve source edits and restore expected files first. Do not delete state/backups to bypass checks. **Source differs** or **GUID conflict** means an unsupported source revision or conflicting manual port. **Recover** restores interrupted transactions after verifying backups.

Console messages: [SCP CE12 Mods]. State: ProjectSettings/ScpCeModsState.json. Transaction: ProjectSettings/ScpCeModsTransaction.json. Backups/completed operations: ScpCeModBackups. Keep these while installed. The Editor build callback copies Translations beside the player; keep the manager while building a modded game. Matching server/client builds and components are required.
