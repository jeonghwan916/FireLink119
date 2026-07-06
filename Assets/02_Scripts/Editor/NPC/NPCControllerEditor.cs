using UnityEditor;
using UnityEngine;

namespace FireLink119.NPC
{
    [CustomEditor(typeof(NPCController))]
    public class NPCControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            NPCController npcController = (NPCController)target;
            bool canEditRuntimeState = EditorApplication.isPlaying && npcController.HasStateAuthority;

            EditorGUILayout.Space();
            EditorGUI.BeginDisabledGroup(!canEditRuntimeState);

            if (GUILayout.Button("Toggle Crouch"))
            {
                npcController.ToggleCrouch();
            }
        
            if (GUILayout.Button("Dead By Explosion"))
            {
                npcController.DieByExplosion();
            }
        
            if (GUILayout.Button("Dead By Smoke"))
            {
                npcController.DieBySmoke();
            }

            EditorGUI.EndDisabledGroup();
        }
    }
}
