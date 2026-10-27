using System;
using UnityEngine.Scripting;

namespace FlapGame.GameLib.Ads
{
    /// <summary>One SDK extra parameter (AppLovin <c>SetExtraParameter</c> / <c>Set*ExtraParameter</c>).</summary>
    [Serializable, Preserve]
    public sealed class AdKeyValue
    {
        public string Key;
        public string Value;

        [Preserve]
        public AdKeyValue() { }

        public AdKeyValue(string key, string value)
        {
            Key   = key;
            Value = value;
        }
    }
}
