# Hunter Hackslash Native

Native Windows 2D hack-and-slash action RPG prototype.

## Current Main Build

Use the native C# project:

```text
native/HunterHackslashNative/HunterHackslashNative.csproj
```

The old web prototype, packaged builds, browser screenshots, runtime downloads, published zip files, and generated deluxe sprite sheets are intentionally ignored by Git.

## Run From Source

Install .NET 9 SDK, then run:

```powershell
dotnet run --project native/HunterHackslashNative/HunterHackslashNative.csproj -c Release
```

Or double-click:

```text
START_HUNTER_HACKSLASH.bat
```

If generated deluxe assets are missing, the game still runs with the built-in fallback renderer. Generate the deluxe sheets locally for the current full visual pass.

## Generate Local Assets

The repository keeps source inputs and the generator script, not the huge generated outputs. Install Pillow, then run:

```powershell
python -m pip install pillow
python tools/generate_deluxe_v1_assets.py
```

This creates local generated outputs under:

```text
assets/characters/action8/deluxe_v1
assets/enemies/action8/deluxe_v1
assets/illustrations/deluxe_v1
```

Those folders are ignored by Git. Do not commit them; use GitHub Releases for packaged builds or large playable archives.

## Controls

- Move: WASD / Arrow Keys
- Light attack: J
- Heavy attack: K
- Dash: Space
- Skills: Q / E / R
- Ultimate: F
- Hitbox debug: F3

## Kept Runtime Assets

The current native build loads generated assets from:

```text
assets/characters/action8/deluxe_v1
assets/enemies/action8/deluxe_v1
assets/illustrations/deluxe_v1
data/attacks
```

The source inputs for regenerating those assets are:

```text
assets/characters/action8/pose_v6
assets/enemies/action8/*.png
tools/generate_deluxe_v1_assets.py
```

Everything else under old generated asset passes is ignored to keep the repository manageable.

## GitHub Desktop

Add this repository root:

```text
D:\codex-projects\hunter-hackslash-playable
```

Do not add `native/dist` or `native/HunterHackslashNative` as the root.
