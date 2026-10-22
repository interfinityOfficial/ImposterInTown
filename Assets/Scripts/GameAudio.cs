using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Sound effects and stings, played flat (2D) from a small pool of AudioSources so each shot can have its own
// pitch. Clips are Kenney's (CC0), loaded by name from Assets/Resources/Audio:
//   UI Audio          click3 (button press)
//   Interface Sounds  drop_003 (speech bubbles popping up), switch_002 (F once: armed),
//                     maximize_007 / minimize_009 (a nod / a head shake),
//                     error_005 (accused an innocent), drop_004 (the taken bar rises),
//                     glitch_001..003 (a copy snapping back)
//   RPG Audio         footstep04/05/06/09 (the player's and villagers' steps: the softer, duller set)
//   Music Jingles     jingles_PIZZI10 (won), jingles_PIZZI07 (lost), jingles_PIZZI09 (a copy turned back)
// Made in code: "babble0".."babble6", marimba notes on a pentatonic scale for villagers' gibberish speech.
// The host object survives scene loads, so a sound started just before PLAY AGAIN isn't cut off.
public static class GameAudio
{
    const int Voices = 10;

    static Host host;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

    class Host : MonoBehaviour
    {
        public AudioSource[] sources;
        public int next;
    }

    // Plays `clip` at `volume`, its pitch nudged randomly by up to ±`jitter` so repeats don't sound identical.
    public static void Play(string clip, float volume = 1f, float jitter = 0.05f, float pitch = 1f)
    {
        var c = Clip(clip);
        if (c == null) return;
        var h = Get();
        var source = h.sources[h.next];
        h.next = (h.next + 1) % h.sources.Length;
        source.pitch = pitch * (1f + Random.Range(-jitter, jitter));
        source.PlayOneShot(c, volume);
    }

    // The same, `delay` seconds from now (real time), so it lands after whatever is playing now.
    public static void PlayAfter(float delay, string clip, float volume = 1f, float jitter = 0.05f)
    {
        if (delay <= 0f) { Play(clip, volume, jitter); return; }
        Get().StartCoroutine(RunAfter(delay, clip, volume, jitter));
    }

    static IEnumerator RunAfter(float delay, string clip, float volume, float jitter)
    {
        yield return new WaitForSecondsRealtime(delay);
        Play(clip, volume, jitter);
    }

    // One of several clips at random (footsteps, glitches).
    public static void PlayAny(string[] choices, float volume = 1f, float jitter = 0.05f) =>
        Play(choices[Random.Range(0, choices.Length)], volume, jitter);

    // A quick run of clips `gap` seconds apart (a stuttering glitch).
    public static void Burst(string[] choices, int count, float gap, float volume = 1f)
    {
        var h = Get();
        h.StartCoroutine(RunBurst(choices, count, gap, volume));
    }

    static IEnumerator RunBurst(string[] choices, int count, float gap, float volume)
    {
        for (int i = 0; i < count; i++)
        {
            PlayAny(choices, volume, 0.15f);
            yield return new WaitForSecondsRealtime(gap * Random.Range(0.6f, 1.4f));
        }
    }

    // A clip by name, for sounds played from somewhere in the world (their own AudioSource) rather than flat.
    public static AudioClip Load(string name) => Clip(name);

    static AudioClip Clip(string name)
    {
        if (clips.TryGetValue(name, out var c)) return c;
        c = name.StartsWith("babble") ? BabbleClip(name[name.Length - 1] - '0') : Resources.Load<AudioClip>("Audio/" + name);
        if (c == null) Debug.LogWarning("Missing sound " + name);
        clips[name] = c;
        return c;
    }

    // Semitones above the base note: a pentatonic scale, so any run of notes sounds like a little tune.
    static readonly int[] Scale = { 0, 2, 4, 7, 9, 12, 14 };

    // A wooden marimba note, step `n` of the scale: a sine that rings away quickly, with a brief bright knock
    // (its 4th harmonic) on the strike. Speakers get their own voice by playing it at their own pitch.
    static AudioClip BabbleClip(int n)
    {
        const int rate = 44100;
        const float length = 0.16f;
        float f = 330f * Mathf.Pow(2f, Scale[Mathf.Clamp(n, 0, Scale.Length - 1)] / 12f);
        int samples = (int)(rate * length);
        var data = new float[samples];
        float phase = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / rate;
            phase += 2f * Mathf.PI * f / rate;
            float ring = Mathf.Sin(phase) * Mathf.Exp(-t / 0.06f) + 0.35f * Mathf.Sin(4f * phase) * Mathf.Exp(-t / 0.012f);
            data[i] = ring * Mathf.Min(1f, t / 0.002f) * Mathf.Min(1f, (length - t) / 0.01f) * 0.6f;
        }
        var clip = AudioClip.Create("Babble" + n, samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static Host Get()
    {
        if (host != null) return host;
        var go = new GameObject("Game Audio");
        Object.DontDestroyOnLoad(go);
        host = go.AddComponent<Host>();
        host.sources = new AudioSource[Voices];
        for (int i = 0; i < Voices; i++)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            host.sources[i] = s;
        }
        return host;
    }
}
