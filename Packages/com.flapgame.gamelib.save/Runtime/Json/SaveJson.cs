using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlapGame.GameLib.Save.Json
{
    /// <summary>Json.NET settings of the save. Add converters for the game's own types before the save is opened.</summary>
    public static class SaveJson
    {
        /// <summary>
        /// Unity structs round-trip (Vector*, Quaternion, Color, Rect); reference loops are skipped instead of throwing;
        /// no type names are read or written (a save file never chooses which type gets created). Collections set in a
        /// field initialiser are REPLACED on load - Json.NET's default appends, so <c>List&lt;int&gt; Ids = new() {1}</c>
        /// would grow by one entry per load.
        /// </summary>
        public static JsonSerializerSettings DefaultSettings { get; } = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            TypeNameHandling = TypeNameHandling.None,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.None,
            Converters = new List<JsonConverter>(UnityJsonConverters.Create()),
        };

        /// <summary>
        /// Parses a save document without turning date-like strings into dates (Json.NET's default would make a stored
        /// string "2026-10-27T10:00:00" come back reformatted).
        /// </summary>
        public static JToken Parse(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            JToken token = JToken.ReadFrom(reader);
            if (reader.Read() && reader.TokenType != JsonToken.Comment)
                throw new JsonReaderException("Additional text after the save document.");
            return token;
        }
    }
}
