# Quest LEGO Color Finder (Unity / Meta Quest Standalone)

Native Unity app for Meta Quest 3 / Quest 3S that starts in Passthrough and helps find LEGO parts by color.

It implements both required paths:

- `Basic Mode (Styling)` = passthrough layer styling only (no raw camera frames)
- `Advanced Mode (Camera Mask)` = camera-texture color thresholding (HSV mask + overlay)

At runtime the app selects the best mode automatically:

- If camera permission + PCA source is available -> `Advanced`
- Otherwise -> `Basic`

## Reality Check / Architecture

This project intentionally uses a runtime-generated scene setup (`AppBootstrap`) so the `Main.unity` scene can stay minimal and stable:

- Creates XR camera rig root (if none exists)
- Enables Meta passthrough (reflection bridge to `OVRManager` / `OVRPassthroughLayer`)
- Builds world-space UI attached to the camera
- Creates advanced overlay quad + GPU shader pipeline

This keeps the project resilient across Meta SDK package revisions while still using the requested Meta APIs when present.

## Unity + Package Versions (Pinned in This Project)

- Unity Editor: `2022.3.62f1` (`ProjectSettings/ProjectVersion.txt`)
- Meta XR All-in-One SDK (UPM): `com.meta.xr.sdk.all` `74.0.0`
- Meta MR Utility Kit (UPM): `com.meta.xr.mrutilitykit` `74.0.0`
- XR Management: `com.unity.xr.management` `4.5.0`
- OpenXR: `com.unity.xr.openxr` `1.11.1`

Notes:

- The Meta package versions are pinned in `Packages/manifest.json`.
- If your Meta registry no longer serves `74.0.0`, install the closest matching major version for both Meta packages and re-open the project.

## Features Implemented

- Passthrough enabled by default
- Runtime auto mode selection (`Basic` vs `Advanced`)
- In-headset world-space UI:
  - Color buttons: Red / Blue / Yellow / Green / Black / White
  - Style toggle: `B/W except target` and `Glow overlay`
  - Mode indicator
  - Status messages
  - Advanced settings panel (HSV tolerance sliders)
  - Big `Calibrate` button (center sample)
- Advanced color detection path:
  - GPU mask generation (`ColorMask.shader`, HSV threshold)
  - GPU composite overlay (`Composite.shader`)
  - One-shot CPU calibration readback (button-triggered only)
- Basic passthrough styling fallback:
  - Edge rendering (best effort via reflection)
  - Contrast/brightness/posterize/tint approximation
- Android build tooling:
  - Unity editor build menu
  - Batchmode build script (`BuildQuestAPK.ps1`)
  - VS Code task (`.vscode/tasks.json`)

## Project Layout

Key files:

- `Assets/Scenes/Main.unity` (main scene; runtime bootstrap creates rig/UI/overlay)
- `Assets/Scripts/AppBootstrap.cs`
- `Assets/Scripts/PassthroughBasicStylingController.cs`
- `Assets/Scripts/PassthroughCameraMaskController.cs`
- `Assets/Scripts/ColorSelectionUI.cs`
- `Assets/Shaders/ColorMask.shader`
- `Assets/Shaders/Composite.shader`
- `Assets/Editor/BuildQuestAPK.cs`
- `Assets/Plugins/Android/AndroidManifest.xml`
- `BuildQuestAPK.ps1`

## Install -> Open -> Play -> Build -> Deploy

### 1) Prerequisites

- Unity Hub + Unity `2022.3.62f1` with Android Build Support:
  - Android SDK/NDK
  - OpenJDK
- Meta Quest Developer Mode enabled
- `adb` available in PATH (optional but recommended)
- Meta UPM registry access configured for the account used in Unity Package Manager (required for Meta packages)

### 2) Open the Project

1. Open this folder in Unity Hub (`Add project from disk`).
2. Launch with Unity `2022.3.62f1`.
3. Let Package Manager resolve dependencies from `Packages/manifest.json`.
4. Open `Assets/Scenes/Main.unity`.

### 3) One-Time Unity Configuration (Important)

The build script applies many Android settings automatically, but you still need to verify XR features in the Unity editor:

1. `Edit > Project Settings > XR Plug-in Management`
2. Android tab:
   - Enable `OpenXR`
3. `Project Settings > OpenXR`
   - Enable Meta Quest/OpenXR features required by your installed Meta package version
