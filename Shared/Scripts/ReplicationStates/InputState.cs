using Godot;
using Godot.Collections;
using System;

public struct InputState(Vector2I inputDirection, Vector2I inputDirectionFraction, bool jumped, TickState tickState)
{
    public Vector2I inputDirection = inputDirection;
    public Vector2I inputDirectionFraction = inputDirectionFraction;
    public TickState tickState = tickState;
    public bool jumped = jumped;

    public readonly Dictionary ToDictionary()
    {
        return new Dictionary
        {
            { "inputDirection", inputDirection },
            { "inputDirectionFraction", inputDirectionFraction },
            { "tickState", tickState.ToDictionary() },
            { "jumped", jumped }
        };
    }

    public static InputState FromDictionary(Dictionary dict)
    {
        Vector2I inputDirection = (Vector2I)dict["inputDirection"];
        Vector2I inputDirectionFraction = (Vector2I)dict["inputDirectionFraction"];
        TickState tickState = TickState.FromDictionary((Dictionary)dict["tickState"]);
        bool jumped = (bool)dict["jumped"];

        return new InputState(inputDirection, inputDirectionFraction, jumped, tickState);
    }

    public bool Equals(InputState other)
    {
        return inputDirection == other.inputDirection &&
               inputDirectionFraction == other.inputDirectionFraction &&
               jumped == other.jumped;
    }

    public override bool Equals(object obj)
    {
        return obj is InputState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(inputDirection, inputDirectionFraction, jumped, tickState);
    }

    public static bool operator ==(InputState left, InputState right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(InputState left, InputState right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"Direciton: {inputDirection}, Direction Fraction: {inputDirectionFraction}, Jumped: {jumped}";
    }
}
