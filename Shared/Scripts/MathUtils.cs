using Godot;
using Godot.Collections;
using System;

public class MathUtils
{
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static Array<Vector2I> GetDirectionVectorsDeterministiced(ref Vector2 vector)
    {
        ulong integerX = (ulong)Math.Floor(vector.X);
        ulong integerY = (ulong)Math.Floor(vector.Y);

        ulong decimalX = (ulong)Math.Round((vector.X - integerX) * 1000);
        ulong decimalY = (ulong)Math.Round((vector.Y - integerY) * 1000);

        Vector2I integerVector = new((int)integerX, (int)integerY);
        Vector2I decimalVector = new((int)decimalX, (int)decimalY);

        return [integerVector, decimalVector];
    }

    public static Vector2 VectorDeterministicedToFloatVector(ref Vector2I intVector, ref Vector2I decimalVector)
    {
        float x = (float)(intVector.X + (decimalVector.X / Math.Pow(10, 3)));
        float y = (float)(intVector.Y + (decimalVector.Y / Math.Pow(10, 3)));

        return new(x, y);
    }
}