4. Import/install Meta XR All-in-One SDK + MRUK if Package Manager prompts for auth or updates

### 4) Play / Test in Editor

- Press Play.
- UI appears in front of the camera.
- Basic mode is used by default.
- In Editor, Advanced mode may use webcam fallback (`WebCamTexture`) if available.

### 5) Build (Manual in Unity)

1. `Tools > Quest Color Finder > Apply Recommended Project Settings`
2. `Tools > Quest Color Finder > Build APK`
3. Choose an output path (e.g. `Builds/QuestColorFinder.apk`)

### 6) Build (One Command / VS Code)

#### PowerShell (recommended)

```powershell
.\BuildQuestAPK.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe"
```

Optional:

```powershell
.\BuildQuestAPK.ps1 `
  -UnityPath "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe" `
  -OutputPath ".\Builds\QuestColorFinder.apk" `
  -LogFile ".\Builds\UnityBuild.log"
```

#### VS Code Task

- Set env var `UNITY_EXE` to your Unity editor executable path
- Run task: `Unity: Build Quest APK`

### 7) Deploy to Headset

```powershell
adb install -r .\Builds\QuestColorFinder.apk
```

Launch the app on the Quest.

## Runtime Behavior (On Headset)

### Basic Mode (Styling)

Used when camera permission/PCA is unavailable.

- Uses passthrough layer styling only
- Improves visibility via contrast/posterize/edge/tint (approximation)
- Not true color segmentation

### Advanced Mode (Camera Mask)

Used when camera permission is granted and a PCA camera texture source is available.

- Reads camera texture
- Converts RGB -> HSV in shader
- Thresholds selected color
- Style 1: grayscale outside mask, target remains highlighted
- Style 2: glow/outline overlay on mask

### Calibration

- Press `CALIBRATE (CENTER SAMPLE)`
- Look at the target LEGO part color in the center of view
- The app samples the center region once and updates target HSV

## Privacy / Permissions

- Requests camera permissions at runtime for Advanced mode
- Falls back to Basic mode if denied
- No camera frames are saved to disk
- No network upload/transmission is implemented

## Troubleshooting Checklist

### Packages fail to resolve

- Confirm Meta UPM registry access/auth is configured
- Open `Window > Package Manager` and retry
- If `74.0.0` is unavailable, use a matching newer Meta package version for:
  - `com.meta.xr.sdk.all`
  - `com.meta.xr.mrutilitykit`

### Passthrough does not start on Quest

- Verify Meta XR SDK is installed (OVR/Meta passthrough components available)
- Verify Android `OpenXR` is enabled
- Confirm Quest app is running standalone on headset (not desktop Game view expectations)
- Check app logs for missing `OVRManager` / `OVRPassthroughLayer` types

### Advanced mode never activates

- Grant camera permission in-headset when prompted
- If previously denied, remove app or re-enable permission in system settings
- Confirm MRUK is installed
- Confirm your MRUK version exposes `PassthroughCameraAccess`
- Check logs for:
  - "component type not found"
  - "no camera texture became available"

### UI is visible but not clickable in-headset

- Ensure your project includes the Meta XR controller/hand UI ray setup used by your SDK version
- The script will add `OVRInputModule` / `OVRRaycaster` automatically when available, but you still need a pointer/ray interactor setup from Meta XR if your template does not provide one

### Advanced overlay looks offset / misaligned

- The project uses a view-locked overlay quad for simplicity and robustness
- Alignment may vary by PCA source/intrinsics/distortion for some SDK versions
- For production-grade alignment, extend `PassthroughCameraMaskController` to use MRUK camera intrinsics/extrinsics and any provided UV transform

### Build fails in batchmode

- Install Android Build Support in Unity Hub
- Confirm `-UnityPath` points to the actual `Unity.exe`
- Check `Builds/UnityBuild.log`
- Open Unity once interactively to let packages finish importing before batch build

## Implementation Notes for Extension

- `Assets/Scripts/PassthroughCameraMaskController.cs` is intentionally reflection-based for MRUK PCA integration, so it stays resilient across minor API changes.
- If you know the exact MRUK version/API, replace the reflection adapter with direct typed calls for better reliability and performance.
- `Assets/Scripts/PassthroughBasicStylingController.cs` also uses reflection to support differing passthrough property names across Meta SDK versions.
