using Godot;
using Godot.Collections;
using System;

public struct CharacterAttributesState(float moveSpeed, float jumpPower, float gravity)
{
    public float moveSpeed = moveSpeed;
    public float jumpPower = jumpPower;
    public float gravity = gravity;

    public readonly Dictionary ToDictionary()
    {
        return new Dictionary
        {
            { "moveSpeed", moveSpeed },
            { "jumpPower", jumpPower },
            { "gravity", gravity }
        };
    }

    public static CharacterAttributesState FromDictionary(Dictionary dict)
    {
        float moveSpeed = (float)dict["moveSpeed"];
        float jumpPower = (float)dict["jumpPower"];
        float gravity = (float)dict["gravity"];
        
        return new CharacterAttributesState(moveSpeed, jumpPower, gravity);
    }
}
