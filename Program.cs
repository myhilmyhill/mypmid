using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text;

[DllImport("winmm", CharSet = CharSet.Auto)]
static extern int midiOutGetNumDevs();
[DllImport("winmm", CharSet = CharSet.Auto)]
static extern int midiOutGetDevCapsA(int uDeviceId, ref MIDIOUTCAPS lpCaps, int uSize);

var rootCommand = new RootCommand("My player for MIDI");

var listCommand = new Command("list", "Show all MIDI out devices");
listCommand.Aliases.Add("--list");
listCommand.SetAction(parseResult =>
{
    int all = midiOutGetNumDevs();
    for (int i = 0; i < all; i++)
    {
        MIDIOUTCAPS caps = new MIDIOUTCAPS();
        midiOutGetDevCapsA(i, ref caps, Marshal.SizeOf(typeof(MIDIOUTCAPS)));
        Console.WriteLine($"{i}: {caps.szPname}");
    }
    return 0;
});

var playCommand = new Command("play", "Play MIDI file");
playCommand.Aliases.Add("p");

var fileArgument = new Argument<string>("file")
{
    Description = "MIDI file"
};

var portOption = new Option<string?>("--port")
{
    Description = "MIDI out port (default: mapper)"
};
portOption.Aliases.Add("-p");

var mapOption = new Option<MidiMap?>("--map")
{
    Description = "Inst map"
};
mapOption.Aliases.Add("-m");

var debugOption = new Option<bool>("--debug")
{
    Description = "Show mciSendString"
};

playCommand.Arguments.Add(fileArgument);
playCommand.Options.Add(portOption);
playCommand.Options.Add(mapOption);
playCommand.Options.Add(debugOption);

playCommand.SetAction(parseResult =>
{
    string file = parseResult.GetValue(fileArgument)!;
    string? port = parseResult.GetValue(portOption);
    MidiMap? map = parseResult.GetValue(mapOption);
    bool debug = parseResult.GetValue(debugOption);

    try
    {
        IPlayer player = new ManagedMidiPlayer(file, port, debug);
        if (map != null)
        {
            player.SetMap(map.Value);
        }

        Console.CancelKeyPress += (_, _) =>
        {
            player.Finally();
        };

        try
        {
            while (player.Play()) ;
        }
        catch
        {
            throw;
        }
        finally
        {
            player.Finally();
        }
    }
    catch (Exception e)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
    return 0;
});

rootCommand.Subcommands.Add(listCommand);
rootCommand.Subcommands.Add(playCommand);

return rootCommand.Parse(args).Invoke();
