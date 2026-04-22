# Platform Notes

This document describes platform behavior that affects setup, UX, and release decisions.

## Native desktop and mobile

The native transport path supports:

- microphone capture
- remote audio playback through Unity `AudioSource`
- video publishing
- lip sync

### Mobile permission requirements

Your product flow must define:

- when microphone permission is requested
- what happens if permission is denied
- what happens on interruption or backgrounding

iOS requires `NSMicrophoneUsageDescription` in Player Settings.

## WebGL

WebGL works, but the browser path behaves differently from native.

### Browser constraints

- audio playback usually requires a user gesture
- microphone capture requires HTTPS or localhost
- remote audio does not route through Unity `AudioSource`
- vision publishing uses browser canvas capture, not Unity `RenderTexture` publishing

### Recommended UX for WebGL

- provide an explicit button that calls `EnableAudioAndStartListening()`
- do not rely only on a background scene click in UI-heavy scenes
- validate the build in the actual hosting environment, especially if embedded in an iframe

### Known issue

- WebGL currently has a known audio/lip-sync sync mismatch. Audio and lip-sync data arrive, but playback timing can drift in browser builds.

This is a defect, not an unsupported feature.

## Vision and camera privacy

If you enable vision:

- define consent timing and user messaging
- define retention and storage policy
- validate platform-specific permission behavior

See `SDK/Modules/Vision/README.md` for vision-specific setup.
