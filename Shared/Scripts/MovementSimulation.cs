using Godot;
using Godot.Collections;
using System;

public class MovementSimulation
{
    public static InputState GetMovementInput(ulong timeStamp, int processTick, float speed)
    {
        Vector2 inputDirectionFloat = Vector2.Zero;
        bool jumped = false;

        if (Input.IsActionPressed("move_forward"))
            inputDirectionFloat.Y -= 1;
        if (Input.IsActionPressed("move_backward"))
            inputDirectionFloat.Y += 1;
        if (Input.IsActionPressed("move_left"))
            inputDirectionFloat.X -= 1;
        if (Input.IsActionPressed("move_right"))
            inputDirectionFloat.X += 1;
        if (Input.IsActionPressed("jump"))
            jumped = true;

        inputDirectionFloat = inputDirectionFloat.Normalized() * speed;

        Array<Vector2I> vectorsDeterministiced = MathUtils.GetDirectionVectorsDeterministiced(ref inputDirectionFloat);
        return new InputState(vectorsDeterministiced[0], vectorsDeterministiced[1], jumped, new TickState(processTick, timeStamp));
    }
    
    public static void SimulateInputMovement(ref InputState input, ref CharacterBody3D characterBody3D, float jumpPower)
    {
        Vector2I inputDirection = input.inputDirection;
        Vector2I inputDirectionFraction = input.inputDirectionFraction;

        Vector2 moveDirection = MathUtils.VectorDeterministicedToFloatVector(ref inputDirection, ref inputDirectionFraction);
        Vector3 currentVelocity = characterBody3D.Velocity;

        float y = input.jumped && characterBody3D.IsOnFloor() ? currentVelocity.Y + jumpPower : currentVelocity.Y;

        characterBody3D.Velocity = new Vector3(moveDirection.X, y, moveDirection.Y);
    }

    public static void SimulateGravity(ref CharacterBody3D characterBody3D, float gravity, float delta)
    {
        if (characterBody3D.IsOnFloor()) return;

        characterBody3D.Velocity -= new Vector3(0, gravity * delta, 0);
    }

    public static TransformState GenerateTransformState(ref CharacterBody3D characterBody3, int processTick, ulong timeStamp)
    {
        return new TransformState(characterBody3.Velocity, characterBody3.Position, new TickState(processTick, timeStamp));
    }

    public static void SimulateCharacterFrame(Character character, ref InputState input, int processTick, float delta)
    {
        CharacterBody3D characterBody3D = character;

        SimulateInputMovement(ref input, ref characterBody3D, character.jumpPower);
        SimulateGravity(ref characterBody3D, character.gravity, delta);
        character.MoveAndSlide();
    }
}
