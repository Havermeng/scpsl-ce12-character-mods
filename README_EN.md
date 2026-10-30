# SCP:SL CE12 — SCP-939 & SCP-3114

[Русский](README.md) · [Install/uninstall](docs/INSTALL_EN.md) · [Components](docs/COMPONENTS.md) · [Validation](docs/VALIDATION.md)

**Preview:** fresh Unity import, compilation/Mirror weaving, independent removal and 68 local-host gameplay checks passed. A new standalone player and independent remote peer have not been tested. See the log limitations in [VALIDATION](docs/VALIDATION.md).

Two separate Unity packages for **SCP:SL Community Edition 12.0.2**, **Unity 6000.3 LTS**, **Built-in RP**:

- **Custom SCP-939:** dressed variant, petting, hugs, hand-holding, gifts with item handover animation, dates/dinner, dialogue and affection.
- **SCP-3114:** disguise, attacks, strangle, HUD/help and animations; optional gothic maid variant and natural spawning.

Download the desired **.unitypackage** from [Releases](https://github.com/Havermeng/scpsl-ce12-character-mods/releases). Check the [validation status](docs/VALIDATION.md). Import the entire package through **Assets → Import Package → Custom Package…**, wait for compilation, then use **SCP → Mods → Character Mod Manager**. Use either or both; removing one preserves the other.

This targets the **Unity source project**, not the retail Steam executable or EXILED. Back up your project and stop Play first. Other reconstructions, platforms, Unity branches and render pipelines require separate verification. Legacy SCP-3114 Manager 1.0/1.1 must be uninstalled with its original manager first. Manual ports and unknown source changes are rejected rather than overwritten.

## Controls

Remote Admin: **forcerole <player ID> Scp939Dressed**, **Scp3114**, or **Scp3114Dressed**.

For 939, approach and press Interact (normally E). Use wheel/arrows to select, E/Enter to confirm, Backspace to go back. **939 help** lists administrative interaction commands.

For 3114, primary fire slaps; hold Interact on an eligible corpse to disguise; hold Reload (normally R) to reveal; secondary fire after revealing strangles. F1 opens help. Rebindings are respected. Natural spawning is disabled by default; dressed variants are admin-only. Server/client builds need matching mods and components.

## Removal and editing

Use **Uninstall** in the manager, wait for compilation, then optionally **Remove installer files**. Installed sources are editable. Before changing/removing a manually edited installation, preserve your edits and restore expected files: conflicts are blocked rather than silently erased. Keep **ScpCeModBackups** and **ProjectSettings/ScpCeModsState.json** while installed. Interrupted transactions have a **Recover** button.

These packages do not replace scenes, lighting/graphics, platform/account code, Package Manager dependencies or Windows network settings. They do not make the base project standalone. Film scripts, building, Human14 and dummy frameworks are not bundled; optional adapters can use existing integrations.

Ready-made voice recordings are excluded because the previously used model card lists an unknown license. You may enable custom-WAV support and add **Assets/StreamingAssets/Scp939Voice/custom/<line-key>.wav**. Keys are in Scp939Lines.cs. Subtitles without voice are the default.

Sources/Content contains additions/integration patches; Sources/Manager contains the editor manager. Maintainers can build with **python tools/build_packages.py** (Python 3.10+); recipients do not need Python or Git.

Created for **Havermeng** with assistance from **OpenAI Codex (GPT-6 family)** and **Claude Code** (earlier model not recorded). Original game/content: **Northwood Studios / Hubert Moszka and SCP:SL developers**. See [credits](CREDITS.md), [license scope](LICENSE.md) and [validation](docs/VALIDATION.md).
