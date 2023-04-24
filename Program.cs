using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text;

[DllImport("winmm", CharSet = CharSet.Auto)]
static extern int midiOutGetNumDevs();
[DllImport("winmm", CharSet = CharSet.Auto)]
static extern int midiOutGetDevCapsA(int uDeviceId, ref MIDIOUTCAPS lpCaps, int uSize);

var rootCommand = new RootCommand("My player for MIDI");
var listOption = new Option<bool>(
    name: "--list",
    description: "Show all MIDI out devices");
listOption.Arity = ArgumentArity.Zero;
var fileArgument = new Argument<string>(
    name: "file",
    description: "MIDI file");
var portOption = new Option<string?>(
    aliases: new[] { "--port", "-p" },
    description: "MIDI out port (default: mapper)");
var mapOption = new Option<MidiMap?>(
    aliases: new[] { "--map", "-m" },
    description: "Inst map");
var debugOption = new Option<bool>(
    name: "--debug",
    description: "Show mciSendString");
rootCommand.AddGlobalOption(listOption);
rootCommand.AddArgument(fileArgument);
rootCommand.AddOption(portOption);
rootCommand.AddOption(mapOption);
rootCommand.AddOption(debugOption);
rootCommand.SetHandler((file, port, debug, list, map) =>
{
    if (list)
    {
        int all = midiOutGetNumDevs();
        for (int i = 0; i < all; i++)
        {
            MIDIOUTCAPS caps = new MIDIOUTCAPS();
            midiOutGetDevCapsA(i, ref caps, Marshal.SizeOf(typeof(MIDIOUTCAPS)));
            Console.WriteLine($"{i}: {caps.szPname}");
        }
        return;
    }

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
    }
},
fileArgument, portOption, debugOption, listOption, mapOption);
return rootCommand.Invoke(args);
