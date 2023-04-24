using System.Globalization;
using Commons.Music.Midi;

public class ManagedMidiPlayer : IPlayer
{
    private readonly bool debug;
    private readonly MidiPlayer player;
    private readonly IMidiOutput output;
    private readonly TimeSpan total;
    bool finished = false;

    public ManagedMidiPlayer(string file, string? port, bool debug = false)
    {
        this.debug = debug;

        var access = MidiAccessManager.Default;
        output = access.OpenOutputAsync(port ?? "0").Result;
        Console.WriteLine($"{port}: {output.Details.Name}");

        var music = MidiMusic.Read(File.OpenRead(file));
        player = new MidiPlayer(music, output);
        player.Play();
        player.Finished += () => {
            finished = true;
        };
        total = TimeSpan.FromMilliseconds(player.GetTotalPlayTimeMilliseconds());
    }

    public bool Play()
    {
        Console.Write($"{player.State} {player.PositionInTime} / {total}\r");
        return !finished;
    }

    public void Finally()
    {
        Console.WriteLine();
        player.Dispose();
        if (debug) Console.WriteLine("player.Dispose");
        // All Sound Off
        for (int i = 0; i < 16; i++) {
            var allSoundOff = new byte[] {(byte)(i + 0xB0), 0x78, 0};
            output.Send(allSoundOff, 0, 3, 0);
            if (debug) Console.WriteLine($"output.Send({string.Join(" ", allSoundOff)})");
        }
        output.CloseAsync().Wait();
        if (debug) Console.WriteLine("output.CloseAsync");
    }

    public void SetMap(MidiMap map)
    {
        var sysexString = map switch
        {
            MidiMap.Default => "",
            MidiMap.Sc55Map =>
            """
            F0 41 10 42 12 40 41 01 01 7D F7
            F0 41 10 42 12 40 42 01 01 7C F7
            F0 41 10 42 12 40 43 01 01 7B F7
            F0 41 10 42 12 40 44 01 01 7A F7
            F0 41 10 42 12 40 45 01 01 79 F7
            F0 41 10 42 12 40 46 01 01 78 F7
            F0 41 10 42 12 40 47 01 01 77 F7
            F0 41 10 42 12 40 48 01 01 76 F7
            F0 41 10 42 12 40 49 01 01 75 F7
            F0 41 10 42 12 40 40 01 01 7E F7
            F0 41 10 42 12 40 4A 01 01 74 F7
            F0 41 10 42 12 40 4B 01 01 73 F7
            F0 41 10 42 12 40 4C 01 01 72 F7
            F0 41 10 42 12 40 4D 01 01 71 F7
            F0 41 10 42 12 40 4E 01 01 70 F7
            F0 41 10 42 12 40 4F 01 01 6F F7
            """,
            MidiMap.Sc88Map =>
            """
            f0 41 10 42 12 40 41 01 02 7c f7
            f0 41 10 42 12 40 42 01 02 7b f7
            f0 41 10 42 12 40 43 01 02 7a f7
            f0 41 10 42 12 40 44 01 02 79 f7
            f0 41 10 42 12 40 45 01 02 78 f7
            f0 41 10 42 12 40 46 01 02 77 f7
            f0 41 10 42 12 40 47 01 02 76 f7
            f0 41 10 42 12 40 48 01 02 75 f7
            f0 41 10 42 12 40 49 01 02 74 f7
            f0 41 10 42 12 40 40 01 02 7d f7
            f0 41 10 42 12 40 4a 01 02 73 f7
            f0 41 10 42 12 40 4b 01 02 72 f7
            f0 41 10 42 12 40 4c 01 02 71 f7
            f0 41 10 42 12 40 4d 01 02 70 f7
            f0 41 10 42 12 40 4e 01 02 6f f7
            f0 41 10 42 12 40 4f 01 02 6e f7
            """,
            MidiMap.Sc88ProMap =>
            """
            f0 41 10 42 12 40 41 01 03 7b f7
            f0 41 10 42 12 40 42 01 03 7a f7
            f0 41 10 42 12 40 43 01 03 79 f7
            f0 41 10 42 12 40 44 01 03 78 f7
            f0 41 10 42 12 40 45 01 03 77 f7
            f0 41 10 42 12 40 46 01 03 76 f7
            f0 41 10 42 12 40 47 01 03 75 f7
            f0 41 10 42 12 40 48 01 03 74 f7
            f0 41 10 42 12 40 49 01 03 73 f7
            f0 41 10 42 12 40 40 01 03 7c f7
            f0 41 10 42 12 40 4a 01 03 72 f7
            f0 41 10 42 12 40 4b 01 03 71 f7
            f0 41 10 42 12 40 4c 01 03 70 f7
            f0 41 10 42 12 40 4d 01 03 6f f7
            f0 41 10 42 12 40 4e 01 03 6e f7
            f0 41 10 42 12 40 4f 01 03 6d f7
            """,
            MidiMap.Sc8850Map =>
            """
            F0 41 10 42 12 40 41 01 04 7A F7
            F0 41 10 42 12 40 42 01 04 79 F7
            F0 41 10 42 12 40 43 01 04 78 F7
            F0 41 10 42 12 40 44 01 04 77 F7
            F0 41 10 42 12 40 45 01 04 76 F7
            F0 41 10 42 12 40 46 01 04 75 F7
            F0 41 10 42 12 40 47 01 04 74 F7
            F0 41 10 42 12 40 48 01 04 73 F7
            F0 41 10 42 12 40 49 01 04 72 F7
            F0 41 10 42 12 40 40 01 04 7B F7
            F0 41 10 42 12 40 4A 01 04 71 F7
            F0 41 10 42 12 40 4B 01 04 70 F7
            F0 41 10 42 12 40 4C 01 04 6F F7
            F0 41 10 42 12 40 4D 01 04 6E F7
            F0 41 10 42 12 40 4E 01 04 6D F7
            F0 41 10 42 12 40 4F 01 04 6C F7
            """,
            MidiMap.XgMode =>
            """
            F0 43 10 4C 00 00 7E 00 F7
            """,
            _ => throw new ArgumentOutOfRangeException(nameof(map), $"Not expected value: {map}"),
        };
        if (string.IsNullOrWhiteSpace(sysexString)) return;

        var sysex = sysexString
            .Split(' ', '\n')
            .Select(s => byte.Parse(s.Trim(), NumberStyles.HexNumber))
            .ToArray();
        output.Send(sysex, 0, sysex.Length, 0);
        Console.WriteLine(map.ToString());
        if (debug) Console.WriteLine($"output.Send({string.Join(" ", sysex)})");
    }
}