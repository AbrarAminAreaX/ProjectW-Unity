# Troubleshooting

Use this when the scene looks correct but behavior is wrong at runtime.

For choosing the right runtime event surface before debugging event behavior, see `WORKING-WITH-EVENTS.md`.

## Check first

1. Unity Console
2. `Edit > Project Settings > Convai SDK`
3. `GameObject > Convai > Validate Scene Setup`
4. Character setup source and active values

## Common problems

### API key missing or invalid

Symptoms:

- connection fails immediately
- auth or token errors in logs

Fix:

- set the API key in `Edit > Project Settings > Convai SDK`

### Character ID missing

Symptoms:

- character never becomes ready
- start conversation fails

Fix:

- set `Character Id` on the `ConvaiCharacter`, or assign a `Character Profile` asset in `Asset` mode

### No microphone input

Symptoms:

- player transcripts never appear
- mic publish fails

Fix:

- grant microphone permission
- confirm the correct input device
- on WebGL, run over HTTPS or localhost
- on WebGL, start audio/mic from a user gesture

### No character audio playback

Symptoms:

- text and transcripts work, but the character is silent

Fix:

- ensure remote audio is enabled for that character
- ensure the character has `ConvaiAudioOutput` or a valid `AudioSource` on native platforms
- on WebGL, remember that playback is browser-routed, not Unity `AudioSource` routed

### Connect succeeds but the character never becomes ready

Symptoms:

- room connects
- no ready event arrives

Fix:

- re-check the active Character ID
- inspect logs around session state and character-ready timing
- increase the character-ready timeout if the environment is slow

### I need runtime error handling in code or the Inspector

Use:

- `ConvaiManager.Events.OnSessionError` for typed code-driven error handling
- `ConvaiSessionEventRelay` for Inspector-driven UnityEvent reactions

Use `OnSessionError` when you want to show fallback UI, trigger telemetry, or react to recoverable vs non-recoverable failures at runtime.

## Known issue: WebGL audio and lip sync drift

Symptoms:

- WebGL receives audio and lip-sync data
- playback timing and lip-sync animation do not stay aligned

Status:

- known defect
- not the same as “audio is unsupported on WebGL”

Current guidance:

- validate browser builds with realistic speech cadence
- treat the issue as a release-note limitation until the sync path is fixed

## If the editor behaves strangely

- recompile scripts
- reopen the affected inspector
- if the test runner hangs the editor, restart Unity and rerun a focused subset before rerunning the full suite
