using Eleris.TiffNet.Internal.Writing;

namespace Eleris.TiffNet.Tests.Writing;

/// <summary>One channel of interleaved pixels into a plane: the shuffles give the samples of the loop, and nothing beyond.</summary>
public class ChannelsTests
{
    [Fact]
    public void Every_channel_of_every_size_is_extracted_as_one_by_one()
    {
        var random = new Random(111);
        foreach (int channels in new[] { 1, 2, 3, 4, 5 })
        {
            foreach (int size in new[] { 1, 2, 4, 8 })
            {
                foreach (int count in Enumerable.Range(0, 70).Concat([257, 1000]))
                {
                    byte[] pixels = new byte[count * channels * size];
                    random.NextBytes(pixels);
                    for (int channel = 0; channel < channels; channel++)
                    {
                        byte[] expected = new byte[count * size];
                        for (int p = 0; p < count; p++)
                        {
                            Array.Copy(pixels, (p * channels + channel) * size, expected, p * size, size);
                        }

                        byte[] plane = new byte[count * size + 16];
                        Array.Fill(plane, (byte)0xEE);
                        Channels.Extract(pixels, channels, channel, size, count, plane);

                        if (!expected.AsSpan().SequenceEqual(plane.AsSpan(0, count * size)) || plane[(count * size)..].Any(b => b != 0xEE))
                        {
                            Assert.Fail($"{channels} channels of {size} bytes, {count} pixels, channel {channel}");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void The_shuffles_never_read_beyond_the_pixels_they_are_given()
    {
        // A read in planar order gives the samples from the one of the channel: the last pixel may lack its later channels.
        byte[] pixels = [.. Enumerable.Range(0, 3 * 2 * 40).Select(i => (byte)i)];
        foreach (int cut in new[] { 0, 1, 2, 3, 4 })
        {
            var given = pixels.AsSpan(2, pixels.Length - 2 - cut);
            byte[] plane = new byte[80];
            int done = Channels.Vectorised(given, 3, 0, 2, 40, plane);

            Assert.True(done * 6 <= given.Length);
            for (int p = 0; p < done; p++)
            {
                Assert.Equal(given[p * 6], plane[p * 2]);
                Assert.Equal(given[p * 6 + 1], plane[p * 2 + 1]);
            }
        }
    }
}
