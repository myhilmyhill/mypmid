public static class Debug
{
    public static string BytesToHex(IEnumerable<byte> bytes)
    {
        return string.Join(" ", bytes.Select(b => b.ToString("x2")));
    }

    public static void PrintBytesToHex(IEnumerable<byte> bytes)
    {
        Console.WriteLine(BytesToHex(bytes));
    }
}