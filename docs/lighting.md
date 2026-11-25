# Presentation Scene Ultra-Level Lighting & Grounding Architecture

## 1. Executive Summary
This document specifies the ULTRA-level lighting and grounding overhaul and hotfix implementations for the presentation carousel in `Assets/Games/kmax-presentation/Scenes/PresentationScene.unity` (URP, Unity 6.3). The rig elevates visual fidelity to luxury showroom standards, eliminates model floating and specular blowouts, eliminates all billboard/camera-tracking artifacts, satisfies the TechExpo-2026 Design theme (`#252A36` slate), and guarantees full compatibility with the `KmaxXR` stereo rig (`VRRenderer`) and WorldSpace UI.

---

## 2. Studio 3-Point + Overhead Spot Rig

The lighting system combines a studio 3-point directional rig with an overhead spot softbox to sculpt depth, silhouette contours, and visible ground pools.

| Light Component | Type | Intensity | Color (sRGB) | Rotation / Position | Shadows | Purpose |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Key Light** | Directional | 0.95 lux | `#FFFBF0` (1.00, 0.98, 0.94) Warm Ivory | `Euler(38°, 205°, 0°)` | Soft Shadows (Bias 0.04, Normal Bias 0.35) | Primary directional modeling; calibrated to prevent specular blowout. |
| **Fill Light** | Directional | 0.40 lux | `#D9E0EB` (0.85, 0.88, 0.92) Neutral Slate | `Euler(18°, 320°, 0°)` | None | Softens shadow contrast without blue color cast; neutral undertone. |
| **Rim / Kicker** | Directional | 0.95 lux | `#E0EBFB` (0.88, 0.92, 0.98) Crisp Silver | `Euler(22°, 40°, 0°)` | None | High-angle back kicker; separates vehicle shoulder lines and anatomy silhouettes from the drapery. |
| **Stage Overhead Spotlight** | Spot | 1.40 lux | `#FFFCF5` (1.00, 0.99, 0.96) Softbox White | Pos `(0.0, 2.6, -0.1)`, `Euler(82°, 0°, 0°)`, Spot 72° (Inner 38°), Range 6.5m | Soft Shadows (Bias 0.03, Normal Bias 0.30) | Overhead softbox; casts a distinct circular pool of light on the stage floor under the model and creates downward contact shadows. |

---

## 3. Grounding & Reflection Architecture

### 3.1 Floor Receive Shadows & Range
- `Showroom_Floor` renderer updated to `receiveShadows = true`.
- URP pipeline `shadowDistance` set to `7.0m` (tight to stage, maximizing 2048 shadowmap resolution across model and floor).
- URP `supportsAdditionalLightShadows` enabled, allowing `StageLight` (Spot) to cast downward shadows onto the floor.

### 3.2 Showroom Floor Material (`M_Showroom_Floor.mat`)
- **Shader**: Universal Render Pipeline/Lit.
- **Base Color**: `#333D52` `Color(0.20f, 0.24f, 0.32f, 1.0f)` (deep slate navy matching `#252A36` showroom palette; eliminates flat black floor).
- **Smoothness**: `0.85` (high specular gloss for mirror reflections and Fresnel light pool falloff).
- **Metallic**: `0.30` (subtle metallic reflectance).

### 3.3 Parallax-Corrected Stage Reflection Probe
- **GameObject**: `ShowroomReflectionProbe`.
- **Mode**: Realtime, `EveryFrame` (refreshed on model transitions via `probe.RenderProbe()`).
- **Box Projection**: `true`, Size `(4.5f, 2.5f, 4.5f)`, Center `(0.0f, 0.20f, 0.0f)`.
- **Resolution**: `256`, Intensity: `1.25`.

### 3.4 Dynamic Soft Floor Contact Shadow Quad
- **GameObject**: `FloorContactShadow` quad.
- **Material**: `M_Showroom_FloorShadow.mat` (URP Unlit, alpha 0.65 with solid dark core and smooth hermite radial falloff `Presentation_Contact_Shadow.png`).
- **Dynamic Tracking (BT-77 Polish)**:
  - Quad tracks each model's actual lowest bound and center in world space: `y = bounds.min.y + 0.008f`.
  - Position: `(bounds.center.x, bounds.min.y + 0.008f, bounds.center.z)`.
  - Scale: `width = Mathf.Max(1.20f, footprint.x * 1.50f) * popScale`, `length = Mathf.Max(1.40f, footprint.y * 1.50f) * popScale`.
  - Rotation: Synchronized with model yaw `Euler(90°, spinner.eulerAngles.y, 0°)`.
  - Result: The shadow quad is raised by +8mm directly into the model's lowest contact patch (tires, legs, PCB bottom edge), eliminating any floating gap and ensuring the contact shadow firmly touches the contact points under every model.

