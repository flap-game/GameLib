using UnityEditor;
using UnityEngine;

namespace FlapGame.GameLib.AppIcon.Editor
{
    /// <summary>Name field plus an image picker that stores the image's GUID.</summary>
    [CustomPropertyDrawer(typeof(AppIconEntry))]
    public sealed class AppIconEntryDrawer : PropertyDrawer
    {
        private const float Preview = 48f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => Preview + 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty name = property.FindPropertyRelative(nameof(AppIconEntry.Name));
            SerializedProperty guid = property.FindPropertyRelative(nameof(AppIconEntry.ImageGuid));
            EditorGUI.BeginProperty(position, label, property);

            var line = new Rect(position.x, position.y + 2f, position.width - Preview - 6f, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(line, name);

            string path = string.IsNullOrEmpty(guid.stringValue) ? null : AssetDatabase.GUIDToAssetPath(guid.stringValue);
            var current = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            line.y += EditorGUIUtility.singleLineHeight + 4f;
            EditorGUI.BeginChangeCheck();
            var picked = (Texture2D)EditorGUI.ObjectField(line, "Image", current, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
                guid.stringValue = picked != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picked)) : string.Empty;

            var preview = new Rect(position.xMax - Preview, position.y + 2f, Preview, Preview);
            if (picked != null) EditorGUI.DrawPreviewTexture(preview, picked, null, ScaleMode.ScaleToFit);
            else EditorGUI.HelpBox(preview, string.Empty, MessageType.None);

            EditorGUI.EndProperty();
        }
    }
}
