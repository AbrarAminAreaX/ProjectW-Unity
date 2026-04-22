# Project Settings Reference

Open: `Edit > Project Settings > Convai SDK`

`ConvaiSettings` stores project-wide defaults. It is not the same as `Room Manager Profile` or `Character Profile` assets.

Use the layers like this:

- `ConvaiSettings`: project defaults
- `Room Manager Profile` / `Character Profile`: reusable authoring assets
- component `Inline` values: scene-local values

Room and character components make the active source explicit. `ConvaiRoomManager` and `ConvaiCharacter` can each use scene-local values or an assigned reusable asset.

## API Configuration

### API Key

Required for the default runtime credential flow.

If this is missing or invalid, room/session connections will fail.

### Realtime Server URL

Default realtime endpoint used by the SDK.

Only change this if your environment requires a non-default backend.

## Player Defaults

### Player Name

Default local player display name used by built-in transcript flows and sample UI.

## Room Manager Profile and Character Profile

These are optional reusable assets.

### Room Manager Profile

`ConvaiRoomManagerProfile` is the reusable **Room Manager Profile** asset.

Use it when you want shared defaults for:

- connection type
- video track name
- LLM provider
- server endpoint
- connect on start
- reconnect policy

### Character Profile

`ConvaiCharacterProfile` is the reusable **Character Profile** asset.

Use it when you want shared defaults for:

- character id
- character name
- name tag color
- remote audio on start
- session resume on reconnect

If a component is in `Asset` mode, the assigned asset is the active source of truth. There are no mixed per-field overrides in that mode.

## Audio

### Default Microphone Index

Fallback microphone index when no runtime override exists.

### Connection Timeout

Project default timeout for connection-related operations.

## Logging

### Global Log Level

Controls Unity Console verbosity.

Recommended:

- `Info` while integrating
- `Debug` only when actively diagnosing an issue

### Include Stack Traces

Useful while debugging, noisy during normal use.

### Colored Output

Adds Unity Console color tags where supported.

### Category Overrides

Use when you need one subsystem to be noisier than the rest.

## Features

### Transcript System

Project default for the built-in transcript routing/presentation path.

### Notification System

Project default for built-in user-facing notification requests.

### Session Resume

Project-level intent only. Actual resume behavior is still resolved per character and at runtime.

## Vision Defaults

These defaults support built-in frame sources such as `CameraVisionFrameSource`.

### Enable Vision

Project-level intent flag. It does not auto-add or remove vision components.

### Capture Resolution / Frame Rate

Used when built-in frame sources are configured to inherit project defaults.

### JPEG Quality

Only applies to JPEG-based frame paths. It does not control WebRTC transport encoding.
