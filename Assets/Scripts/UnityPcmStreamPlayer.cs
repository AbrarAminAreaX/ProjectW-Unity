using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Receives base64 PCM16 mono chunks from Flutter (24000 Hz from the
/// LiveChat WebSocket pipeline) and pushes them into a streaming AudioClip
/// that plays on a silent AudioSource. Audible playback still happens on
/// the Android native side (AudioTrack); this Unity AudioSource exists
/// only so analyzers like uLipSync have something to FFT.
///
/// Usage:
///   var player = robot.GetComponent&lt;UnityPcmStreamPlayer&gt;();
///   player.PushChunk(base64String);
///
/// Inspector:
///   - Sample Rate (default 24000) — must match server
///   - Output Volume (default 0) — keep at 0; native AudioTrack is the audible source
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class UnityPcmStreamPlayer : MonoBehaviour
{
    [Tooltip("PCM sample rate from the server (Hz). The LiveChat backend ships 24000.")]
    public int sampleRate = 24000;

    [Tooltip("If true, this script overrides AudioSource.volume in Awake. Disable when you mute via an AudioMixer group instead (recommended) — uLipSync needs samples at full amplitude before the mixer attenuates output.")]
    public bool overrideAudioSourceVolume = false;
    [Tooltip("Volume to write to the AudioSource only when overrideAudioSourceVolume is true.")]
    [Range(0f, 1f)] public float outputVolume = 1f;

    [Tooltip("Ring buffer length in seconds. Larger absorbs network jitter; smaller reduces lag from native to Unity.")]
    public float bufferSeconds = 2f;

    private AudioSource _audioSource;
    private AudioClip _clip;
    private float[] _ring;        // ring buffer of float samples (-1..1)
    private int _writePos;        // next write index
    private int _samplesQueued;   // how many unread samples are in the ring
    private readonly object _ringLock = new object();

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.loop = true;
        // Only touch volume if asked. Default: leave the AudioSource at whatever
        // the user set in the Inspector — they typically wire an AudioMixer
        // group with -80dB to mute the audible output while keeping samples
        // at full amplitude for uLipSync to analyze.
        if (overrideAudioSourceVolume) _audioSource.volume = outputVolume;
        _audioSource.spatialBlend = 0f; // 2D, no positional attenuation

        int ringLen = Mathf.Max(1024, Mathf.RoundToInt(bufferSeconds * sampleRate));
        _ring = new float[ringLen];

        // Streaming AudioClip backed by our ring buffer.
        _clip = AudioClip.Create(
            "uLipSyncStream",
            ringLen,
            1,
            sampleRate,
            stream: true,
            pcmreadercallback: OnAudioRead,
            pcmsetpositioncallback: OnAudioSetPosition);
        _audioSource.clip = _clip;
        _audioSource.Play();
    }

    void OnEnable() { if (_audioSource != null && overrideAudioSourceVolume) _audioSource.volume = outputVolume; }

    /// <summary>
    /// Decode and enqueue a base64 PCM16 mono chunk. Safe to call from
    /// any thread; the actual buffer write is locked.
    /// </summary>
    public void PushChunk(string base64Pcm)
    {
        if (string.IsNullOrEmpty(base64Pcm)) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64Pcm); }
        catch (FormatException e)
        {
            Debug.LogWarning("[PcmStream] bad base64: " + e.Message);
            return;
        }

        // PCM16 LE → float [-1..1]
        int samples = bytes.Length / 2;
        if (samples == 0) return;

        lock (_ringLock)
        {
            for (int i = 0; i < samples; i++)
            {
                int lo = bytes[i * 2] & 0xff;
                int hi = (sbyte)bytes[i * 2 + 1];
                short s = (short)((hi << 8) | lo);
                _ring[_writePos] = s / 32768f;
                _writePos = (_writePos + 1) % _ring.Length;
            }
            _samplesQueued = Mathf.Min(_ring.Length, _samplesQueued + samples);
        }
    }

    /// <summary>Reset/clear (e.g. on disconnect or scene end).</summary>
    public void Reset()
    {
        lock (_ringLock)
        {
            Array.Clear(_ring, 0, _ring.Length);
            _writePos = 0;
            _samplesQueued = 0;
        }
    }

    // ─── AudioClip streaming callbacks (called by Unity's audio thread) ───

    private int _readPos;

    private void OnAudioRead(float[] data)
    {
        // Pull from ring buffer; on underrun, fill with zeros so the
        // stream keeps running (uLipSync sees silence, returns sil viseme).
        lock (_ringLock)
        {
            int n = data.Length;
            int avail = _samplesQueued;
            int toCopy = Mathf.Min(n, avail);
            for (int i = 0; i < toCopy; i++)
            {
                data[i] = _ring[_readPos];
                _readPos = (_readPos + 1) % _ring.Length;
            }
            for (int i = toCopy; i < n; i++) data[i] = 0f;
            _samplesQueued -= toCopy;
        }
    }

    private void OnAudioSetPosition(int newPosition)
    {
        // Streaming clip position resets aren't meaningful for our use — we
        // always read from the head of unread data, so just no-op.
    }
}
