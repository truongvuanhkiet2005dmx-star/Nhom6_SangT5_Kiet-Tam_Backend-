using SmartMoney.Api.Services;

namespace SmartMoney.Api.Tests;

public class VietnamTimeTests
{
    [Fact]
    public void MonthRange_UsesVietnamTimeOffset()
    {
        var (startUtc, endUtc) = VietnamTime.GetMonthUtcRange(2026, 3);

        Assert.Equal(new DateTime(2026, 2, 28, 17, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 3, 31, 17, 0, 0, DateTimeKind.Utc), endUtc);
    }

    [Fact]
    public void NormalizeUtc_ConvertsDateTimeOffsetToUtc()
    {
        var value = new DateTimeOffset(2026, 3, 1, 1, 30, 0, TimeSpan.FromHours(7));

        var utc = VietnamTime.NormalizeUtc(value);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 2, 28, 18, 30, 0, DateTimeKind.Utc), utc);
    }
}