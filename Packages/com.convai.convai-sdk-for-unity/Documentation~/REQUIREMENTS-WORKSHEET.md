# Requirements Worksheet (for scoping)

Use this worksheet to turn “we want Convai characters” into a decision-complete spec.

## 1) Target platforms (the biggest scope multiplier)

- iOS / Android (native)
- Desktop (Windows/macOS)
- WebGL (browser)
- XR

For each platform, define:

- Minimum device/browser targets
- Network constraints (enterprise firewall, low bandwidth, offline expectations)

## 2) Voice UX

- Push-to-talk vs open mic vs hybrid
- Barge-in (can user interrupt the character?)
- Captions/subtitles required?
- Text input fallback required?

## 3) Character count and behavior

- How many characters can be active in a scene?
- Are characters “always listening” or only in specific zones/interactions?
- Do you need structured actions/triggers (quests, purchases, UI navigation)?

## 4) Vision (“character can see”)

- Is vision in scope?
- Source: in-game camera vs device camera/webcam
- When active: always vs user action vs specific scenes
- Privacy: consent UX, retention rules, PII constraints

## 5) Transcripts, logging, and analytics

- What gets stored (if anything): transcripts, events, session IDs
- Who can access logs, and retention/deletion policies
- Telemetry requirements (latency, reconnect count, opt-in/opt-out)

## 6) Acceptance criteria (MVP definition of done)

Define a single “MVP demo” that must work reliably:

- [ ] Scene connects and character becomes ready within X seconds
- [ ] Mic permission flow works (and denial fallback is acceptable)
- [ ] Character audio is audible and stable for a 5–10 minute session
- [ ] Transcripts render in the chosen UI mode
- [ ] Disconnect/reconnect behavior is acceptable on poor networks

