<p align="center">
  <img src="docs/logo.png" width="120" alt="KUDUCK logo">
</p>

<h1 align="center">KUDUCK</h1>
<p align="center"><b>Image &amp; PDF tools, one duck away.</b><br>
A small desktop duck for Windows that opens a toolbox for your pictures and PDFs.</p>

<p align="center">
  <a href="https://github.com/azeembukhariprivate/kuduck/releases/latest/download/KUDUCK-Setup.exe"><img src="https://img.shields.io/badge/Download-KUDUCK%20for%20Windows-2ea44f?style=for-the-badge" alt="Download KUDUCK for Windows"></a>
  <br>
  <sub>The installer for Windows 10 and 11, always the latest version · <a href="https://github.com/azeembukhariprivate/kuduck/releases/latest">what's new</a></sub>
</p>

---

## What it does

A yellow duck sits on your desktop. **Click it** to open the toolbox, **drag it** anywhere, or
**drop files on it** and KUDUCK opens the right tool. The duck also suggests your last action
(“You resized 3 photos to 1080 px — do it again?”) so repeat jobs take one click.

**Image tools**
- **Resize** — by pixels, percent or presets (Full HD, square post, story, email…)
- **Convert** — JPG, PNG, WEBP, BMP, ICO, GIF, TIFF (also opens HEIC/AVIF photos)
- **Compress** — choose the quality, optionally remove hidden location/camera info
- **Passport photo** — India, US, UK, UAE, India visa/OCI, Schengen, Canada, Pakistan or a custom size;
  finds the face automatically, white or light-blue background, digital file or 4×6 / A4 print sheet at 300 dpi
- **Remove background** — transparent PNG or a plain colour
- **Images → PDF** — A4, Letter or image-sized pages

**PDF tools**
- **Merge**, **Split** (every page, ranges or every N pages), **Compress**
- **PDF → Word**, **PDF → Excel**, **Word → PDF**
- **Pages** — rotate, reorder and delete pages with thumbnails
- **Lock / Unlock** — add or remove a password (locked PDFs use AES-256 encryption)
- **Scan → Text** — read text from scanned PDFs and photos (OCR)

**Also:** light and dark mode, keep-on-top, start with Windows, update notices.

## Private by design

Everything runs **on your PC**. No files are uploaded and no account is needed. The only time KUDUCK
goes online is to check this page for a newer version (you can turn that off in Settings).

## Updates

KUDUCK checks this project's Releases page shortly after it starts and then once a day while it runs
(or any time with **Settings → Check now**). When a new version is out:

- the **duck** says so in its speech bubble, with **Update** and **Later** buttons,
- a **Windows notification** appears once from the tray icon,
- the **home screen** shows a banner with **Update now** and **What's new**.

**Update** downloads the new installer from this repository's Releases page, checks it against the
checksum GitHub publishes, installs it quietly (no admin prompt, settings kept) and reopens KUDUCK.

## Install

1. [Download the installer](https://github.com/azeembukhariprivate/kuduck/releases/latest/download/KUDUCK-Setup.exe) (`KUDUCK-Setup.exe`, always the latest version). It's also on the [latest release](https://github.com/azeembukhariprivate/kuduck/releases/latest) as `KUDUCK-Setup-x.y.z.exe`.
2. Run it. KUDUCK installs **for your user only**, so it doesn't ask for administrator rights.
3. Windows may show *“Windows protected your PC”* because the installer isn't code-signed.
   Click **More info → Run anyway**.

Requirements: Windows 10 (1809 or newer) or Windows 11, 64-bit.

Notes:
- **Word → PDF** uses Microsoft Word if it's installed, or the free LibreOffice.
- **Scan → Text** uses the text recognition built into Windows, in your Windows display languages.
- KUDUCK never asks for administrator rights. If you choose a protected folder (like Program Files)
  as the save location, pick another folder or run KUDUCK as administrator yourself.

## Publishing a new version

1. Change `<Version>` in `src/Kuduck/Kuduck.csproj` (for example `1.1.0`).
2. Commit and push to `main`.
3. GitHub Actions builds the installer. When no release exists yet for that version, it creates the `v` tag and publishes the installer on the Releases page after a few minutes. Pushes that keep the same version only check that the app compiles. (Publishing a release by hand on GitHub still works too: the installer is attached to it.)

To test an installer without publishing, open **Actions → Build → Run workflow** and download the
installer from the finished run.

## Build it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, for the installer,
Python 3.12 and [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
dotnet run --project src/Kuduck            # run the app

# full installer (same steps as .github/workflows/build.yml)
dotnet publish src/Kuduck/Kuduck.csproj -c Release -r win-x64 --self-contained true -o publish
pip install -r helper/requirements.txt pyinstaller
pyinstaller helper/kuduck_helper.py --name kuduck_helper --onedir --console --distpath build/helper
xcopy /e /i build\helper\kuduck_helper publish\helper
mkdir publish\Assets\models
curl -L -o publish\Assets\models\silueta.onnx https://github.com/danielgatis/rembg/releases/download/v0.0.0/silueta.onnx
iscc installer\kuduck.iss
```

The tests (`dotnet run --project tests/Kuduck.Tests`, any OS) make real images and PDFs and check resizing,
converting, merging, splitting, rearranging, locking (AES-256) and unlocking, and that results never overwrite
the originals.

## Project layout

```
src/Kuduck/          the Windows app (C#, WPF)
tests/Kuduck.Tests/  tests for the image and PDF engines
  Views/             duck, widget and tool pages
  Tools/             image, PDF, passport, OCR and conversion engines
  Services/          settings, history, start-with-Windows, updates, theme
helper/              PDF → Word / Excel converter (Python), bundled into the installer
installer/           Inno Setup script
tools/make_icon.py   regenerates the icon and logo
```

## Licence

KUDUCK is released under the [MIT licence](LICENSE). It uses open-source components listed in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

Created by Azeem.
