namespace CxSql.Models;

public static class UnixTime
{
    public static long NowMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
