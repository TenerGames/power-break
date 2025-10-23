using Godot;
using System;

public partial class CameraController : Camera3D
{
    Node3D targetNode;
    [Export] public float DistanceMultiplier = 1f; 
    [Export] public float HeightMultiplier = 0.37f; 
    [Export] public float Smoothness = 10.0f;
    [Export] public Vector3 OffsetRotation = new Vector3(-3f, 0, 0); 
    [Export] public Vector3 CameraOffset = new Vector3(-0.15f, 0.0f, 0.0f); 

    private Vector3 _smoothedPosition;
    private float _size;

    public Node3D TargetNode
    {
        get { return targetNode; }
        set { targetNode = value; }
    }

    public override void _Ready()
    {
        base._Ready();

        if (targetNode != null)
        {
            ProcessPriority = targetNode.ProcessPriority + 1;
            ProcessPhysicsPriority = targetNode.ProcessPhysicsPriority + 1;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (targetNode == null) return;

        Aabb bounds = targetNode.GetChild<MeshInstance3D>(0).GetAabb();
        _size = bounds.Size.Length();

        Vector3 baseTargetPos = targetNode.GlobalPosition + Vector3.Up * (_size * HeightMultiplier);

        Basis basis = targetNode.GlobalTransform.Basis;
        Vector3 backDir = -basis.Z.Normalized();
        Vector3 rightDir = basis.X.Normalized();
        Vector3 upDir = basis.Y.Normalized();

        Vector3 desiredPos =
            baseTargetPos +
            backDir * (_size * DistanceMultiplier + CameraOffset.Z) +
            rightDir * (_size * CameraOffset.X) +
            upDir * (_size * CameraOffset.Y);


        _smoothedPosition = _smoothedPosition.Lerp(desiredPos, (float)(Smoothness * delta));
        GlobalPosition = _smoothedPosition;

        LookAt(baseTargetPos, Vector3.Up);
        RotateObjectLocal(Vector3.Right, Mathf.DegToRad(OffsetRotation.X));
    }
}
