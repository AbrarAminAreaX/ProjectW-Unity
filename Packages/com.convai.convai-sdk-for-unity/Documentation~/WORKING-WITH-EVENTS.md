# Working With Events

Use this guide when you need to react to Convai runtime activity and you want to choose the right surface quickly.

## The short version

- `ConvaiManager.Events`
  - canonical typed reactive API
  - best for engineers writing gameplay, analytics, or custom runtime logic
- `ConvaiManager.Transcripts`
  - canonical transcript timeline and history API
  - best for chat logs, subtitles, replayable transcript state, and transcript-driven UI
- `ConvaiSessionEventRelay`, `ConvaiTranscriptEventRelay`, `ConvaiCharacterEventRelay`
  - no-code UnityEvent relays for designers
  - best when you want inspector wiring instead of subscription code
- `ConvaiCharacter`
  - local character convenience callbacks
  - best for one-character scene reactions
  - not the canonical room-level event source

## I want to update UI when the NPC speaks

Choose one:

- For a local, character-only widget, use `ConvaiCharacterEventRelay` or `ConvaiTranscriptDisplay`
- For room-wide subtitles or transcript UI, use `ConvaiManager.Transcripts`
- For typed code that reacts to each message, use `ConvaiManager.Events.OnCharacterTranscriptReceived`

## I want to react to connection or runtime errors

Choose one:

- Designers: add `ConvaiSessionEventRelay` and wire UnityEvents in the Inspector
- Engineers: subscribe to `ConvaiManager.Events.OnSessionStateChanged` and `ConvaiManager.Events.OnSessionError`

```csharp
using Convai.Domain.DomainEvents.Session;
using Convai.Runtime.Components;
using UnityEngine;

public sealed class SessionStatusExample : MonoBehaviour
{
    [SerializeField] private ConvaiManager manager;

    private void OnEnable()
    {
        if (manager == null) return;

        manager.Events.OnSessionStateChanged += HandleSessionStateChanged;
        manager.Events.OnSessionError += HandleSessionError;
    }

    private void OnDisable()
    {
        if (manager == null) return;

        manager.Events.OnSessionStateChanged -= HandleSessionStateChanged;
        manager.Events.OnSessionError -= HandleSessionError;
    }

    private void HandleSessionStateChanged(SessionStateChanged e)
    {
        Debug.Log($"Session: {e.OldState} -> {e.NewState}");
    }

    private void HandleSessionError(SessionError e)
    {
        Debug.LogError($"Convai error ({e.ErrorCode}): {e.Message}");
    }
}
```

## I want subtitles, chat history, or replayable transcript state

Use `ConvaiManager.Transcripts`.

That is the canonical transcript state surface. It owns timeline snapshots, turn history, and transcript queries. Do not rebuild transcript history by stitching together raw transcript callbacks unless you have a very specific reason.

Use these convenience layers on top when useful:

- `ITranscriptListener`
  - quick subtitle/chat UI hookup
- `ITranscriptUI`
  - built-in transcript presentation mode integration
- `ConvaiTranscriptEventRelay`
  - immediate UnityEvent reactions to transcript messages

## I want to drive gameplay from final transcript text

Choose one:

- Designers: add `ConvaiTranscriptEventRelay`, enable `Final Only`, and wire `On Final Character Transcript Received` or `On Final Player Transcript Received`
- Engineers: subscribe to `ConvaiManager.Events` and filter on `IsFinal`

The relay path is best for keyword-driven doors, timeline triggers, simple UI state, and animation toggles.

## I’m a designer and don’t want to write code

Use the relay components:

- `ConvaiSessionEventRelay`
  - connect, disconnect, reconnect, runtime error, usage-limit reactions
- `ConvaiTranscriptEventRelay`
  - transcript-driven UI or gameplay reactions
- `ConvaiCharacterEventRelay`
  - speech start/stop, local transcript, emotion, turn completed, ready

Recommended starting pattern:

1. Add the relay component to the same GameObject as the relevant manager or character.
2. Assign the explicit `ConvaiManager` or `ConvaiCharacter` reference if you want deterministic scene wiring.
3. Use UnityEvents to trigger animator parameters, UI panels, audio cues, or gameplay scripts.
4. Prefer `Final Only` on transcript relays when a reaction should happen once per completed line.

## Open Unity and wire the relays in a sample scene without code

