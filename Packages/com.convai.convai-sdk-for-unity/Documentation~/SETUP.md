# Setup

This is the shortest path from package import to a working conversation.

If setup fails, go to `TROUBLESHOOTING.md`.

## Prerequisites

- Convai API key
- At least one Character ID from the Convai dashboard
- Unity version aligned with this repo snapshot

## Fastest validation path

1. Import the **Basic Sample** from Package Manager
2. Open `Samples/BasicSample/Scenes/Basic Sample.unity`
3. Set your API key in `Edit > Project Settings > Convai SDK`
4. Select the sample `ConvaiCharacter` and set `Character Id`
5. Press Play and connect

The LipSync Sample is a showcase scene with additional rendering dependencies. Use the Basic Sample first.

## Integrating your own scene

### 1. Add the required scene objects

Run:

- `GameObject > Convai > Setup Required Components`

This ensures the scene contains:

- `ConvaiManager`

Then add manually:

- `ConvaiPlayer` on the player object
- `ConvaiCharacter` on each NPC or agent
- `ConvaiAudioOutput` on characters that should use Unity audio playback

### 2. Configure project defaults

Open:

- `Edit > Project Settings > Convai SDK`

At minimum, set:

- `API Key`

Recommended while integrating:

- `Global Log Level = Info`
- verify the server URL is correct for your environment

See `PROJECT-SETTINGS.md` for the full reference.

### 3. Choose a conversation mode

On `ConvaiRoomManager`, use the inspector as your main setup surface:

- `Hands Free`
  - recommended starting point
  - uses SDK-default smart turn
- `Push To Talk`
  - exposes a scene-level push-to-talk key plus the common push-to-talk options
  - the built-in runtime push-to-talk controller is driven automatically from the room settings

Use `ConvaiRoomManager` for normal scene setup. `ConvaiManager` stays in the scene as the runtime facade for connection, events, audio, and transcript APIs.

If you need reusable room defaults or low-level room/session controls, open `ConvaiRoomManager`, choose the room setup source there, and assign a `Room Manager Profile` asset only when you want reusable room defaults across scenes.

### 4. Configure each character

On each `ConvaiCharacter`:

- set `Character Setup Source` to `Inline` or `Character Profile Asset`
- in `Inline` mode, set `Character Id` and optional `Character Name`
- in `Character Profile Asset` mode, assign a `Character Profile` asset

`Inline` is the default and recommended starting point.

### 5. Start a conversation

The most direct API is `ConvaiCharacter`:

```csharp
using Convai.Runtime.Components;
using UnityEngine;

public sealed class StartStopExample : MonoBehaviour
{
    [SerializeField] private ConvaiCharacter character;

    public async void StartConversation() => await character.StartConversationAsync();
    public async void StopConversation() => await character.StopConversationAsync();
}
```

For scene-level events, audio, room control, and transcript APIs, use `ConvaiManager`.

See `API-ENTRYPOINTS.md` for the recommended API by task and `WORKING-WITH-EVENTS.md` for the event-system integration guide.

## Configuration assets

You only need config assets if you want reusable defaults.

- `Room Manager Profile` maps to `ConvaiRoomManagerProfile`
- `Character Profile` maps to `ConvaiCharacterProfile`

Use `Asset` mode on the component to make the assigned asset the single active source of truth.

For turn-taking specifically:

- `ConvaiRoomManager` is the scene setup surface
- `Room Manager Profile` assets are reusable advanced defaults
- per-call `ConnectAsync(RoomSessionConnectOptions)` overrides both when you need explicit runtime control

## End-user identity

The SDK will generate a device-based `end_user_id` by default, but production games should usually provide their own stable user identity.

- use `ConvaiManager.SetEndUserIdentityProvider(...)` to provide a stable end-user ID
- use `ConvaiManager.SetEndUserMetadataProvider(...)` to send optional metadata such as `name`
- keep the same `end_user_id` for the same human across sessions and devices when possible

`end_user_id` is the public identity input used for tracking, analytics, and end-user management. If character memory is enabled, the backend uses that identity to resolve the internal memory partition for that character.

## First successful run checklist

- API key is set
- Scene contains one `ConvaiManager`
- Scene contains one `ConvaiPlayer`
- At least one `ConvaiCharacter` has a valid `Character Id`
- Characters that should play audio have an `AudioSource` or `ConvaiAudioOutput`
- Microphone permission is granted on the target platform

## Next docs

- `API-ENTRYPOINTS.md`
- `WORKING-WITH-EVENTS.md`
- `PROJECT-SETTINGS.md`
- `PLATFORMS.md`
- `TROUBLESHOOTING.md`
