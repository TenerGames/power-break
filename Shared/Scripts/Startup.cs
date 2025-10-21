using Godot;
using Godot.Collections ;
using System;
using System.Linq;

public partial class Startup : Node3D
{
    [Export] public Dictionary<long, CombatPlayer> combatPlayers;
    public const int CharacterStartDataChannel = 1;
    public const int CharactersInputsChannel = 2;
    public const int CharactersReconciliationChannel = 3;
    
    public override void _Ready()
    {

        combatPlayers = [];
        
        string[] args = OS.GetCmdlineArgs();

        if (args.Contains("--role"))
        {
            int index = System.Array.IndexOf(args, "--role");

            if (index + 1 < args.Length)
            {
                string role = args[index + 1];

                PackedScene scene = (PackedScene)GD.Load("res://"+role+"/Scenes/Combat_Test.tscn");
                var sceneInstance = scene.Instantiate();
                AddChild(sceneInstance);
            }
        }
        else
        {
            GD.Print("Nenhum argumento --role foi passado.");
        }
    }
}
