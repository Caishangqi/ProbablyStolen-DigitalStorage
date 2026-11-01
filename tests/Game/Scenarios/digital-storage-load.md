# Digital Storage load check

## Purpose

This scenario checks that Probably Stolen discovers the `digital_storage` package, accepts its normal approval flow, loads the `IMod` entry after restart, and can enter a disposable game. It does not test item storage, transfer, inventory persistence, or save compatibility.

The current Mods-menu compatibility version is `0.46D`; `GameVersions/Version` must match it. The internal `PlayerStore.VERSION_NUMBER` text `VERSION 100-Rev6` is a different value. If another installation displays a different compatibility version, confirm it and update the manifest and package checks before continuing.

## Prepare the package

1. Close Probably Stolen. The deployment command refuses to continue while the selected game process is running.
2. Build the Release package against the intended installation:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 -GameDirectory "<game-root>"
   ```

   The command resolves the game files, builds the solution, and writes the package under `artifacts/package/DigitalStorage` by default. It does not install or launch the game.
3. Check the package inventory and manifest:

   ```powershell
   python -B scripts/tests/test_package.py --package-dir artifacts/package/DigitalStorage
   ```

   The expected inventory is `manifest.xml` and four product DLLs: Common, Client, Platform, and Mod. Save the package version and SHA-256 values from the command output for the evidence record. Stop if validation fails or the installed game version differs from the manifest.
4. If using the optional F8 scanner, check its console prerequisite. The local modding guide says the console is available when an empty `dev.txt` file exists beside the game executable. Do not overwrite an existing file. Create one only when authorized and record ownership for cleanup. Without it, mark only the F8 diagnostic as not run; the normal review/enable flow remains available.

## Install and scan

1. Deploy the validated package to the resolved `Mods/DigitalStorage` directory:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/deploy.ps1 -Action Deploy -GameDirectory "<game-root>"
   ```

   Review the printed target and each `Add`, `Update`, or `Unchanged` line. Continue only when the target is the intended installation's `Mods/DigitalStorage` directory. If the script reports an identity mismatch, an unknown file, or an unsafe path, stop. Do not remove or rename files to bypass the check.
2. Launch Probably Stolen through Steam (App ID `4348910`). The user performs all game UI operations manually. Open the Mods menu and locate `Digital Storage` with ID `digital_storage`. If it is absent, stop and record the loader error. Do not proceed from a successful build alone.
3. If the F8 console is available, run this optional diagnostic:

   ```text
   mod-scan digital_storage
   ```

   Record the scan result separately. The current manifest declares four assemblies and no capabilities. Stop and investigate any finding or hard refusal. When F8 is unavailable, record `Not run` for this command and use the normal review flow in the next step.
4. Use the game's normal review and enable action. Confirm the loader accepts the package without a version or capability refusal. Do not use Developer mode to bypass approval. Record the action shown by the game and any warning or scan result.
5. Exit the game completely and launch it again. Check the game diagnostics for `Digital Storage: initialized.` and `Digital Storage: enabled.` The game may add its normal mod-name prefix. Record where these messages were observed and any Digital Storage, ModLoader, dependency, or lifecycle error.
6. Start a new game or a save explicitly reserved for testing. Confirm the game enters and remains responsive. Do not test storage behavior or use a valuable save. This mod version only logs startup lifecycle status.

## Restore the test environment

Close the game before changing deployed files. To remove this package, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/deploy.ps1 -Action Remove -GameDirectory "<game-root>"
```

The command removes only the validated manifest and four product DLLs from `Mods/DigitalStorage`. It stops if the directory contains an unknown file or a different manifest identity. It does not change other mods, saves, or game trust records. Remove `dev.txt` only if this scenario created it, and only after the game has closed.

## Evidence record

Record only the details needed to reproduce the result. Remove player names, save names, account identifiers, and absolute machine paths from logs or screenshots.

```text
Status: Not run | Pass | Fail | Blocked
Test date and time (UTC):
Game compatibility version shown in Mods menu:
Steam build ID:
Mod ID / name / author / version: digital_storage / Digital Storage / Caizii / 0.1.0
Package manifest and DLL SHA-256 values:
Deployment target (sanitized): <game>/Mods/DigitalStorage
Optional F8 mod-scan result (Pass | Fail | Not run):
Normal review/loader check and enable result:
Diagnostic observation source:
Full game restart confirmed:
Initialization message observed:
Enable message observed:
Disposable game entered:
Digital Storage or loader errors:
dev.txt existed before test / created by test / removed by test:
Evidence file names after sanitization:
```

Set `Status: Pass` only after the normal loader review accepts the package, approval and a full restart succeed, both lifecycle messages are observed, and the game enters the disposable session. F8 `mod-scan` is a separate optional diagnostic; do not report it as passed unless it ran. Any actual scanner refusal must be resolved before proceeding. A package check, menu listing, or process launch alone is not a pass. If required review, runtime logs or game-entry observations are unavailable, leave the status `Blocked` and the game-load task open.
