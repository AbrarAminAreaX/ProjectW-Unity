# API Entrypoints

Use the highest-level API that solves your task.

## Recommended entrypoint by task

| Goal | Use | Notes |
|---|---|---|
| Scene-level runtime, room, audio, and event ownership | `ConvaiManager` | Default Unity entrypoint |
| Typed SDK events (transcripts, emotions, actions) | `ConvaiManager.Events` | Primary reactive event surface |
| No-code event reactions in the Inspector | `ConvaiSessionEventRelay`, `ConvaiTranscriptEventRelay`, `ConvaiCharacterEventRelay` | Recommended designer path |
| Scene setup in the inspector | `ConvaiRoomManager` | Configure conversation mode, push-to-talk, room source, and advanced room defaults here |
| Start or stop one character conversation | `ConvaiCharacter` | Preferred per-character API |
| SDK metadata only | `ConvaiSDK` | Static package metadata |
| Room transcript timeline, history, and live updates | `ConvaiManager.Transcripts` | Canonical transcript entrypoint |
| Built-in transcript UI hooks | `ITranscriptListener`, `ITranscriptUI` | Convenience adapters over `ConvaiManager.Transcripts` |
| Advanced room/session overrides | `ConvaiRoomManager` | Advanced setup only |

## Default mental model

- `ConvaiManager` is the runtime facade and code-facing scene entrypoint
- `ConvaiManager.Events` is the canonical typed reactive event surface
- `ConvaiManager.Transcripts` is the canonical transcript timeline and history surface
- event relays are the inspector-friendly no-code layer on top of those manager and character APIs
- `ConvaiRoomManager` is the inspector setup surface for conversation mode and room defaults
- `ConvaiPlayer` is the player agent
- `ConvaiCharacter` components are the character agents and expose local convenience callbacks
- modules such as LipSync, Vision, and Narrative attach capabilities to the active runtime and room

## Use `ConvaiManager` for runtime behavior

Examples:

- connect and disconnect the room
- toggle microphone mute
- read the room transcript timeline
- access the active character or owned character set

## Use `ConvaiRoomManager` for scene setup

Examples:

- choose `Hands Free` or `Push To Talk`
- set the push-to-talk key for the scene
- choose `Scene Defaults` or `Room Manager Profile Asset`
- adjust advanced room/session defaults when needed

## Use `ConvaiCharacter` for character-scoped behavior

Examples:

- start or stop a conversation
- send character triggers or dynamic info
- toggle remote audio for one character
- consume character-specific transcript or emotion events

Use `ConvaiCharacter` local callbacks when the reaction belongs to one character. Do not use them as the canonical room-level event source for transcript history or session orchestration.

## Use `ConvaiManager.Transcripts` for transcript-driven features

Use `ConvaiManager.Transcripts` when you need:

- the canonical room transcript timeline
- active vs completed turn state
- per-player/character transcript queries
- room chat, subtitles, history, or multiplayer transcript UIs

Compatibility surfaces:

- `ConvaiManager.Events` for reactive handling of typed domain events (Recommended)
- `ITranscriptListener` for quick UI hookups
- `ITranscriptUI` for built-in transcript-mode views
- `ConvaiCharacter.OnTranscriptReceived` only for character-scoped TTS-style behavior

Do not build new transcript features by combining multiple transcript surfaces for the same UI concern.

## Use event relays for no-code scene wiring

Use relay components when designers need Inspector-based UnityEvents instead of C# subscriptions:

- `ConvaiSessionEventRelay`
  - connection, reconnection, session state, runtime error, usage-limit hooks
- `ConvaiTranscriptEventRelay`
  - transcript-driven UI and gameplay reactions
- `ConvaiCharacterEventRelay`
  - one-character speech, emotion, ready, and turn-complete reactions

For walkthroughs and task-based examples, see `WORKING-WITH-EVENTS.md`.

## Advanced runtime extension points

`ConvaiRuntimeBuilder` exposes supported advanced providers for projects that need explicit runtime composition:

- `WithEndUserIdentityProvider(IEndUserIdentityProvider)`
- `WithEndUserMetadataProvider(IEndUserMetadataProvider)`
- `WithFeatureVariants(IFeatureVariantProvider)`
- `UsePersistence(IPersistenceProvider)`
- `UseTelemetry(ITelemetryProvider)`

These are advanced integration seams, not the normal Unity-scene entrypoint. Most users should stay on the `ConvaiManager` path.

For normal scene-based setups, prefer:

- `ConvaiManager.SetEndUserIdentityProvider(...)`
- `ConvaiManager.SetEndUserMetadataProvider(...)`

