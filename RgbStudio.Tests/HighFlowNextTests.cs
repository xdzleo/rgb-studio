using RgbStudio.Core;
using Xunit;

namespace RgbStudio.Tests;

/// <summary>The settings report as this high flow NEXT returned it, before any change.</summary>
public class HighFlowNextTests
{
    const string Original =
        "0300010000000A0000000400000000000002BC02580000010001000100010001000100010001000001F43A0000000001" +
        "00000000000000000000000000000000000000000000C8012C01F402BC03E804E205DC07D009C40BB8FF000000010F13" +
        "0000FFFF0A0F0000006401640000006400FF0023000A0005005A00190000000000000000000000000000000000FF0100" +
        "FFFF00000000000000000000000000000000000F0F080040FFFF0F19000003E80164000003E801FF0004002C00010000" +
        "000000000000000000000000000000000000000F03E7FFFF00FEFFFF0000FFFF0000FFFF0000FFFF001E0F0B0000FFFF" +
        "0F19000003E80164000003E801FF001E0028000100060050000000000000000000000000000002FF02FF01FBFFFF0525" +
        "FFFF00C5FFFF03F5FFFF05F3FFFF002D0F040006FFFF0F19000003E80164000003E801FF002800050000000000000000" +
        "0000000000000000000000000000000F0000FFFF01FDFFFF03FFFFFF00FAFFFF01CE10FF003C0F030000FFFF0F190000" +
        "03E80164000003E801FF0032006400000000000000000000000000000000000000000000FFFF0000FFFF0000FFFF0000" +
        "FFFF0000FFFF0000FFFF004B0F030000FFFF0F19000003E80164000003E801FF00320064000000000000000000000000" +
        "00000000000000000000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF01000A0A008200001212000000961717" +
        "0000009601FF0011000000080001000000000000000000000000000000000000FFFF041EE2FF02FFFFFF000000000000" +
        "00000000000001000A000000FFFF0F19000003E80164000003E80164001E001900000000000000000000000000000000" +
        "000000000000FF0A0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00000000000001F403B600000000E002000A01C2" +
        "0FA011940BB80001FB0C";

    static byte[] Report() => Convert.FromHexString(Original);

    [Fact]
    public void Reads_this_sensors_lighting()
    {
        var c = HighFlowNextConfig.Parse(Report());
        Assert.Equal(255, c.Brightness);
        var ring = c[6];
        Assert.Equal((1, 0, 10, (byte)10), (ring.Strip, ring.FirstLed, ring.Count, ring.Mode));   // the ring, "Wave"
        Assert.Equal((0, (byte)255, (byte)255), ring.Hsv(0));                                       // background red
        Assert.Equal((1054, (byte)226, (byte)255), ring.Hsv(1));                                    // the violet-blue
        Assert.Equal(new[] { 1, 15, 30, 45, 60, 75 }, c.All.Take(6).Select(x => x.FirstLed));
        Assert.False(c[7].InUse);
    }

    [Fact]
    public void Writes_the_same_bytes_the_hardware_test_did()
    {
        // 15:43, the background went green: only 0x22E changed, and the CRC became 91AD.
        var c = HighFlowNextConfig.Parse(Report());
        c[6].SetHsv(0, 512, 255, 255);
        var r = c.ToReport();
        var before = Report();
        Assert.Equal(new[] { 0x22E, 0x2A8, 0x2A9 }, Enumerable.Range(0, r.Length).Where(i => r[i] != before[i]));
        Assert.Equal((byte)0x91, r[0x2A8]);
        Assert.Equal((byte)0xAD, r[0x2A9]);
        HighFlowNextConfig.Parse(r);   // and it reads back as valid
    }

    [Fact]
    public void Colors_convert_to_the_devices_hue_scale()
    {
        var c = HighFlowNextConfig.Parse(Report());
        c[6].SetColor(1, new Rgb(0, 0, 255));
        Assert.Equal((1024, (byte)255, (byte)255), c[6].Hsv(1));
        c[6].SetColor(1, new Rgb(0, 255, 0));
        Assert.Equal((512, (byte)255, (byte)255), c[6].Hsv(1));
        Assert.Equal(new Rgb(0, 255, 0), c[6].Color(1));
    }

    [Fact]
    public void Refuses_a_report_it_does_not_understand()
    {
        var bad = Report();
        bad[0x100] ^= 1;   // checksum no longer closes
        Assert.Throws<InvalidDataException>(() => HighFlowNextConfig.Parse(bad));
        var other = Report();
        other[2] = 2;      // another structure version
        Assert.Throws<InvalidDataException>(() => HighFlowNextConfig.Parse(other));
        Assert.Throws<InvalidDataException>(() => HighFlowNextConfig.Parse(Report().AsSpan(0, 600)));
    }

    [Fact]
    public void Only_lighting_changes_are_allowed_through()
    {
        var a = HighFlowNextConfig.Parse(Report());
        var b = a.Clone();
        b[3].Mode = 1;
        b.Brightness = 100;
        Assert.True(HighFlowNextConfig.OnlyLightingDiffers(a, b));

        var flow = Report();
        flow[0x46] ^= 1;   // flow calibration
        var fixedCrc = HighFlowNextConfig.Crc16Usb(flow.AsSpan(1, 0x2A7));
        flow[0x2A8] = (byte)(fixedCrc >> 8); flow[0x2A9] = (byte)fixedCrc;
        Assert.False(HighFlowNextConfig.OnlyLightingDiffers(a, HighFlowNextConfig.Parse(flow)));
    }
}
