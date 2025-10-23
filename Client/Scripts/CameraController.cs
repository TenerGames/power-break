using Godot;
using System;

public partial class CameraController : Camera3D
{
    private Node3D targetNode = null;
    [Export] public float DistanceMultiplier = 1f; 
    [Export] public float HeightMultiplier = 0.37f; 
    [Export] public float Smoothness = 35.0f;
    [Export] public Vector3 OffsetRotation = new Vector3(-3f, 0, 0); 
    [Export] public Vector3 CameraOffset = new Vector3(-0.15f, 0.0f, 0.0f); 

    private Vector3 _smoothedPosition;
    private float _size;
    private bool moveCamera = false;

    public Node3D TargetNode
    {
        get { return targetNode; }
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (!moveCamera) return;

        MeshInstance3D MeshInstance3D = targetNode.GetChildOrNull<MeshInstance3D>(0);

        if (MeshInstance3D == null) return;

        Aabb bounds = MeshInstance3D.GetAabb();

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

    public void ClearTargetNode()
    {
        targetNode.TreeExited -= ClearTargetNode;
        targetNode = null;
        moveCamera = false;
    }

    public void SetTargetNode(Node3D newTargetnode)
    {
        if (newTargetnode == null) return;

        if (targetNode != null)
        {
            targetNode.TreeExited -= ClearTargetNode;
        }

        targetNode = newTargetnode;
        ProcessPriority = targetNode.ProcessPriority + 1;
        ProcessPhysicsPriority = targetNode.ProcessPhysicsPriority + 1;

        targetNode.TreeExited += ClearTargetNode;

        moveCamera = true;
    }
}
