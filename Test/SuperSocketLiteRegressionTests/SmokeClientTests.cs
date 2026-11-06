using System;
using System.Linq;
using SuperSocketLite.SmokeClient;

static class SmokeClientTests
{
    public static void LengthBoundsMatchTheWireFormat()
    {
        foreach (var bigEndian in new[] { false, true })
        {
            foreach (var includesHeader in new[] { false, true })
            {
                foreach (var idBytes in new[] { 0, 2 })
                {
                    var flags = new[] { "--port", "12345", "--id-bytes", idBytes.ToString() }
                        .Concat(bigEndian ? new[] { "--big-endian" } : Array.Empty<string>())
                        .Concat(includesHeader ? Array.Empty<string>() : new[] { "--length-excludes-header" }).ToArray();
                    var maxBody = short.MaxValue - (includesHeader ? 2 + idBytes : 0);
                    var valid = Options.Parse(flags.Concat(new[] { "--size", maxBody.ToString() }).ToArray(), out var error);
                    Assert.True(valid != null && error == null, "the signed 16-bit boundary should be accepted");
                    var codec = new PacketCodec(valid!);
                    var packet = codec.Encode(-12, valid!.Body);
                    Assert.Equal(maxBody, codec.GetRemainingBodyLength(packet), "boundary length must round-trip in either byte order");
                    if (idBytes != 0)
                    {
                        Assert.Equal((short)-12, codec.ReadPacketId(packet), "signed packet IDs must remain supported");
                    }
                    var invalid = Options.Parse(flags.Concat(new[] { "--size", (maxBody + 1).ToString() }).ToArray(), out error);
                    Assert.True(invalid == null && error != null, "oversized requests must fail before connecting");
                    var rejected = false;
                    try
                    {
                        codec.Encode(101, new byte[maxBody + 1]);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        rejected = true;
                    }
                    Assert.True(rejected, "the codec must not silently truncate an oversized length");
                }
            }
            var wideFlags = new[] { "--port", "12345", "--len-bytes", "4", "--size", "65000" }
                .Concat(bigEndian ? new[] { "--big-endian" } : Array.Empty<string>()).ToArray();
            var wide = Options.Parse(wideFlags, out _)!;
            var wideCodec = new PacketCodec(wide);
            Assert.Equal(65000, wideCodec.GetRemainingBodyLength(wideCodec.Encode(101, wide.Body)),
                "four-byte lengths must retain larger payload support");
        }
    }

    public static void OneWayModeCannotClaimResponseValidation()
    {
        foreach (var expectation in new[] { new[] { "--expect-echo" }, new[] { "--expect-id", "102" } })
        {
            var options = Options.Parse(new[] { "--port", "12345", "--no-wait-response" }.Concat(expectation).ToArray(), out var error);
            Assert.True(options == null && error != null, "one-way mode cannot report success for a response it never read");
        }
    }
}
