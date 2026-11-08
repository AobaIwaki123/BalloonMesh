namespace BalloonMesh.Server.Services;

public readonly record struct GeoPoint(double Latitude, double Longitude);

public sealed record RangeConstraint(GeoPoint Position, double DistanceMeters, double Weight, string Source);

public sealed record PositionSolution(GeoPoint Position, double Confidence, string Method, double ResidualMeters);

/// <summary>
/// Small-area 2D multilateration.  For a 50-balloon installation the operating
/// area is a few hundred metres, so an equirectangular local frame is ample.
/// </summary>
public sealed class PositionSolver
{
    private const double MetersPerDegreeLatitude = 111_320.0;

    public PositionSolution? Solve(IReadOnlyList<RangeConstraint> constraints, GeoPoint? previous)
    {
        var usable = constraints
            .Where(c => c.DistanceMeters > 0 && double.IsFinite(c.DistanceMeters))
            .OrderBy(c => c.Source == "ftm" ? 0 : 1)
            .ToArray();
        if (usable.Length == 0)
        {
            return previous is null
                ? null
                : new PositionSolution(previous.Value, 0.05, "previous-only", double.NaN);
        }

        var reference = previous ?? usable[0].Position;
        var metersPerDegreeLongitude = Math.Max(
            1.0,
            MetersPerDegreeLatitude * Math.Cos(reference.Latitude * Math.PI / 180.0));

        double ToX(double longitude) => (longitude - reference.Longitude) * metersPerDegreeLongitude;
        double ToY(double latitude) => (latitude - reference.Latitude) * MetersPerDegreeLatitude;
        GeoPoint FromLocal(double x, double y) => new(
            reference.Latitude + y / MetersPerDegreeLatitude,
            reference.Longitude + x / metersPerDegreeLongitude);

        var points = usable.Select(c => (X: ToX(c.Position.Longitude), Y: ToY(c.Position.Latitude))).ToArray();
        double? x = null;
        double? y = null;
        string method;

        if (usable.Length == 1)
        {
            var anchor = points[0];
            var distance = usable[0].DistanceMeters;
            if (previous is { } prior)
            {
                var px = ToX(prior.Longitude);
                var py = ToY(prior.Latitude);
                var dx = px - anchor.X;
                var dy = py - anchor.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 0.01)
                {
                    dx = 0;
                    dy = 1;
                    length = 1;
                }
                x = anchor.X + dx / length * distance;
                y = anchor.Y + dy / length * distance;
            }
            else
            {
                x = anchor.X;
                y = anchor.Y;
            }
            method = "single-range";
        }
        else if (usable.Length == 2)
        {
            var p1 = points[0];
            var p2 = points[1];
            var d1 = usable[0].DistanceMeters;
            var d2 = usable[1].DistanceMeters;
            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var separation = Math.Sqrt(dx * dx + dy * dy);
            if (separation < 0.01)
            {
                x = p1.X;
                y = p1.Y;
                method = "two-circle-degenerate";
            }
            else
            {
                var a = (d1 * d1 - d2 * d2 + separation * separation) / (2 * separation);
                var hSquared = d1 * d1 - a * a;
                var h = hSquared > 0 ? Math.Sqrt(hSquared) : 0;
                var baseX = p1.X + a * dx / separation;
                var baseY = p1.Y + a * dy / separation;
                var perpX = -dy / separation;
                var perpY = dx / separation;
                var candidate1 = (X: baseX + h * perpX, Y: baseY + h * perpY);
                var candidate2 = (X: baseX - h * perpX, Y: baseY - h * perpY);

                if (previous is { } prior)
                {
                    var px = ToX(prior.Longitude);
                    var py = ToY(prior.Latitude);
                    double Distance((double X, double Y) c) =>
                        (c.X - px) * (c.X - px) + (c.Y - py) * (c.Y - py);
                    x = Distance(candidate1) <= Distance(candidate2) ? candidate1.X : candidate2.X;
                    y = Distance(candidate1) <= Distance(candidate2) ? candidate1.Y : candidate2.Y;
                }
                else
                {
                    x = candidate1.X;
                    y = candidate1.Y;
                }
                method = "two-circle";
            }
        }
        else
        {
            var origin = points[0];
            var d0 = usable[0].DistanceMeters;
            double a11 = 0, a12 = 0, a22 = 0, b1 = 0, b2 = 0;
            for (var i = 1; i < usable.Length; i++)
            {
                var weight = Math.Clamp(usable[i].Weight, 0.01, 4.0);
                var ax = 2 * (points[i].X - origin.X);
                var ay = 2 * (points[i].Y - origin.Y);
                var di = usable[i].DistanceMeters;
                var rhs = d0 * d0 - di * di
                          + points[i].X * points[i].X - origin.X * origin.X
                          + points[i].Y * points[i].Y - origin.Y * origin.Y;
                a11 += weight * ax * ax;
                a12 += weight * ax * ay;
                a22 += weight * ay * ay;
                b1 += weight * ax * rhs;
                b2 += weight * ay * rhs;
            }

            var determinant = a11 * a22 - a12 * a12;
            if (Math.Abs(determinant) > 1e-6)
            {
                x = (b1 * a22 - b2 * a12) / determinant;
                y = (a11 * b2 - a12 * b1) / determinant;
                method = usable.Any(c => c.Source != "ftm") ? "weighted-multilateration" : "multilateration";
            }
            else
            {
                var weightedX = 0.0;
                var weightedY = 0.0;
                var totalWeight = 0.0;
                foreach (var point in points)
                {
                    weightedX += point.X;
                    weightedY += point.Y;
                    totalWeight += 1;
                }
                x = weightedX / totalWeight;
                y = weightedY / totalWeight;
                method = "centroid";
            }
        }

