# KUDUCK

Image & PDF tools, one duck away: a small desktop duck for Windows that opens a toolbox for pictures and PDFs.
Part of the KUDU family of free, open-source Windows apps by Azeem Bukhari.

## Working with Azeem (the owner)

- **Branches:** work on `main` and push to `main`. Ask before creating any other branch.
- **New features:** first say what you understood, and ask your doubts and suggestions, one question at a time.
  Only start building when Azeem says "build".
- **Look:** keep the app's current look. No redesigns unless asked.
- **About window credit:** "Made by Azeem, with Claude and ♥" (the heart in the accent colour). Keep it.
- **Commits:** author `Azeem Bukhari <12558389+azeembukhariprivate@users.noreply.github.com>`. Clear messages that say
  what changed and why. Don't put AI model names or versions in commits, code or docs.
- **Words:** the app's text, README and release notes use plain everyday words, not technical jargon.
- **Before pushing:** build and run the tests locally, and re-read the diff. After pushing, watch the GitHub Actions
  build until it's green; fix failures, never skip or disable a test to get green.

## How the app is built

- .NET 10 WPF app (`src/Kuduck`), published self-contained for win-x64. It doesn't use kudu-common.
- Some image work (e.g. background removal) runs in a Python helper (`helper/kuduck_helper.py`, built with
  PyInstaller in CI) using the `silueta.onnx` model downloaded during the build.
- Results never overwrite the original files. PDF locking uses AES-256; passwords are never put on a command line.
- Updates are checked by version and checksum only.
- `tests/Kuduck.Tests` makes real images and PDFs and checks every tool; see "Build it yourself" in the README for the
  full installer steps.

```
dotnet run --project tests/Kuduck.Tests
dotnet run --project src/Kuduck
```

## Releasing

1. Raise `<Version>` in `src/Kuduck/Kuduck.csproj`.
2. Commit and push to `main`.
3. GitHub Actions (`.github/workflows/build.yml`, Windows) tests and builds the app. When no release exists yet for that
   version it creates the `v` tag and publishes the installer on the Releases page, plus a copy named
   `KUDUCK-Setup.exe` that the README's download button links to (`releases/latest/download/KUDUCK-Setup.exe`).
   Pushes that keep the same version only build.
