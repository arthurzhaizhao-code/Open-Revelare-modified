using OpenRevelare.Gui.ViewModels;
using Xunit;

namespace OpenRevelare.Tests;

public class SingleFrameAutoTransactionTests
{
    [Fact]
    public void Lost_raw_base_is_recovered_from_sibling_medians()
    {
        double[]? resolved = SingleFrameFilmBase.Resolve(
            new[] { 0.0, 0.0, 0.0 },
            requirePhysicalRawBase: true,
            savedRollBase: null,
            new[]
            {
                new[] { 0.01, 0.08, 0.52 },
                new[] { 0.03, 0.10, 0.56 },
                new[] { 0.02, 0.09, 0.54 },
            });

        Assert.Equal(new[] { 0.02, 0.09, 0.54 }, resolved);
    }

    [Fact]
    public void Valid_current_base_always_wins()
    {
        double[] current = { 0.2, 0.3, 0.7 };
        double[]? resolved = SingleFrameFilmBase.Resolve(
            current,
            requirePhysicalRawBase: true,
            savedRollBase: null,
            new[] { new[] { 0.01, 0.02, 0.03 } });

        Assert.Equal(current, resolved);
        Assert.NotSame(current, resolved);
    }

    [Fact]
    public void Normalized_tiff_may_have_a_zero_base()
    {
        double[]? resolved = SingleFrameFilmBase.Resolve(
            new[] { 0.0, 0.0, 0.0 },
            requirePhysicalRawBase: false,
            savedRollBase: null,
            Array.Empty<double[]>());

        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, resolved);
    }

    [Fact]
    public void Highlight_failure_rolls_back_once()
    {
        int rollbacks = 0;

        bool committed = SingleFrameAutoTransaction.Run(
            () => false,
            () => rollbacks++);

        Assert.False(committed);
        Assert.Equal(1, rollbacks);
    }

    [Fact]
    public void Successful_measurements_commit_without_rollback()
    {
        int rollbacks = 0;

        bool committed = SingleFrameAutoTransaction.Run(
            () => true,
            () => rollbacks++);

        Assert.True(committed);
        Assert.Equal(0, rollbacks);
    }

    [Fact]
    public void Exception_rolls_back_before_it_escapes()
    {
        int rollbacks = 0;

        Assert.Throws<InvalidOperationException>(() => SingleFrameAutoTransaction.Run(
            () => throw new InvalidOperationException("failed"),
            () => rollbacks++));

        Assert.Equal(1, rollbacks);
    }
}