        if (x is null || y is null || !double.IsFinite(x.Value) || !double.IsFinite(y.Value))
        {
            return previous is null
                ? null
                : new PositionSolution(previous.Value, 0.05, "previous-fallback", double.NaN);
        }

        var solution = FromLocal(x.Value, y.Value);
        if (solution.Latitude is < -90 or > 90 || solution.Longitude is < -180 or > 180)
        {
            return previous is null
                ? null
                : new PositionSolution(previous.Value, 0.05, "invalid-fallback", double.NaN);
        }

        var squaredError = 0.0;
        var weightSum = 0.0;
        foreach (var constraint in usable)
        {
            var cx = ToX(constraint.Position.Longitude);
            var cy = ToY(constraint.Position.Latitude);
            var predicted = Math.Sqrt((x.Value - cx) * (x.Value - cx) + (y.Value - cy) * (y.Value - cy));
            var error = predicted - constraint.DistanceMeters;
            squaredError += constraint.Weight * error * error;
            weightSum += constraint.Weight;
        }
        var residual = weightSum > 0 ? Math.Sqrt(squaredError / weightSum) : double.NaN;

        var baseConfidence = usable.Length switch
        {
            >= 4 => 0.90,
            3 => 0.75,
            2 => 0.50,
            _ => 0.28
        };
        var penalty = double.IsFinite(residual) ? Math.Min(0.8, residual / 50.0) : 0.3;
        var confidence = Math.Clamp(baseConfidence * (1.0 - penalty), 0.03, 0.99);
        return new PositionSolution(solution, confidence, method, residual);
    }

    public static double RssiToDistanceMeters(int rssiDbm)
    {
        // Log-distance path loss, calibrated loosely for the prototype.  This is
        // auxiliary evidence only and is weighted far below FTM ranges.
        const double measuredPowerAtOneMeter = -52.0;
        const double pathLossExponent = 2.3;
        var distance = Math.Pow(10.0, (measuredPowerAtOneMeter - rssiDbm) / (10.0 * pathLossExponent));
        return Math.Clamp(distance, 0.5, 150.0);
    }
}
