# Convai SDK Docs

Use these docs in this order:

1. `SETUP.md`
2. `API-ENTRYPOINTS.md`
3. `WORKING-WITH-EVENTS.md`
4. `PROJECT-SETTINGS.md`
5. `PLATFORMS.md`
6. `TROUBLESHOOTING.md`

Reference docs:

- `RELEASE-CAPABILITY-MATRIX.md` for supported vs verified behavior
- `Documentation~/Architecture/ARCHITECTURE.md` for architecture and contracts
- `Documentation~/Architecture/ROADMAP.md` for the current hardening backlog

## Recommended integration path

- Add one `ConvaiManager` to the scene
- Add `ConvaiPlayer` to the player object
- Add `ConvaiCharacter` to each character
- Add `ConvaiAudioOutput` to characters that should play voice through Unity audio
- Configure your API key in `Edit > Project Settings > Convai SDK`

For first validation, start with the Basic Sample. It is the lowest-friction path and does not require URP.

For runtime event wiring, read `WORKING-WITH-EVENTS.md` after `API-ENTRYPOINTS.md`.

## Configuration model

Room and character components now use an explicit configuration source:

- `Inline` for component-local values
- `Asset` for reusable `Room Manager Profile` / `Character Profile` assets

`Inline` is the default and is enough for most scenes. Use `Asset` only when you want reusable defaults across scenes or prefabs.

## Known issues

- WebGL currently has a known audio/lip-sync sync mismatch. Audio and lip-sync data arrive, but playback timing can drift in browser builds.
- WebGL audio still requires a user gesture in most browsers.
- Browser audio routing on WebGL does not use Unity `AudioSource` playback.
