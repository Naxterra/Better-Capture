# BetterCapture completion requirements

- Every completed user-facing application change must produce a versioned Windows x64 installer with `installer/Build-Installer.ps1`.
- Run the application Release build and the complete test suite before building the installer.
- Report the installer path, version, size, and SHA-256 digest when handing off the change.
- Bump the patch version before publishing a new installer or GitHub Release; keep the app project and Inno Setup defaults aligned.
- Do not publish source, tags, installers, or GitHub Releases unless the user explicitly requests publication.
- The in-app updater must remain manual, verify the GitHub asset SHA-256 digest, and require the user to confirm before launching a downloaded installer.
