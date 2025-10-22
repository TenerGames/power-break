using Godot;
using Godot.Collections;
using System;

public struct CharacterAttributesState(float moveSpeed, float jumpPower)
{
    public float moveSpeed = moveSpeed;
    public float jumpPower = jumpPower;

    public readonly Dictionary ToDictionary()
    {
        return new Dictionary
        {
            { "moveSpeed", moveSpeed },
            { "jumpPower", jumpPower }
        };
    }

    public static CharacterAttributesState FromDictionary(Dictionary dict)
    {
        float moveSpeed = (float)dict["moveSpeed"];
        float jumpPower = (float)dict["jumpPower"];
        
        return new CharacterAttributesState(moveSpeed, jumpPower);
    }
}
