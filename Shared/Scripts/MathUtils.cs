using Godot;
using System;

public class MathUtils
{
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
