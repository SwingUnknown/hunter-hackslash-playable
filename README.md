# Hunter Hackslash Native

Native Windows 2D hack-and-slash action RPG prototype.

## Current Main Build

Use the native C# project:

```text
native/HunterHackslashNative/HunterHackslashNative.csproj
```

The old web prototype, packaged builds, browser screenshots, runtime downloads, and published zip files are intentionally ignored by Git.

## Run From Source

Install .NET 9 SDK, then run:

```powershell
dotnet run --project native/HunterHackslashNative/HunterHackslashNative.csproj -c Release
```

Or double-click:

```text
START_HUNTER_HACKSLASH.bat
```

## Controls

- Move: WASD / Arrow Keys
- Light attack: J
- Heavy attack: K
- Dash: Space
- Skills: Q / E / R
- Ultimate: F
- Hitbox debug: F3

## Kept Runtime Assets

The current native build uses:

```text
assets/characters/action8/deluxe_v1
assets/enemies/action8/deluxe_v1
assets/illustrations/deluxe_v1
data/attacks
```

Everything else under old generated asset passes is ignored to keep the repository manageable.
