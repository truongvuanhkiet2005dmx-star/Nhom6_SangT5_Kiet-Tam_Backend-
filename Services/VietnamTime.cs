namespace SmartMoney.Api.Services;

public static class VietnamTime
{
    private static readonly TimeZoneInfo VietnamZone = GetVietnamZone();

    public static DateTime GetUtcNow()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamZone);
        return TimeZoneInfo.ConvertTimeToUtc(local, VietnamZone);
    }

    public static (int Year, int Month) GetCurrentYearMonth()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamZone);
        return (local.Year, local.Month);
    }

    public static DateTime ToVietnamDateTimeUtc(DateTime utcValue)
    {
        var valueUtc = NormalizeUtc(utcValue);
        return TimeZoneInfo.ConvertTimeFromUtc(valueUtc, VietnamZone);
    }

    public static (DateTime StartUtc, DateTime EndUtc) GetMonthUtcRange(int year, int month)
    {
        var startLocal = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var endLocal = startLocal.AddMonths(1);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, VietnamZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, VietnamZone);
        return (startUtc, endUtc);
    }

    public static DateTime NormalizeUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return value;

        if (value.Kind == DateTimeKind.Local)
            return value.ToUniversalTime();

        return TimeZoneInfo.ConvertTimeToUtc(
            value,
            VietnamZone);
    }

    public static DateTime NormalizeUtc(DateTimeOffset value)
        => value.ToUniversalTime().UtcDateTime;

    private static TimeZoneInfo GetVietnamZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Vietnam.Custom",
            TimeSpan.FromHours(7),
            "Vietnam Time",
            "Vietnam Time");
    }
}
