using AwesomeAssertions;
using FsrsSharp.Legacy;
using Xunit;

namespace FsrsSharp.Tests.Legacy;

/// <summary>Port of <c>show_diff_message.test.ts</c>. The upstream cases that feed strings and numbers (the
/// <c>DateInput</c> union) use <see cref="DateTime"/>s of the same instants here.</summary>
public class ShowDiffMessageTests
{
    private static readonly string[] Chinese = ["秒", "分", "时", "天", "月", "年"];
    private static readonly string[] ChineseHour = ["秒", "分", "小时", "天", "月", "年"];
    private static readonly string[] ChineseMonth = ["秒", "分", "小时", "天", "个月", "年"];

    private static readonly DateTime T1 = DateTime.UtcNow;

    private static DateTime AddDays(double days) => T1.AddMilliseconds(1000 * 60 * 60 * 24 * days);

    private static string Show(DateTime due, DateTime last, bool unit = false, IReadOnlyList<string>? timeUnit = null) =>
        LegacyHelpers.ShowDiffMessage(due, last, unit, timeUnit);

    [Fact]
    public void BadType_ShouldWorkForEveryInstantRepresentation()
    {
        // ts-fsrs issue 19: ISO strings, local date-time strings and epoch numbers are all the same instants here.
        var t1 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(1970, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Local);
        var t4 = new DateTime(1970, 1, 2, 0, 0, 0, DateTimeKind.Local);
        var t5 = DateTime.UnixEpoch;
        var t6 = DateTime.UnixEpoch.AddMilliseconds(1000 * 60 * 60 * 24);

        foreach (var (due, last) in new[] { (t2, t1), (t4, t3), (t6, t5) })
        {
            Show(due, last).Should().Be("1");
            Show(due, last, true).Should().Be("1day");
            Show(due, last, true, Chinese).Should().Be("1天");
        }
    }

    [Fact]
    public void Min()
    {
        var t2 = T1.AddMilliseconds(1000 * 60);
        var t3 = T1.AddMilliseconds(1000 * 60 * 59);
        Show(t2, T1).Should().Be("1");
        Show(t2, T1, true).Should().Be("1min");
        Show(t2, T1, true, Chinese).Should().Be("1分");

        Show(t3, T1, true).Should().Be("59min");
        Show(t3, T1, true, Chinese).Should().Be("59分");
    }

    [Fact]
    public void Hour()
    {
        var t2 = T1.AddMilliseconds(1000 * 60 * 60);
        var t3 = T1.AddMilliseconds(1000 * 60 * 60 * 59.0);
        Show(t2, T1).Should().Be("1");

        Show(t2, T1, true).Should().Be("1hour");
        Show(t2, T1, true, ChineseHour).Should().Be("1小时");

        Show(t3, T1, true).Should().NotBe("59hour");
        Show(t3, T1, true, ChineseHour).Should().NotBe("59小时");

        Show(t3, T1, true).Should().Be("2day");
        Show(t3, T1, true, Chinese).Should().Be("2天");
    }

    [Fact]
    public void Day()
    {
        var t2 = AddDays(1);
        var t3 = AddDays(30);
        var t4 = AddDays(31);
        Show(t2, T1).Should().Be("1");
        Show(t2, T1, true).Should().Be("1day");
        Show(t2, T1, true, ChineseMonth).Should().Be("1天");

        Show(t3, T1).Should().Be("30");
        Show(t3, T1, true).Should().Be("30day");
        Show(t3, T1, true, ChineseMonth).Should().Be("30天");

        Show(t4, T1).Should().NotBe("31");
        Show(t4, T1, true).Should().Be("1month");
        Show(t4, T1, true, ChineseMonth).Should().Be("1个月");
    }

    [Fact]
    public void Month()
    {
        var t2 = AddDays(31);
        var t3 = AddDays(31 * 12);
        var t4 = AddDays(31 * 13);
        Show(t2, T1).Should().Be("1");
        Show(t2, T1, true).Should().Be("1month");
        Show(t2, T1, true, ChineseMonth).Should().Be("1个月");

        Show(t3, T1).Should().NotBe("12");
        Show(t3, T1, true).Should().NotBe("12month");
        Show(t3, T1, true, ChineseMonth).Should().NotBe("12个月");

        Show(t4, T1).Should().NotBe("13");
        Show(t4, T1, true).Should().Be("1year");
        Show(t4, T1, true, ChineseMonth).Should().Be("1年");
    }

    [Fact]
    public void Year()
    {
        var t2 = AddDays(31 * 13);
        var t3 = AddDays(31 * 13 + 1);
        var t4 = AddDays(31 * 24 + 1);
        Show(t2, T1).Should().Be("1");
        Show(t2, T1, true).Should().Be("1year");
        Show(t2, T1, true, ChineseMonth).Should().Be("1年");

        Show(t3, T1).Should().Be("1");
        Show(t3, T1, true).Should().Be("1year");
        Show(t3, T1, true, ChineseMonth).Should().Be("1年");

        Show(t4, T1).Should().Be("2");
        Show(t4, T1, true).Should().Be("2year");
        Show(t4, T1, true, ChineseMonth).Should().Be("2年");
    }

    [Fact]
    public void WrongTimeUnitLength_ShouldFallBackToTheDefaultNames()
    {
        string[] wrong = ["年"];
        var t2 = AddDays(31 * 13);
        Show(t2, T1).Should().Be("1");
        Show(t2, T1, true).Should().Be("1year");
        Show(t2, T1, true, wrong).Should().NotBe("1年");
        Show(t2, T1, true, wrong).Should().Be("1year");
    }
}
