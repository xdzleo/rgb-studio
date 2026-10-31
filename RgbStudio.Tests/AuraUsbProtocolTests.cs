using RgbStudio.Core;
using Xunit;

namespace RgbStudio.Tests;

/// <summary>The exact bytes that reached this board's LEDs in the hardware test.</summary>
public class AuraUsbProtocolTests
{
    // This board's real replies.
    static byte[] Hex(string s)
    {
        var bytes = s.Split(' ').Select(b => Convert.ToByte(b, 16)).ToArray();
        var r = new byte[65];
        bytes.CopyTo(r, 0);
        return r;
    }
    static readonly byte[] FirmwareReply = Hex("EC 02 41 55 4C 41 33 2D 41 52 33 32 2D 30 33 30 39");
    static readonly byte[] ConfigReply = Hex("EC 30 00 00 10 1F 03 01 02 00 78 3C 00 01 00 00 78 3C 00 01 00 00 78 3C 00 00 00 00 00 00 00 03 06 02 01 F4");

    [Fact]
    public void Reads_this_boards_firmware_and_config()
    {
        Assert.Equal("AULA3-AR32-0309", AuraUsbProtocol.ParseFirmware(FirmwareReply));
        Assert.Equal((3, 3), AuraUsbProtocol.ParseConfig(ConfigReply));
    }

    [Fact]
    public void A_reply_to_something_else_is_not_read_as_valid()
    {
        Assert.Equal("", AuraUsbProtocol.ParseFirmware(ConfigReply));
        Assert.Equal((0, 0), AuraUsbProtocol.ParseConfig(FirmwareReply));
    }

    [Fact]
    public void Setup_reports_match_the_sequence()
    {
        Assert.Equal(Hex("EC 52 53 00 01"), AuraUsbProtocol.SetGen1());
        Assert.Equal(Hex("EC 35 03 00 00 FF"), AuraUsbProtocol.DirectMode(3));
        Assert.Equal(Hex("EC 82"), AuraUsbProtocol.Query(AuraUsbProtocol.RequestFirmware));
    }

    [Fact]
    public void Direct_on_the_mainboard_is_one_report_on_channel_4_with_apply()
    {
        var c = new Rgb[] { new(1, 2, 3), new(4, 5, 6), new(7, 8, 9) };
        var reports = AuraUsbProtocol.Direct(AuraUsbProtocol.MainboardDirectChannel, c).ToList();
        Assert.Single(reports);
        Assert.Equal(Hex("EC 40 84 00 03 01 02 03 04 05 06 07 08 09"), reports[0]);
    }

    [Fact]
    public void Direct_on_120_leds_is_six_reports_and_only_the_last_applies()
    {
        var c = Enumerable.Repeat(new Rgb(9, 9, 9), 120).ToArray();
        var reports = AuraUsbProtocol.Direct(1, c).ToList();
        Assert.Equal(6, reports.Count);
        Assert.All(reports, r => { Assert.Equal(65, r.Length); Assert.Equal(0xEC, r[0]); Assert.Equal(0x40, r[1]); Assert.Equal(20, r[4]); });
        Assert.Equal(new byte[] { 0x01, 0x01, 0x01, 0x01, 0x01, 0x81 }, reports.Select(r => r[2]).ToArray());
        Assert.Equal(new byte[] { 0, 20, 40, 60, 80, 100 }, reports.Select(r => r[3]).ToArray());
    }

    [Fact]
    public void A_partial_last_report_carries_only_its_leds()
    {
        var reports = AuraUsbProtocol.Direct(0, Enumerable.Repeat(new Rgb(5, 5, 5), 23).ToArray()).ToList();
        Assert.Equal(2, reports.Count);
        Assert.Equal(3, reports[1][4]);
        Assert.Equal(0x80, reports[1][2]);
        Assert.Equal(0, reports[1][5 + 3 * 3]);   // nothing past the third LED
    }

    [Fact]
    public void No_report_this_app_builds_is_the_commit()
    {
        Assert.NotEqual(0x3F, AuraUsbProtocol.SetGen1()[1]);
        Assert.NotEqual(0x3F, AuraUsbProtocol.DirectMode(0)[1]);
        Assert.All(AuraUsbProtocol.Direct(0, new Rgb[25]), r => Assert.NotEqual(0x3F, r[1]));
    }

    [Fact]
    public void The_power_limit_caps_every_led_whatever_the_effect_asks()
    {
        var backend = new SimulatedBackend();
        var s = new AppSettings { Gamma = false, HeaderMilliamps = 0, PowerLimit = 50 };
        var p = s.Builtin("static").Clone();
        p.Color1 = "#FFFFFF";
        s.Put(p);
        s.ActivePreset = p.Id;
        var engine = new Engine(s, () => backend);
        typeof(Engine).GetProperty("Devices")!.SetValue(engine, backend.Open());
        engine.RenderFrame(backend, 0);
        Assert.All(backend.Written[3], c => Assert.Equal(new Rgb(128, 128, 128), c));
    }
}
