# XR voice adapter — first batch

This directory provides an opt-in HTTP/WAV interface and one foreground voice
owner. It does not start a service, generate speech on scene load, import
LingChat settings, or invoke a fallback voice.

## Scene integration

Add `LingChatXR.Speech.XrSpeechController` to the character's voice object. Its
required `AudioSource` belongs exclusively to this controller. Keep existing
volume/spatial settings on that source; do not add a second speech player or
call `PlayOneShot` on it.

All methods and event handlers must run on Unity's main thread:

```csharp
var speech = voiceObject.AddComponent<XrSpeechController>();
// Optional, explicit configuration. The default endpoint remains empty/silent.
var settings = speech.Settings;
settings.provider = XrTtsProvider.MyNeuroChinese;
settings.endpoint = configuredFullEndpoint;
speech.Configure(settings);

// Called only in response to an explicit speech request:
speech.SpeakDialogue(text);

// Optional already-loaded local reaction. No synthesis and no dialogue queue:
speech.PlayReaction(preloadedReaction);

// Stop / change character / teardown:
speech.Stop();
```

`Configure` and `SetRequestFactory` cancel the current voice. `LoadSettings`
and `SaveSettings` are explicit calls and use only
`Application.persistentDataPath/xr-tts-v1.json`. The component never reads or
writes a LingChat character directory. Missing or invalid saved settings retain
the current Inspector configuration. Loading settings does not synthesize speech.

## Configured protocols

`MyNeuroChinese` sends a JSON POST to the full configured endpoint:

```json
{"text":"请求中的原文","text_language":"zh"}
```

`Sbv2Japanese` sends a JSON POST to the explicitly configured full `/synthesize`
URL. Its initial voice parameters are:

```json
{"ident":"Ling v2","length_scale":1,"sdp_ratio":0,"speaker_id":0,"style_id":0,"text":"要求された原文"}
```

Both expect a complete WAV response, decoded by `DownloadHandlerAudioClip`;
this is not chunk-streaming TTS. The adapter uses a configured timeout (default
60 seconds), a 32 MiB response ceiling, and no HTTP redirects. An empty endpoint
is silent. Failure is silent and does not select another provider or voice.

The currently observed LingChat default is an **in-process** `localsbv2api`
provider using `ling-v2` / speaker `0`. That fact does not establish a running
HTTP endpoint. This batch supplies the compatible sbv2api request interface,
without starting LingChat or a new server. Quest must use the PC's configured
LAN address when that separate service is prepared; Quest's `localhost` is not
the PC.

## Ownership and cancellation

- Dialogue supersedes any current voice; new dialogue cancels the previous request.
- Cached reactions are skipped while a dialogue request, dialogue, or reaction
  occupies the foreground source. A reaction does not start or advance dialogue.
- `PlaybackEvent(kind, outcome, generation)` distinguishes dialogue/reaction and
  Started/Completed/Cancelled/Skipped/Failed. Started means `AudioSource.Play`
  was invoked, not a measured acoustic first-sample timestamp.
- `Stop`, component disable, app pause, and loss of focus invalidate the current
  generation, stop playback, abort/dispose client HTTP work, and release downloaded
  clips. Caller-owned cached clips are never destroyed.
- Aborting HTTP cancels client waiting. It does **not** prove the remote GPU job
  stopped. No server-cancellation acknowledgement protocol is implemented.
- Network completion and audio manipulation use Unity coroutines on the main
  thread. Late generations cannot take the voice source. A completed reaction is
  never reported as completed dialogue.
- The caller remains responsible for associating cached reaction assets with the
  right model/voice version; this batch has no generated reaction cache.

## Evidence and limits (2026-10-29)

The three C# files compiled successfully against the locally installed
Unity **6000.4.0f1** engine assemblies and .NET Standard 2.1 references, with C# 9
and warnings treated as errors. No Unity 2022 validation was used for this adapter.
The compile output is a temporary validation DLL, not a new project dependency.

No real HTTP synthesis request, service startup, voice audition, runtime WAV
decode/playback, remote cancellation, Quest networking, or acoustic latency
measurement occurred. The source interface and API compilation are the evidence
for this batch; actual audio behavior remains to be exercised in the Unity 6
sample and then on the headset.

Unity API references checked while implementing:
[DownloadHandlerAudioClip](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Networking.DownloadHandlerAudioClip.html),
[UnityWebRequest.Abort](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Networking.UnityWebRequest.Abort.html),
[AudioSource.Play](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AudioSource.Play.html).
