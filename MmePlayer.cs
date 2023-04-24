
using System.Runtime.InteropServices;
using Commons.Music.Midi;

public class MmePlayer : IPlayer
{
    private bool debug = true;
    private IntPtr hmidi;

    public MmePlayer(string file, int port)
    {
        throw new NotImplementedException();

        var result = midiOutOpen(out hmidi, port, IntPtr.Zero, IntPtr.Zero, 0);
        if (debug) Console.WriteLine(nameof(midiOutOpen));
        if (result != 0)
        {
            throw new Exception("failed open");
        }

        try
        {
            byte[] data = new byte[4];
            uint msg = BitConverter.ToUInt32(data, 0);
            midiOutShortMsg(hmidi, msg);
        }
        catch
        {
            Finally();
            throw;
        }
    }

    public bool Play()
    {
        return false;
    }

    public void Finally()
    {
        midiOutReset(hmidi);
        if (debug) Console.WriteLine(nameof(midiOutReset));
        midiOutClose(hmidi);
        if (debug) Console.WriteLine(nameof(midiOutClose));
    }

    [DllImport("winmm", CharSet = CharSet.Auto)]
    private static extern uint midiOutOpen(out IntPtr lphMidiOut, int uDeviceID, IntPtr dwCallback, IntPtr dwInstance, int dwFlags);

    [DllImport("winmm")]
    private static extern uint midiOutShortMsg(IntPtr hMidiOut, uint dwMsg);
    
    [DllImport("winmm")]
    private static extern uint midiOutReset(IntPtr hMidiOut);

    [DllImport("winmm")]
    private static extern uint midiOutClose(IntPtr hMidiOut);

    public void SetMap(MidiMap map)
    {
        throw new NotImplementedException();
    }
}
