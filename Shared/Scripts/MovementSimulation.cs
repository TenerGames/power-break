using Godot;
using Godot.Collections;
using System;

public class MovementSimulation
{
    public static InputState GetMovementInput(ulong timeStamp, int processTick)
    {
        Vector2 inputDirectionFloat = Vector2.Zero;
        bool jumped = false;

        if (Input.IsActionPressed("move_forward"))
            inputDirectionFloat.Y += 1;
        if (Input.IsActionPressed("move_backward"))
            inputDirectionFloat.Y -= 1;
        if (Input.IsActionPressed("move_left"))
            inputDirectionFloat.X += 1;
        if (Input.IsActionPressed("move_right"))
            inputDirectionFloat.X -= 1;
        if (Input.IsActionPressed("jump"))
            jumped = true;

        inputDirectionFloat = inputDirectionFloat.Normalized();

        Array<Vector2I> vectorsDeterministiced = MathUtils.GetDirectionVectorsDeterministiced(ref inputDirectionFloat);
        return new InputState(vectorsDeterministiced[0], vectorsDeterministiced[1], jumped, new TickState(processTick, timeStamp));
    }
    
    public static void SimulateInputMovement(float delta, ref InputState input, ref CharacterBody3D characterBody3D, ref CharacterAttributesState characterAttributesState)
    {
        Vector2I inputDirection = input.inputDirection;
        Vector2I inputDirectionFraction = input.inputDirectionFraction;

        Vector2 moveDirection = MathUtils.VectorDeterministicedToFloatVector(ref inputDirection, ref inputDirectionFraction) * characterAttributesState.moveSpeed;
        Vector3 currentVelocity = characterBody3D.Velocity;

        bool onFloor = characterBody3D.IsOnFloor();

        float x = onFloor ? moveDirection.X : currentVelocity.X > 0 ? Math.Max(currentVelocity.X - (delta * 15), 0.0F) : Math.Min(currentVelocity.X + (delta * 15), 0.0F);
        float y = input.jumped && onFloor ? currentVelocity.Y + characterAttributesState.jumpPower : currentVelocity.Y;
        float z = onFloor ? moveDirection.Y : currentVelocity.Z > 0 ? Math.Max(currentVelocity.Z - (delta * 15), 0.0F) : Math.Min(currentVelocity.Z + (delta * 15), 0.0F);

        characterBody3D.Velocity = new Vector3(x, y, z);
    }

    public static void SimulateGravity(ref CharacterBody3D characterBody3D, float gravity, float delta)
    {
        if (characterBody3D.IsOnFloor()) return;

        characterBody3D.Velocity -= new Vector3(0, gravity * delta, 0);
    }

    public static CharacterAttributesState GetCharacterAttributes(Character character)
    {
        return new CharacterAttributesState(character.moveSpeed, character.jumpPower, character.GetCharacterGravity());
    }

    public static TransformState GenerateTransformState(ref CharacterBody3D characterBody3, int processTick, ulong timeStamp, CharacterAttributesState characterAttributesState)
    {
        return new TransformState(characterBody3.Velocity, characterBody3.Position, new TickState(processTick, timeStamp), characterAttributesState);
    }

    public static void SimulateCharacterFrame(Character character, ref InputState input, int processTick, float delta, ref CharacterAttributesState characterAttributesState)
    {
        CharacterBody3D characterBody3D = character;

        SimulateInputMovement(delta, ref input, ref characterBody3D, ref characterAttributesState);
        SimulateGravity(ref characterBody3D, character.GetCharacterGravity(), delta);
        character.MoveAndSlide();
    }
}
