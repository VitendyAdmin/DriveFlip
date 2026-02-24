using System;
using System.IO;
using System.Media;

namespace DriveFlip.Services;

/// <summary>
/// Generates and plays short notification chimes using programmatic WAV synthesis.
/// Sounds are pre-generated once and cached for instant playback.
/// </summary>
public static class SoundService
{
    private static readonly Lazy<byte[]> SuccessWav = new(GenerateSuccessWav);
    private static readonly Lazy<byte[]> ErrorWav = new(GenerateErrorWav);

    private const int SampleRate = 44100;
    private const short BitsPerSample = 16;
    private const short Channels = 1;
    private const int FadeMs = 10;

    public static void PlaySuccess()
    {
        try { PlayWav(SuccessWav.Value); }
        catch { /* Audio failure should never crash the app */ }
    }

    public static void PlayError()
    {
        try { PlayWav(ErrorWav.Value); }
        catch { /* Audio failure should never crash the app */ }
    }

    private static void PlayWav(byte[] wavData)
    {
        using var ms = new MemoryStream(wavData);
        using var player = new SoundPlayer(ms);
        player.Play();
    }

    /// <summary>
    /// Bright ascending arpeggio: C5 → E5 → G5 → C6, ~100ms each.
    /// Sine wave with slight detuned chorus for analog synth warmth.
    /// </summary>
    private static byte[] GenerateSuccessWav()
    {
        var notes = new (double freq, int durationMs)[]
        {
            (523.25, 100),  // C5
            (659.25, 100),  // E5
            (783.99, 100),  // G5
            (1046.50, 150), // C6 (slightly longer for a satisfying end)
        };

        int totalSamples = 0;
        foreach (var (_, dur) in notes)
            totalSamples += SampleRate * dur / 1000;

        var samples = new short[totalSamples];
        int offset = 0;

        foreach (var (freq, dur) in notes)
        {
            int count = SampleRate * dur / 1000;
            int fadeSamples = SampleRate * FadeMs / 1000;

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;

                // Main sine + detuned chorus (+3 Hz) for warmth
                double sample = 0.5 * Math.Sin(2 * Math.PI * freq * t)
                              + 0.3 * Math.Sin(2 * Math.PI * (freq + 3) * t);

                // Fade envelope to avoid clicks
                double envelope = 1.0;
                if (i < fadeSamples)
                    envelope = (double)i / fadeSamples;
                else if (i > count - fadeSamples)
                    envelope = (double)(count - i) / fadeSamples;

                sample *= envelope * 0.6; // Master volume
                samples[offset + i] = (short)(sample * short.MaxValue);
            }

            offset += count;
        }

        return BuildWav(samples);
    }

    /// <summary>
    /// Descending minor tone: E5 → C5, ~150ms each.
    /// Triangle wave with slight vibrato for an 80s warning feel.
    /// </summary>
    private static byte[] GenerateErrorWav()
    {
        var notes = new (double freq, int durationMs)[]
        {
            (659.25, 150),  // E5
            (523.25, 200),  // C5 (longer for gravity)
        };

        int totalSamples = 0;
        foreach (var (_, dur) in notes)
            totalSamples += SampleRate * dur / 1000;

        var samples = new short[totalSamples];
        int offset = 0;

        foreach (var (freq, dur) in notes)
        {
            int count = SampleRate * dur / 1000;
            int fadeSamples = SampleRate * FadeMs / 1000;

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;

                // Vibrato: 5 Hz, ±2 Hz depth
                double vibrato = freq + 2.0 * Math.Sin(2 * Math.PI * 5.0 * t);

                // Triangle wave
                double phase = (vibrato * t) % 1.0;
                double sample = phase < 0.5
                    ? 4.0 * phase - 1.0
                    : 3.0 - 4.0 * phase;

                // Fade envelope
                double envelope = 1.0;
                if (i < fadeSamples)
                    envelope = (double)i / fadeSamples;
                else if (i > count - fadeSamples)
                    envelope = (double)(count - i) / fadeSamples;

                sample *= envelope * 0.5; // Master volume
                samples[offset + i] = (short)(sample * short.MaxValue);
            }

            offset += count;
        }

        return BuildWav(samples);
    }

    private static byte[] BuildWav(short[] samples)
    {
        int dataSize = samples.Length * sizeof(short);
        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);

        // fmt chunk
        bw.Write("fmt "u8);
        bw.Write(16);                          // chunk size
        bw.Write((short)1);                    // PCM format
        bw.Write(Channels);
        bw.Write(SampleRate);
        bw.Write(SampleRate * Channels * BitsPerSample / 8); // byte rate
        bw.Write((short)(Channels * BitsPerSample / 8));      // block align
        bw.Write(BitsPerSample);

        // data chunk
        bw.Write("data"u8);
        bw.Write(dataSize);
        foreach (var s in samples)
            bw.Write(s);

        return ms.ToArray();
    }
}
