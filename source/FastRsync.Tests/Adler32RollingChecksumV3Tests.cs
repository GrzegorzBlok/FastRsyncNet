using FastRsync.Hash;
using NUnit.Framework;
using System;

namespace FastRsync.Tests;

[TestFixture]
public class Adler32RollingChecksumV3Tests
{
    [Test]
    public void Adler32RollingChecksumV3_CalculatesChecksum()
    {
        // Arrange
        var data1 = "Adler32 checksum test"u8.ToArray();
        var data2 = "Fast Rsync Fast Rsync"u8.ToArray();
        var data3 = "~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~"u8.ToArray();
        var data4 = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Duis malesuada turpis non libero faucibus sodales. Mauris eget justo est. Pellentesque."u8.ToArray();
        var data5 = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum."u8.ToArray();

        // Act
        var checksum1 = new Adler32RollingChecksumV3().Calculate(data1, 0, data1.Length);
        var checksum2 = new Adler32RollingChecksumV3().Calculate(data2, 0, data2.Length);
        var checksum3 = new Adler32RollingChecksumV3().Calculate(data3, 0, data3.Length);
        var checksum4 = new Adler32RollingChecksumV3().Calculate(data4, 0, data4.Length);
        var checksum5 = new Adler32RollingChecksumV3().Calculate(data5, 0, data5.Length);

        // Assert
        Assert.That(checksum1, Is.EqualTo(0x4ff907a1));
        Assert.That(checksum2, Is.EqualTo(0x5206079b));
        Assert.That(checksum3, Is.EqualTo(0x040f0fc1));
        Assert.That(checksum4, Is.EqualTo(0x2d10357d));
        Assert.That(checksum5, Is.EqualTo(0xa05ca509));
    }


    [Test]
    public void Adler32RollingChecksumV3_RotatesChecksum()
    {
        //Arrange
        const int len = 8 * 1024;
        const int windowSize = len - 1;
        var bytes = new byte[len];
        new Random().NextBytes(bytes);
        var adler = new Adler32RollingChecksumV3();

        // Act
        var rotatedChecksum = adler.Rotate(adler.Calculate(bytes, 0, windowSize), bytes[0], bytes[^1], windowSize);
        var checksum = adler.Calculate(bytes, 1, windowSize);

        // Assert
        Assert.That(rotatedChecksum, Is.EqualTo(checksum));
    }
}