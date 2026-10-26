using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// The JSON parser behind <see cref="IRemoteConfig.GetObject{T}"/> for every provider. Newtonsoft (not JsonUtility)
    /// so a top-level array / <c>List&lt;T&gt;</c>, dictionaries and nullable fields work.
    /// <para>
    /// Rules: property names match case-insensitively; unknown properties are ignored; a property left out keeps the
    /// type's default (use nullable fields to tell "absent" from "zero"); enums accept their name (case-insensitive,
    /// e.g. <c>"Max"</c>) or their number (<c>2</c>). Malformed JSON returns the caller's default with a warning.
    /// Types only built by this parser should carry <c>[UnityEngine.Scripting.Preserve]</c> so IL2CPP stripping keeps
    /// their constructors.
    /// </para>
    /// </summary>
    public static class RemoteConfigJson
    {
        public static readonly JsonSerializerSettings Settings = new()
        {
            Converters            = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling     = NullValueHandling.Ignore,
        };

        /// <summary>Parses <paramref name="json"/>; false (and <paramref name="value"/> = default) when empty or malformed.</summary>
        public static bool TryDeserialize<T>(string json, out T value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                value = JsonConvert.DeserializeObject<T>(json, Settings);
                return value != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteConfig] Could not parse JSON as {typeof(T).Name}: {e.Message}");
                value = default;
                return false;
            }
        }

        /// <summary>Parsed value, or <paramref name="defaultValue"/> when <paramref name="json"/> is empty or malformed.</summary>
        public static T Deserialize<T>(string json, T defaultValue = default) =>
            TryDeserialize(json, out T value) ? value : defaultValue;
    }
}
