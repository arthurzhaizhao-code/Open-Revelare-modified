using OpenRevelare.Gui.Controls;
using Xunit;

namespace OpenRevelare.Tests;

public sealed class VectorscopeDataTests
{
    private static float[] Solid(int width, int height, float r, float g, float b)
    {
        var data = new float[width * height * 3];
        for (int i = 0; i < data.Length; i += 3)
        {
            data[i] = r;
            data[i + 1] = g;
            data[i + 2] = b;
        }
        return data;
    }

    private static (int X, int Y) OccupiedCell(VectorscopeData data)
    {
        int cell = Array.FindIndex(data.Counts, count => count > 0);
        Assert.True(cell >= 0);
        return (cell % data.Size, cell / data.Size);
    }

    [Fact]
    public void Neutral_samples_land_at_the_centre()
    {
        VectorscopeData data = VectorscopeData.FromBuffer(Solid(16, 16, 0.5f, 0.5f, 0.5f),
                                                          16, 16, size: 64);

        Assert.Equal((32, 32), OccupiedCell(data));
    }

    [Fact]
    public void Red_samples_land_above_and_left_of_centre()
    {
        VectorscopeData data = VectorscopeData.FromBuffer(Solid(16, 16, 1f, 0f, 0f),
                                                          16, 16, size: 64);
        var (x, y) = OccupiedCell(data);

        Assert.True(x < data.Size / 2);
        Assert.True(y < data.Size / 2);
    }

    [Fact]
    public void Invalid_values_do_not_escape_the_plot()
    {
        VectorscopeData data = VectorscopeData.FromBuffer(
            Solid(4, 4, float.NaN, float.PositiveInfinity, float.NegativeInfinity),
            4, 4, size: 32);

        Assert.Equal(16, data.Counts.Sum());
        Assert.Equal((16, 16), OccupiedCell(data));
    }
}
