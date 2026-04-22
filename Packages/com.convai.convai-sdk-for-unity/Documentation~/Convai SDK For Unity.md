# About Convai SDK For Unity

The Convai SDK for Unity adds realtime conversational AI characters to Unity projects. The package includes scene-level runtime hosting, character and player components, transcript/UI support, optional modules such as LipSync, Vision, and Narrative, and platform-specific networking for native and WebGL builds.

## Installation

Install the package through the Unity Package Manager.

After installation:

1. Open `Edit > Project Settings > Convai SDK` and set your API key
2. Use `GameObject > Convai > Setup Required Components` in your scene
3. Import the samples from Package Manager if you want a known-good reference scene

## Core components

| Component | Purpose |
|---|---|
| `ConvaiManager` | Scene entrypoint and runtime host |
| `ConvaiCharacter` | Per-character conversation component |
| `ConvaiPlayer` | Player identity and input component |
| `ConvaiAudioOutput` | Native-platform character audio playback helper |

Advanced/internal:

| Component | Purpose |
|---|---|
| `ConvaiRoomManager` | Managed room/session orchestration under `ConvaiManager` |

## Modules

- `ConvaiLipSyncComponent`
- `ConvaiVisionPublisher`
- `ConvaiNarrativeDesignManager`

## Documentation

- `README.md`
- `SETUP.md`
- `API-ENTRYPOINTS.md`
- `WORKING-WITH-EVENTS.md`
- `PROJECT-SETTINGS.md`
- `PLATFORMS.md`
- `TROUBLESHOOTING.md`

## Known limitations

- WebGL currently has a known audio/lip-sync timing mismatch.
- WebGL audio playback requires browser gesture-aware UX.
- WebGL audio is browser-routed rather than Unity `AudioSource` routed.
