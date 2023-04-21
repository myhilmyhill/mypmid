using System.Runtime.InteropServices;
using System.Text;

[DllImport("winmm", CharSet = CharSet.Auto)]
static extern int mciSendString(string lpstrCommand, StringBuilder? lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

void SendMciVoid(string cmd, bool debug = false)
{
    var result = mciSendString(cmd, null, 0, IntPtr.Zero);
    if (debug) Console.WriteLine(cmd);
    if (result != 0) throw new Exception($"{cmd} -> ${result}");
}

T SendMci<T>(string cmd, bool debug = false)
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

void Finally(bool debug = false)
{
    SendMciVoid($"stop all", debug);
    SendMciVoid($"close all", debug);
    Console.WriteLine("");
}

const string alias = "a";
string file = args.ElementAtOrDefault(0) ?? throw new ArgumentNullException("file");
string? port = args.ElementAtOrDefault(1);
bool debug = false;
Console.CancelKeyPress += (_, _) =>
{
    Finally(debug);
};

try
{
    SendMciVoid($"open \"{file}\" alias {alias}", debug);
    SendMciVoid($"set {alias} time format ms");
    if (port != null)
    {
        SendMciVoid($"set {alias} port {port}", debug);
    }

    SendMciVoid($"play {alias}", debug);
    var length = SendMci<double>($"status {alias} length", debug);
    var currentPort = SendMci<string>($"status {alias} port", debug);

    for (; ; )
    {
        var position = SendMci<double>($"status {alias} position", debug);
        string mode = SendMci<string>($"status {alias} mode", debug);
        Console.Write($"{mode} ({currentPort}) {TimeSpan.FromMilliseconds(position)} / {TimeSpan.FromMilliseconds(length)}\r");

        if (position >= length) return;
    }
}
finally
{
    Finally(debug);
}
