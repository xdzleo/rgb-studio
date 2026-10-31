using RgbStudio.Core;
using Xunit;

namespace RgbStudio.Tests;

/// <summary>What reached this machine's RAM sticks over SMBus in the hardware test.</summary>
public class EneSmbusTests
{
    [Fact]
    public void The_register_pointer_goes_byte_swapped()
    {
        Assert.Equal(0x0010UL, EneSmbusBackend.Ene.Pointer(0x1000));   // device name
        Assert.Equal(0x0081UL, EneSmbusBackend.Ene.Pointer(0x8100));   // first direct color
        Assert.Equal(0x2080UL, EneSmbusBackend.Ene.Pointer(0x8020));   // direct mode
    }

    [Fact]
    public void Colors_go_red_blue_green()
    {
        // Red read back as FF 00 00 and blue as 00 FF 00 on the sticks.
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x00 }, EneSmbusBackend.Ene.Rbg(new Rgb(255, 0, 0)));
        Assert.Equal(new byte[] { 0x00, 0xFF, 0x00 }, EneSmbusBackend.Ene.Rbg(new Rgb(0, 0, 255)));
        Assert.Equal(new byte[] { 0x10, 0x30, 0x20 }, EneSmbusBackend.Ene.Rbg(new Rgb(0x10, 0x20, 0x30)));
    }

    [Theory]
    [InlineData(0x50, false)] [InlineData(0x57, false)]   // the modules' SPD data
    [InlineData(0x6F, false)] [InlineData(0x78, false)]
    [InlineData(0x70, true)] [InlineData(0x71, true)] [InlineData(0x73, true)] [InlineData(0x77, true)]
    public void Only_the_light_controller_addresses_are_allowed(int address, bool allowed) =>
        Assert.Equal(allowed, EneSmbusBackend.Ene.AddressAllowed(address));
}
