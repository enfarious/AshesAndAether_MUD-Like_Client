namespace AshesAndAether_Client;

public readonly record struct Vector3(double X, double Y, double Z)
{
    public double DistanceTo(Vector3 other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public double HorizontalDistanceTo(Vector3 other)
    {
        var dx = X - other.X;
        var dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public Vector3 DeltaTo(Vector3 other) => new(other.X - X, other.Y - Y, other.Z - Z);
}