Use the same `end_user_id` for the same human across sessions. The backend treats that as the public identity layer, while long-term memory remains partitioned per character.

## Minimal examples

### Scene-level connect/disconnect

```csharp
using Convai.Runtime.Components;
using UnityEngine;

public sealed class ConnectionPanel : MonoBehaviour
{
    [SerializeField] private ConvaiManager manager;

    public async void Connect() => await manager.ConnectAsync();
    public async void Disconnect() => await manager.DisconnectAsync();
}
```

### Advanced connect-time override

Use `ConnectAsync(RoomSessionConnectOptions)` only when you need to override the manager's normal inspector setup at runtime.

```csharp
using Convai.Runtime.Components;
using Convai.Runtime.Room;
using UnityEngine;

public sealed class PushToTalkConnectExample : MonoBehaviour
{
    [SerializeField] private ConvaiManager manager;

    public async void ConnectPushToTalk()
    {
        try
        {
            RoomSessionConnectOptions options = new()
            {
                TurnTaking = TurnTakingOptions.CreatePushToTalkDefault()
            };

            await manager.ConnectAsync(options);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to connect: {ex}");
        }
    }
}
```

### Per-character conversation control

```csharp
using Convai.Runtime.Components;
using UnityEngine;

public sealed class CharacterConversationUI : MonoBehaviour
{
    [SerializeField] private ConvaiCharacter character;

    public async void StartConversation() => await character.StartConversationAsync();
    public async void StopConversation() => await character.StopConversationAsync();
}
```

### Audio controls

```csharp
using Convai.Runtime.Components;
using UnityEngine;

public sealed class AudioControls : MonoBehaviour
{
    [SerializeField] private ConvaiManager manager;
    [SerializeField] private ConvaiCharacter character;

    public void ToggleMicMute() => manager.Audio.ToggleMicMuted();

    public void ToggleCharacterAudio()
    {
        bool muted = manager.Audio.IsCharacterMuted(character.CharacterId);
        manager.Audio.SetCharacterMuted(character.CharacterId, !muted);
    }
}
```

### Event subscriptions

Use `ConvaiManager.Events` for typed reactive event handling. Subscribe in `OnEnable` and unsubscribe in `OnDisable` using named handlers.

```csharp
using Convai.Runtime.Components;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.DomainEvents.Transcript;
using UnityEngine;

public sealed class EventListenerExample : MonoBehaviour
{
    [SerializeField] private ConvaiManager manager;

    private void OnEnable()
    {
        if (manager == null) return;

        var events = manager.Events;
        events.OnCharacterTranscriptReceived += HandleCharacterTranscriptReceived;
        events.OnSessionError += HandleSessionError;
    }

    private void OnDisable()
    {
        if (manager == null) return;

        var events = manager.Events;
        events.OnCharacterTranscriptReceived -= HandleCharacterTranscriptReceived;
        events.OnSessionError -= HandleSessionError;
    }

    private void HandleCharacterTranscriptReceived(CharacterTranscriptReceived e)
    {
        Debug.Log($"[{e.CharacterId}] {e.Text}");
    }

    private void HandleSessionError(SessionError e)
    {
        Debug.LogError($"SDK Error ({e.ErrorCode}): {e.Message}");
    }
}
```

### Designer-friendly relay setup

Use the relay components when you want UnityEvent wiring in the Inspector instead of code:

- add `ConvaiSessionEventRelay` to a GameObject near your `ConvaiManager`
- add `ConvaiTranscriptEventRelay` when transcript text should drive UI or gameplay
- add `ConvaiCharacterEventRelay` to the same GameObject as a `ConvaiCharacter` for local reactions such as animation or VFX

For the task-based guide, examples, and surface decision tree, read `WORKING-WITH-EVENTS.md`.

## Avoid these mistakes

- Do not treat `ConvaiManager` and `ConvaiRoomManager` as two competing setup surfaces. Configure the scene on `ConvaiRoomManager`; use `ConvaiManager` for runtime APIs.
- Do not build new push-to-talk setup around old sample behaviors. Use `ConvaiRoomManager` scene setup, or override turn-taking with `ConnectAsync(RoomSessionConnectOptions)` when you need explicit runtime control.
- Do not build transcript state from raw transcript callbacks when `ConvaiManager.Transcripts` already exposes the room timeline.
- Do not treat `ConvaiCharacter` local callbacks as the canonical room-level transcript or session event surface.
- Do not rely on scene-order ownership in multi-character scenes.
- Do not treat scene defaults and Room Manager Profile assets as active at the same time. The active room setup source on `ConvaiRoomManager` decides which one is used.
