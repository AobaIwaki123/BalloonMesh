using BalloonMesh.Server.Services;

namespace BalloonMesh.Server.Tests;

public sealed class PositionSolverTests
{
    private const double BaseLat = 35.63;
    private const double BaseLon = 139.77;

    private static GeoPoint Offset(double eastM, double northM)
    {
        var lat = BaseLat + northM / 111_320.0;
        var lon = BaseLon + eastM / (111_320.0 * Math.Cos(BaseLat * Math.PI / 180.0));
        return new GeoPoint(lat, lon);
    }

    private static double Distance(GeoPoint left, GeoPoint right) =>
        SimulationService.DistanceMeters(left.Latitude, left.Longitude, right.Latitude, right.Longitude);

    [Fact]
    public void ThreeAnchorsRecoverKnownPoint()
    {
        var target = Offset(30, 40);
        var constraints = new[]
        {
            new RangeConstraint(Offset(0, 0), Distance(Offset(0, 0), target), 1.0, "ftm"),
            new RangeConstraint(Offset(100, 0), Distance(Offset(100, 0), target), 1.0, "ftm"),
            new RangeConstraint(Offset(0, 100), Distance(Offset(0, 100), target), 1.0, "ftm")
        };

        var solution = new PositionSolver().Solve(constraints, null);

        Assert.NotNull(solution);
        Assert.Equal("multilateration", solution!.Method);
        Assert.True(Math.Abs(solution.Position.Latitude - target.Latitude) < 1e-7);
        Assert.True(Math.Abs(solution.Position.Longitude - target.Longitude) < 1e-7);
        Assert.True(solution.Confidence > 0.7);
    }

    [Fact]
    public void TwoCirclesChooseCandidateNearestPreviousPosition()
    {
        var anchor1 = Offset(0, 0);
        var anchor2 = Offset(100, 0);
        var northCandidate = Offset(50, 43.3);
        var distance = Distance(anchor1, northCandidate);
        var constraints = new[]
        {
            new RangeConstraint(anchor1, distance, 1.0, "ftm"),
            new RangeConstraint(anchor2, distance, 1.0, "ftm")
        };
        var previous = Offset(50, 60);

        var solution = new PositionSolver().Solve(constraints, previous);

        Assert.NotNull(solution);
        Assert.Equal("two-circle", solution!.Method);
        Assert.True(solution.Position.Latitude > BaseLat + 30.0 / 111_320.0);
    }

    [Fact]
    public void SingleRangeKeepsDirectionFromPreviousPosition()
    {
        var anchor = Offset(0, 0);
        var previous = Offset(0, 10);
        var solution = new PositionSolver().Solve(
            [new RangeConstraint(anchor, 100, 1.0, "ftm")], previous);

        Assert.NotNull(solution);
        Assert.Equal("single-range", solution!.Method);
        var north = Distance(anchor, solution.Position);
        Assert.InRange(north, 99.0, 101.0);
        Assert.True(solution.Position.Latitude > anchor.Latitude);
    }

    [Fact]
    public void SingleRangeWithoutPreviousPositionDoesNotInventAPoint()
    {
        var anchor = Offset(0, 0);
        var solution = new PositionSolver().Solve(
            [new RangeConstraint(anchor, 100, 1.0, "ftm")], null);

        Assert.Null(solution);
    }

    [Fact]
    public void NeighborConstraintUsesLowerWeightButParticipates()
    {
        var target = Offset(20, 20);
        var constraints = new[]
        {
            new RangeConstraint(Offset(0, 0), Distance(Offset(0, 0), target), 1.0, "ftm"),
            new RangeConstraint(Offset(100, 0), Distance(Offset(100, 0), target), 1.0, "ftm"),
            new RangeConstraint(Offset(0, 100), Distance(Offset(0, 100), target), 0.15, "neighbor")
        };

        var solution = new PositionSolver().Solve(constraints, null);

        Assert.NotNull(solution);
        Assert.Equal("weighted-multilateration", solution!.Method);
        Assert.True(Math.Abs(solution.Position.Latitude - target.Latitude) < 2e-6);
    }
}
