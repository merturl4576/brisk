using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

/// One predicate, because three shipped surfaces used to hold two opinions.
/// `brisk scan`'s notice asked `is not null`, the GUI's snapshot asked
/// `is not null && double.IsFinite`, and the thermals rule asked a third copy
/// of the second — so on a machine whose sensor reports NaN the console said
/// the sensor answered while the card said it did not. Both cannot be right
/// about the same reading, and this is the one place that decides.
///
/// NaN is the case that matters and the reason the predicate is not just a
/// null check: a present-but-silent sensor reports it, it fails every
/// threshold so nothing calls it hot, and printed it renders "CPU NaN°C".
public class SensorReadingTests
{
    [Theory]
    [InlineData(55.0)]
    [InlineData(0.0)]
    [InlineData(-40.0)]
    [InlineData(105.5)]
    public void AFiniteNumber_IsAReading(double celsius) =>
        Assert.True(SensorReading.IsReal(celsius));

    [Fact]
    public void NoAnswerAtAll_IsNotAReading() =>
        Assert.False(SensorReading.IsReal(null));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonFiniteNumber_IsNotAReading(double celsius) =>
        Assert.False(SensorReading.IsReal(celsius));
}
