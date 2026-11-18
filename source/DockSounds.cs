using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;

namespace LumaDock;

internal static class DockSounds
{
    private static readonly Dictionary<string, SoundPlayer> tones = new();

    public static void Play(string name)
    {
        try
        {
            if (!tones.TryGetValue(name, out var tone))
            {
                tone = new SoundPlayer(Wave(name));
                tones[name] = tone;
            }
            tone.Play();
        }
        catch (InvalidOperationException) { }
    }

    private static MemoryStream Wave(string name)
    {
        var notes = name switch
        {
            "Chime" => new[] { (523.25, 0.0, .24), (783.99, .10, .42) },
            "Ripple" => new[] { (523.25, 0.0, .18), (659.25, .08, .19), (783.99, .16, .32) },
            "Bell" => new[] { (587.33, 0.0, .64), (1174.66, 0.0, .21) },
            "Sonar" => new[] { (440.0, 0.0, .38), (440.0, .2, .36) },
            "Pulse" => new[] { (293.66, 0.0, .17), (392.0, .15, .2) },
            _ => new[] { (784.0, 0.0, .26), (1046.5, .05, .36), (1568.0, .11, .26) }
        };
        const int sampleRate = 44100, count = sampleRate * 3 / 4;
        var stream = new MemoryStream(44 + count * 2);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + count * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(count * 2);
        for (int index = 0; index < count; index++)
        {
            double time = (double)index / sampleRate;
            double y = 0;
            foreach (var (frequency, start, length) in notes)
            {
                double t = time - start;
                if (t < 0 || t >= length) continue;
                double ramp = Math.Min(1, t / .007) * Math.Min(1, (length - t) / .035) * Math.Exp(-5 * t / length);
                y += (Math.Sin(Math.Tau * frequency * t) + .17 * Math.Sin(Math.Tau * frequency * 2.01 * t)) * ramp;
            }
            writer.Write((short)(y * 6200));
        }
        writer.Flush();
        stream.Position = 0;
        return stream;
    }
}
