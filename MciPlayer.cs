using System.Runtime.InteropServices;
using System.Text;

public class MciPlayer : IPlayer
{
    private const string alias = "a";
    private readonly string file;
    private readonly string? port;
    private readonly bool debug;
    private readonly double length;
    private readonly string currentPort;

    public MciPlayer(string file, string? port = null, bool debug = false)
    {
        this.file = file;
        this.port = port;
        this.debug = debug;

        try
        {
            SendMciVoid($"open \"{file}\" alias {alias}", debug);
            SendMciVoid($"set {alias} time format ms");
            if (port != null)
            {
                SendMciVoid($"set {alias} port {port}", debug);
            }

            SendMciVoid($"play {alias}", debug);
            length = SendMci<double>($"status {alias} length", debug);
            currentPort = SendMci<string>($"status {alias} port", debug);
        }
        catch
        {
            Finally();
            throw;
        }
    }

    public bool Play()
    {
        var position = SendMci<double>($"status {alias} position", debug);
        string mode = SendMci<string>($"status {alias} mode", debug);
        Console.Write($"{mode} ({currentPort}) {TimeSpan.FromMilliseconds(position)} / {TimeSpan.FromMilliseconds(length)}\r");

        return position < length;
    }

    public void Finally()
    {
        SendMciVoid($"stop all", debug);
        SendMciVoid($"close all", debug);
        Console.WriteLine("");
    }

    [DllImport("winmm", CharSet = CharSet.Auto)]
    private static extern int mciSendString(string lpstrCommand, StringBuilder? lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

    private void SendMciVoid(string cmd, bool debug = false)
    {
        var result = mciSendString(cmd, null, 0, IntPtr.Zero);
        if (debug) Console.WriteLine(cmd);
        if (result != 0) throw new Exception($"{cmd} -> ${result}");
    }

    private T SendMci<T>(string cmd, bool debug = false)
    {
        var sb = new StringBuilder(127);
        var result = mciSendString(cmd, sb, sb.Capacity, IntPtr.Zero);
        if (debug) Console.WriteLine(cmd, typeof(T));
        if (result != 0) throw new Exception($"{cmd} -> ${result}");

        var s = sb.ToString();
        object ret;
        if (typeof(T) == typeof(string)) ret = s;
        else if (typeof(T) == typeof(int)) ret = int.Parse(s);
        else if (typeof(T) == typeof(double)) ret = double.Parse(s);
        else if (typeof(T) == typeof(TimeSpan)) ret = TimeSpan.Parse(s);
        else throw new Exception($"not supported type: {typeof(T)}");

        return (T)ret;
    }
}
