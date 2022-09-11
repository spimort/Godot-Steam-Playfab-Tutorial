using Godot;

public class MainScene : Node {
    public override void _Ready() {
        if (OS.HasFeature("__clientBuild")) {
            GetTree().ChangeScene("res://Scenes/GameClient.tscn");
        } else {
            GetTree().ChangeScene("res://Scenes/GameServer.tscn");
        }
    }
}
