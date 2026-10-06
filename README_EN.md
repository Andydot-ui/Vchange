<!--markdownlint-disable MD033 MD041-->

<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/logo-dark-theme.png">
  <img src="docs/images/logo-light-theme.png" width="72" height="72" alt="Vchange Logo">
</picture>

# Vchange

**Video / Image Converter, Timelapse & Long-Exposure Stacking for Windows · WPF + ffmpeg**

A 4-in-1 tool — video conversion, image conversion, timelapse assembly and long-exposure stacking — with a guided, single-file, portable UI that works out of the box.

中文文档: [**README.md**](README.md)

[![Latest Release](https://img.shields.io/github/v/release/Andydot-ui/Vchange?style=flat-square&color=%233fb950&label=latest%20release)](https://github.com/Andydot-ui/Vchange/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Andydot-ui/Vchange/total?style=flat-square&color=%230a84ff&label=downloads)](https://github.com/Andydot-ui/Vchange/releases)
[![Stars](https://img.shields.io/github/stars/Andydot-ui/Vchange?style=flat-square&label=Stars)](https://github.com/Andydot-ui/Vchange)
[![Repo Size](https://img.shields.io/github/repo-size/Andydot-ui/Vchange?style=flat-square&color=3cb371)](https://github.com/Andydot-ui/Vchange)
<br/>
![.NET](https://img.shields.io/badge/.NET-8-512bd4?style=flat-square)
[![License](https://img.shields.io/github/license/Andydot-ui/Vchange?style=flat-square)](LICENSE)
[![Issues](https://img.shields.io/github/issues/Andydot-ui/Vchange?style=flat-square&color=%233fb950&label=Issues)](https://github.com/Andydot-ui/Vchange/issues)

[**⬇ Download now**](https://github.com/Andydot-ui/Vchange/releases/latest) | [**🐛 Report a bug**](https://github.com/Andydot-ui/Vchange/issues)

</div>

## ✨ Features

### 🏠 Four tools, one launcher

- [x] On startup, pick the tool you need: **Video Converter / Timelapse / Image Stacking / Image Converter**
- [x] All four share the same guided wizard layout and parameter logic; go back a step at any time
- [x] Smooth cross-fade page transitions and highlight animations on the selected card

### 🎬 Video conversion

- [x] Step-by-step flow: **Pick file → Output format → Video params → Bitrate → Convert**
- [x] **21 output formats** (mp4 / mkv / avi / mov / webm / gif / animated apng …)
- [x] Codec, resolution and frame rate can each be kept **same as the source video**
- [x] Bitrate is **recommended from the resolution** and can be edited manually; resolution presets up to 8K plus custom width × height
- [x] Hardware-accelerated encoders: `h264_nvenc` / `h264_qsv` / `h264_amf` / HEVC / AV1
- [x] **Batch conversion**: multi-select files in the picker; for multiple files choose an output folder and convert sequentially with `i/N` progress, per-file log lines and automatic numbering of name clashes

### ⏱ Timelapse assembly

- [x] Renames an image sequence to **zero-padded names automatically** (01, 02, 03 …, padding width adapts to the file count)
- [x] Two strategies: **copy into a new `rename` folder** / **rename in place**; when disk space is too low it automatically switches to in-place renaming and locks the option
- [x] Separate **rename and encode progress bars, stacked one above the other** — renaming always happens first
- [x] Encoding options mirror the video converter (frame rate / format / codec / recommended bitrate / resolution up to the source image size)
- [x] When finished, the video **opens in your default player**; after you confirm it looks right, the `rename` folder is cleaned up automatically

### 🌌 Image stacking (long exposure)

- [x] Stack star trails, waterfalls and traffic lights with **max / average / min** blending
- [x] Outputs **JPG / PNG / BMP / TIFF / DNG** (16-bit linear DNG, written by pure C#)
- [x] JPEG quality slider plus output resolution (same as source / presets / custom)
- [x] Live stacking progress bar and stacking log
- [x] Produces **EXIF-free** images

### 🖼 Image conversion

- [x] Image conversion (multi-select **batch conversion** supported): **JPG / PNG / BMP / TIFF / WEBP / WMP / DNG**
- [x] In batch mode pick an output folder and convert one by one with `i/N` progress; name clashes get automatic numbering
- [x] Tunables: JPG / WebP quality slider, TIFF compression (LZW / none / Zip / RLE / CCITT G4), PNG interlacing, output resolution
- [x] Defaults to *match the source image* — pixels untouched
- [x] Animated images (GIF / APNG) are handled by the video converter

### 🎨 Modern UI

- [x] Light / dark theme **follows the system**
- [x] Windows 11 acrylic blur window
- [x] Custom frameless window, rounded cards, toggle switches and a styled quality slider
- [x] Live progress bar (percentage + speed) with the raw ffmpeg log
- [x] Remembers the window position between sessions

### 📦 Works out of the box

- [x] **Single-file exe** — no .NET runtime installation needed (.NET 8 is embedded)
- [x] **Full ffmpeg 9.0.2 build embedded** — no separate download
- [x] App icon switches with the system light/dark theme (white / black clapperboard)

## 📷 Screenshots

> The screenshots below show the **video conversion** flow; timelapse, image stacking and image conversion use the same guided layout.

### Main flow

##### 1. Pick a video file

![Pick file](docs/screenshots/1-file.png)

##### 2. Choose an output format

![Output format](docs/screenshots/2-format.png)

##### 3. Set video parameters

![Video params](docs/screenshots/3-params.png)

##### 4. Set the bitrate

![Bitrate](docs/screenshots/4-bitrate.png)

##### 5. Review the summary

![Summary](docs/screenshots/5-summary.png)

##### 6. Conversion complete (progress & log)

![Done](docs/screenshots/6-done.png)

## 🚀 Getting started

**Requirements**: Windows 10 / 11 (64-bit)

1. Open the [**Releases page**](https://github.com/Andydot-ui/Vchange/releases/latest)
2. Download and run `Vchange.exe`
3. Pick a tool on the home screen and follow the wizard

> [!TIP]
> Slow GitHub download? Use the high-speed mirror: [**123 Cloud · Vchange.exe**](https://1828395166.share.123pan.cn/123pan/DA7Sjv-Xs0dv)
>
> [!IMPORTANT]
> **The high-speed link only works for the latest release**: whenever a new version is published, the mirror link of older versions stops working. Always take the link from this README or the [project site](https://andydot-ui.github.io/Vchange/); older versions are available on the [Releases](https://github.com/Andydot-ui/Vchange/releases) page.

> [!NOTE]
> The embedded ffmpeg is extracted on first use (a few seconds) and reused afterwards.
> No .NET runtime and no separate ffmpeg setup required.

### Download files

| File | Purpose |
| --- | --- |
| `Vchange.exe` (~372 MB) | **Main app (recommended)** — single-file portable build with the .NET 8 runtime and the full ffmpeg embedded; double-click and go |
| `ffmpeg-9.0.2-full.exe` (~217 MB) | The full ffmpeg command-line tool; not needed for normal use — handy if you want ffmpeg on its own or to replace the embedded build |

## 🔧 Building from source

1. Install Visual Studio 2022 / the .NET 8 SDK
2. Put a full `ffmpeg.exe` into the `Resources\` folder (only embedded in Release builds)
3. Run:

```bash
dotnet publish Vchange.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

4. The output lands in `bin\Release\net8.0-windows\win-x64\publish\Vchange.exe`

> If a nested legacy `Vchange\` copy exists in the repository root, it is excluded in `Vchange.csproj` and never compiled.

## 📁 Project structure

```
Vchange/
├─ Vchange.csproj                 # Project file (.NET 8.0-windows + WPF)
├─ App.xaml / App.xaml.cs         # App entry & global styles (theme resources)
├─ MainWindow.xaml / .xaml.cs     # Main window & business logic (home + four flows)
├─ MessageDialog.xaml / .xaml.cs  # Custom message dialog (with confirm mode)
├─ FfmpegProvider.cs              # ffmpeg discovery (external / PATH / embedded extraction)
├─ ThemeManager.cs                # Light/dark theme management
├─ RenameEngine.cs                # Natural sort, zero-padded renaming, disk-space checks
├─ TimelapseFlow.cs               # Timelapse flow (rename + assemble)
├─ ImageStacker.cs                # Long-exposure stacking engine (max / average / min)
├─ StackingFlow.cs                # Image stacking flow
├─ ImageConverter.cs              # Image conversion & color-space remapping engine
├─ ImageConvertFlow.cs            # Image conversion flow
├─ TiffWriter.cs                  # TIFF encoder (pure C#)
├─ DngWriter.cs                   # 16-bit linear DNG writer (no EXIF)
├─ Vchange.slnx                   # Solution file
├─ Resources/
│   ├─ app_white.ico              # Dark-theme icon
│   └─ app_dark.ico               # Light-theme icon
├─ tools/
│   ├─ make-icon.ps1              # App icon generator
│   └─ take-screenshots.ps1       # UI screenshot automation
└─ docs/                          # Website (GitHub Pages) & screenshot assets
   ├─ index.html                  # Product site (Apple-style, pure static)
   └─ screenshots/                # UI screenshots
```

## ❓ FAQ

<details>
<summary><b>What's new in v1.2.1?</b></summary>

Fixes the save dialogs in all four workflows so suggested filenames are prefilled and the source folder is preferred as the starting location. Also removes the image converter's confusing color-space options. The batch conversion and DNG-folder support introduced in v1.2.0 are included.
</details>

<details>
<summary><b>First run warns about an "unknown publisher"?</b></summary>

Expected: the program has no commercial code-signing certificate, so Windows asks for confirmation on unknown publishers — it is **not a virus false positive**. Click **More info → Run anyway** (blue dialog) or **Run** (gray dialog). If the app exits immediately complaining about extraction, move the exe to an **ASCII path** (e.g. your Downloads folder) and run it there.
</details>

<details>
<summary><b>Conversion failed — what now?</b></summary>

Check the raw ffmpeg output in the *Conversion log*; it usually states the exact cause (corrupted source, unsupported encoder, …).
</details>

<details>
<summary><b>Why is the first conversion slower?</b></summary>

The first run extracts the embedded ffmpeg (~217 MB) to `%LocalAppData%\Vchange\ffmpeg.exe`. It happens once; afterwards it is reused instantly.
</details>

<details>
<summary><b>Is hardware acceleration supported?</b></summary>

Yes. Pick `h264_nvenc` (NVIDIA), `h264_qsv` (Intel) or `h264_amf` (AMD) in the codec dropdown; HEVC and AV1 hardware encoders are available too.
</details>

<details>
<summary><b>Do stacking/conversion write EXIF metadata?</b></summary>

No. Stacking and image conversion produce **EXIF-free** images (the 16-bit linear DNG is EXIF-free as well). Keep the originals if you need the capture metadata.
</details>

<details>
<summary><b>The high-speed download link doesn't work / isn't the newest version?</b></summary>

The 123 Cloud mirror link **only stays valid for the latest release**; older links expire when a new version ships. Copy the fresh link from this README or the [project site](https://andydot-ui.github.io/Vchange/), or download from [Releases](https://github.com/Andydot-ui/Vchange/releases).
</details>

## 🙋 Feedback & support

Found a problem or have an idea? Issues are welcome (please follow the templates — the more detail, the faster the fix):

- [🐛 Report a bug](https://github.com/Andydot-ui/Vchange/issues/new?template=BugReport.yml)
- [💡 Request a feature](https://github.com/Andydot-ui/Vchange/issues/new?template=FeatureRequest.yml)
- [📋 Browse existing issues](https://github.com/Andydot-ui/Vchange/issues)
- 📮 Email: **andydot@qq.com**

## ⭐ Star History

<a href="https://www.star-history.com/?repos=andydot-ui%2Fvchange&type=date&releases=&legend=bottom-right">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=andydot-ui/vchange&type=date&theme=dark&legend=bottom-right" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=andydot-ui/vchange&type=date&legend=bottom-right" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=andydot-ui/vchange&type=date&legend=bottom-right" />
 </picture>
</a>

## 📄 License

Licensed under the [GNU General Public License v3.0 (GPL-3.0)](LICENSE).

<div align="center">

If this project helps you, please give it a ⭐ Star!

</div>
