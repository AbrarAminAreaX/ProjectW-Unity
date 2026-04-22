# Release Capability Matrix

This file separates three things:

- what the SDK is designed to support
- what this repo validates automatically
- what is currently known to be limited

## Designed platform behavior

| Capability | Native | WebGL |
|---|---|---|
| Audio conversation | Yes | Yes |
| Microphone capture | Yes | Yes |
| Remote audio playback | Unity `AudioSource` | Browser-routed playback |
| User gesture required for audio | No | Yes |
| Video publishing | Yes | Yes |
| Spatial audio | Yes | No |
| Built-in transcript/settings/notification UI | Yes | Yes |

## Current validation sources

| Validation Type | Status |
|---|---|
| Editor compile and focused runtime tests | Repo-owned |
| EditMode platform smoke coverage | Repo-owned |
| Native runtime behavior | Manually validated in maintained sample/runtime testing |
| WebGL runtime behavior | Manually validated in browser builds |

## Known limitations

| Limitation | Platform | Status |
|---|---|---|
| Audio and lip-sync timing can drift | WebGL | Known defect |
| Remote audio is not routed through Unity `AudioSource` | WebGL | By design |
| Spatial audio | WebGL | Unsupported |

## Runtime hosting assumptions

- One `ConvaiManager` per scene is the intended host
- `ConvaiRoomManager` remains the managed room/session component under that host
- `ConvaiCharacter` and `ConvaiPlayer` are scene-owned agents

## Release interpretation

The SDK is release-capable on native and WebGL paths, with the current WebGL audio/lip-sync timing issue documented as a known limitation rather than an unsupported capability.
