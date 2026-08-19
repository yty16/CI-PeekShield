using System.IO;
using System.Media;

namespace CIPeekShield.Services;

public static class AudioAlert
{
    private static readonly byte[] _wav = BuildBeep();

    public static void Play()
    {
        try
        {
            using var ms = new MemoryStream(_wav);
            using var player = new SoundPlayer(ms);
            player.Play();
        }
        catch {  }
    }

    private static byte[] BuildBeep()
    {
        const int rate = 44100;
        const int totalMs = 320;
        var samples = new short[rate * totalMs / 1000];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / rate;
            double f = t < 0.16 ? 880 : 1320;
            double elapsedInPhase = t < 0.16 ? t : (t - 0.16);
            double env = System.Math.Min(1.0, elapsedInPhase / 0.02) * System.Math.Min(1.0, (0.16 - elapsedInPhase) / 0.04 + 0.2);
            env = System.Math.Max(0, env);
            samples[i] = (short)(System.Math.Sin(2 * System.Math.PI * f * t) * 0.32 * 32767 * env);
        }

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write("RIFF".ToCharArray());
            bw.Write(36 + samples.Length * 2);
            bw.Write("WAVE".ToCharArray());
            bw.Write("fmt ".ToCharArray());
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(rate);
            bw.Write(rate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write("data".ToCharArray());
            bw.Write(samples.Length * 2);
            foreach (var s in samples) bw.Write(s);
        }
        return ms.ToArray();
    }
}
