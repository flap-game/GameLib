using System;
using UnityEngine;

namespace FlapGame.GameLib.Save
{
    /// <summary>Settings of <see cref="SaveModule"/>.</summary>
    [Serializable]
    public sealed class SaveSettings
    {
        [Tooltip("Save file inside Application.persistentDataPath. .tmp, .bak and .corrupt-* copies sit next to it.")]
        public string FileName = "gamelib.save";

        [Tooltip("Write the payload AES-encrypted. Off = plain JSON (readable while debugging). Both are always readable, " +
                 "so this can be switched on later without losing saves. The checksum is always there.")]
        public bool Encrypt;

        [Tooltip("Key of the checksum and the encryption. Empty = the bundle id. NEVER change it after release: saves " +
                 "written with the old key fail their checksum. It ships in the build - this stops casual edits, not attackers.")]
        public string Secret = string.Empty;

        [Tooltip("Seconds between a change and its background write. Several changes in that time make one write. 0 = only " +
                 "on pause / quit / an explicit Flush.")]
        [Min(0f)] public float AutoSaveDelaySeconds = 2f;

        [Tooltip("Write when the app goes to the background or loses focus. Leave on: mobile OSes kill backgrounded apps " +
                 "without another callback.")]
        public bool FlushOnPause = true;

        [Tooltip("Indent the JSON in the Editor (plain files only), for reading it by hand.")]
        public bool PrettyPrintInEditor = true;

        [Tooltip("In the Editor, read a plain save whose checksum does not match (you edited it by hand) instead of " +
                 "treating it as damaged. Builds always treat it as damaged.")]
        public bool AcceptEditedSaveInEditor = true;

        /// <summary><see cref="Secret"/>, or the bundle id when it is empty.</summary>
        public string ResolveSecret() => string.IsNullOrEmpty(Secret) ? Application.identifier : Secret;

        public SaveSettings Clone() => (SaveSettings)MemberwiseClone();
    }
}