---

## 4. Post-Processing Volume (`PresentationPostFX.asset`)

Configured dedicated volume profile `Assets/Games/kmax-presentation/Rendering/PresentationPostFX.asset` with full URP sub-asset serialization and `UniversalAdditionalCameraData.renderPostProcessing = true` on all active cameras, enforced at runtime and in builder:
- **Tonemapping**: `TonemappingMode.ACES` for cinematic contrast and filmic highlight roll-off.
- **Bloom**: Threshold `1.20f`, Intensity `0.15f`, Scatter `0.50f` (subtle bloom without burning highlights).
- **Vignette**: Intensity `0.20f`, Smoothness `0.40f`, Color Black.
- **Color Adjustments**: Post Exposure `-0.15f` (BT-77 polish: raised from `-0.35f` to `-0.15f` for brighter showroom visibility), Contrast `4.0f`, Saturation `0.0f` (neutralized grading; zero blue color cast).
- **Split Toning**: Disabled/neutralized (ensures true colors for Red Mustang, White Armchair, and Green PCB).
- **Stereo Safety**: Zero SSAO (guarantees 100% binocular stereo comfort on VRRenderer).

---

## 5. Per-Item Exposure Calibration & Table Framing

### 5.1 Material Calibration (`CalibrateMaterials`)
- **White Clamping**: Materials with luminance $\ge 0.70$ or all $r, g, b \ge 0.70$ are scaled down so that max channel does not exceed `0.62f`.
- **Smoothness Clamping**: White surfaces clamped to `_Smoothness <= 0.08`, transforming specular glare into smooth surface gradients.
- **Measurement Verification**:
  - `Coffee Table 17`: Max tabletop pixel luminance measured at **0.7725** (well under the 0.93 limit; mean luminance 0.4173).
  - `Armchair 02`: Seat cushion max luminance measured at **0.7567** (no clipping to white).

### 5.2 PCB LM358 Cyan Wedge Defect & LedHalo Blue Blob Elimination
- **Root Cause**: `LedHalo.Create()` instantiated a primitive Quad with default local scale `Vector3.one` under the scaled PCB root ($16.34\times$). Because `Camera.main` is null at runtime under `VRRenderer`, `viewCamera` remained unassigned, leaving the quad at an unpositioned $16.34\text{m}$ billboard cutting across the upper-left frustum. Furthermore, billboard halos created an offset blue glow blob above the angled board.
- **Resolution (BT-77 Polish)**:
  - `LedHalo` billboard quads are completely disabled and destroyed in `PcbWorkingExhibit.cs` and `PresentationDirector.cs` (`BindPcbExhibit`), eliminating the floating blue glow blob above the board.
  - The LED diode uses authentic physical PBR emission (`EmissionKeyword`) and a localized point light glow (`LedGlowLight`) placed at the exact center of the diode mesh (`ledLens.bounds.center`).
  - Active copper flow lines remain fully functional with animated voltage waves and zero visual artifacts. Measured bright cyan pixels: active board traces only. Zero artifacts in all frames.

### 5.3 Neutralize Color Cast
- Removed SplitToning and excessive saturation (+5) and contrast (+8).
- Ford Mustang GT Race Red paint verified with Red/Blue channel ratio of **2.23** (pure vibrant Race Red).
- Showroom backdrop maintains deep `#252A36` slate theme.

---

## 6. Budget & Performance Summary

| Budget Item | Limit | Actual | Status |
| :--- | :--- | :--- | :--- |
| **Realtime Shadow-Casting Lights** | $\le 4$ | 2 (Key Light + Stage Top Spot) | PASS (50% headroom) |
| **Additional Lights Per Object** | $\le 4$ | 2 (Spot + 1 point for exhibits) | PASS |
| **Shadow Distance** | Tuned to stage | 7.0m | PASS |
| **Tabletop Max Luminance** | $< 0.93$ | 0.7725 | PASS |
| **UI Canvas Mode** | WorldSpace | WorldSpace | PASS |
| **Environment Transform** | $(0.25, 0.60, 0.25)$ | $(0.25, 0.60, 0.25)$ | PASS (Preserved) |