Use the Basic Sample for this walkthrough.

### Goal A: open a door from final transcript text

1. Import the Basic Sample from Package Manager.
2. Open `Samples/BasicSample/Scenes/Basic Sample.unity`.
3. Create or choose a door GameObject in the scene.
4. Add `RelayDrivenDoorExample` to the door GameObject.
5. Assign the door transform on that component.
6. Set the keyword you want to react to.
7. Create an empty GameObject near the scene-level runtime objects.
8. Add `ConvaiTranscriptEventRelay`.
9. Assign the scene `ConvaiManager`, or leave auto-resolve enabled if there is only one active manager.
10. Enable `Final Only`.
11. If this should only react to one NPC, set `Character Id Filter`.
12. In `On Final Character Transcript Received`, add the door GameObject and choose `RelayDrivenDoorExample.HandleFinalCharacterTranscript`.
13. Enter Play Mode, connect, and speak the keyword or prompt the character to say it.

Expected result:

- the relay fires once for the completed line
- the door rotates to the configured open state
- no code changes are required

### Goal B: animate a character while it speaks

1. Select the NPC GameObject that already has `ConvaiCharacter`.
2. Add `ConvaiCharacterEventRelay`.
3. Add `RelayDrivenSpeechAnimator`.
4. Assign the target `Animator`.
5. Set the bool parameter name used by the Animator Controller, for example `IsSpeaking`.
6. In `On Speech Started`, add the same GameObject and choose `RelayDrivenSpeechAnimator.HandleSpeechStarted`.
7. In `On Speech Stopped`, add the same GameObject and choose `RelayDrivenSpeechAnimator.HandleSpeechStopped`.
8. Enter Play Mode and start a conversation.

Expected result:

- the animator bool turns on when the character starts speaking
- the animator bool turns off when speech stops

### Goal C: show connection or runtime status in UI

1. Create a UI GameObject or status controller object.
2. Add `ConvaiSessionEventRelay`.
3. Assign the scene `ConvaiManager`, or use auto-resolve in a simple scene.
4. Wire `On Connected`, `On Disconnected`, `On Reconnecting`, or `On Session Error` to your UI methods.
5. Enter Play Mode and connect/disconnect to confirm the transitions.

Expected result:

- lifecycle transitions are visible without subscription code
- session-error reactions can be authored in the Inspector

## Check whether a designer can succeed without explanation beyond the docs and Inspector

Use this as the acceptance checklist for the no-code path:

### Documentation check

- the designer starts from `SETUP.md`, `API-ENTRYPOINTS.md`, and this file only
- they can tell which relay to use without reading source code
- they can tell when to choose relays vs `ConvaiManager.Transcripts` vs `ConvaiCharacter`

### Inspector check

- the relay Inspector explains what the component is for in plain language
- missing manager or character targets show a clear warning
- transcript relay filter meanings are understandable from the Inspector alone
- the event fields are grouped clearly enough that no architecture knowledge is required

### Task success check

- the designer can wire one transcript-driven action without asking for code help
- the designer can wire one speech animation reaction without asking for code help
- the designer can wire one connection or error UI reaction without asking for code help

### Failure check

If the designer gets stuck, note exactly where:

- choosing the wrong surface
- understanding the Inspector warnings
- finding the right UnityEvent target method
- understanding final vs interim transcript behavior
- understanding whether they need a manager-scoped or character-scoped relay

If any of those fail in practice, treat that as a product UX bug, not a documentation nit.

## I’m an engineer and want the typed API

Use `ConvaiManager.Events`.

This is the canonical room/session event surface. It gives you typed domain events without exposing raw event hub usage in the beginner path.

Use `ConvaiManager.Transcripts` when you need transcript state rather than single-message callbacks.

Use `ConvaiCharacter` local callbacks only when the logic is intentionally scoped to one character.

## Surface rules

- Use `ConvaiManager.Events` for typed reactive facts
- Use `ConvaiManager.Transcripts` for transcript state and history
- Use relay components for no-code inspector workflows
- Use `ConvaiCharacter` callbacks for local character reactions only
- Avoid mixing multiple transcript surfaces for the same UI concern

## Related docs

- `API-ENTRYPOINTS.md`
- `SETUP.md`
- `PROJECT-SETTINGS.md`
- `TROUBLESHOOTING.md`
