using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(NPCController))]
public class NPCControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        NPCController npcController = (NPCController)target;

        if (GUILayout.Button("Set Target To Player1"))
        {
            npcController.StartFollowingPlayer(PlayerType.Player1);
            EditorUtility.SetDirty(npcController);
        }
        
        if (GUILayout.Button("Set Target To Player2"))
        {
            npcController.StartFollowingPlayer(PlayerType.Player2);
            EditorUtility.SetDirty(npcController);
        }

        if (GUILayout.Button("Toggle Crouch"))
        {
            npcController.ToggleCrouch();
            EditorUtility.SetDirty(npcController);
        }
        
        if (GUILayout.Button("Dead By Explosion"))
        {
            npcController.DieByExplosion();
            EditorUtility.SetDirty(npcController);
        }
        
        if (GUILayout.Button("Dead By Smoke"))
        {
            npcController.DieBySmoke();
            EditorUtility.SetDirty(npcController);
        }
    }
}
