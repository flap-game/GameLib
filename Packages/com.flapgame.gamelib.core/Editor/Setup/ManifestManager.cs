using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlapGame.GameLib.Editor
{
    /// <summary>
    /// Reads and edits <c>Packages/manifest.json</c> through Newtonsoft <see cref="JObject"/> (key order
    /// preserved, unknown fields untouched). A manifest that does not parse throws
    /// <see cref="InvalidDataException"/> from the constructor, so nothing is ever written over it.
    /// Output uses Unity's own layout: 2-space indentation, LF line endings.
    /// </summary>
    public sealed class ManifestManager
    {
        public static string ManifestPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/manifest.json"));

        private readonly string _path;
        private readonly JObject _root;
        private readonly bool _trailingNewline;

        public ManifestManager() : this(ManifestPath) { }

        /// <exception cref="InvalidDataException">The file is not a JSON object.</exception>
        public ManifestManager(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            string text = File.ReadAllText(path);
            _trailingNewline = text.EndsWith("\n", StringComparison.Ordinal);
            try
            {
                // DateParseHandling.None: keep every string verbatim (no date reformatting on save).
                using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
                _root = JToken.ReadFrom(reader) as JObject
                        ?? throw new InvalidDataException("root is not a JSON object");
                if (reader.Read() && reader.TokenType != JsonToken.Comment)
                    throw new InvalidDataException("unexpected content after the root object");
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"'{path}' is not valid JSON ({e.Message}). Fix it by hand; nothing was written.", e);
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"'{path}': {e.Message}. Fix it by hand; nothing was written.", e);
            }
        }

        public bool HasDependency(string id) =>
            Deps().ContainsKey(id);

        public string GetVersion(string id) =>
            Deps().TryGetValue(id, out var v) && v.Type == JTokenType.String ? (string)v : null;

        public void SetDependency(string id, string value) =>
            Deps()[id] = value;

        public void RemoveDependency(string id) =>
            Deps().Remove(id);

        /// <summary>True when the entry is a git URL (https/ssh/git+ ... .git or #rev), i.e. something UPM clones.</summary>
        public static bool IsGitSource(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string v = value.Trim();
            return v.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
                   || v.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
                   || v.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
                   || ((v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                       && (v.Contains(".git") || v.Contains("#")));
        }

        // Adds a scoped registry, or merges missing scopes into an existing entry
        // with the same URL (trailing slash insensitive).
        public void EnsureScopedRegistry(string name, string url, string[] scopes)
        {
            var list = Registries();
            foreach (var reg in list.OfType<JObject>())
            {
                if (!UrlEquals((string)reg["url"], url)) continue;

                if (reg["scopes"] is not JArray existing)
                {
                    existing = new JArray();
                    reg["scopes"] = existing;
                }
                foreach (var scope in scopes)
                {
                    if (!IsScopeCovered(scope, existing))
                        existing.Add(scope);
                }
                return;
            }
            list.Add(new JObject
            {
                ["name"]   = name,
                ["url"]    = url,
                ["scopes"] = new JArray(scopes.Cast<object>().ToArray())
            });
        }

        // Removes the given scopes from any scoped registry that matches the URL.
        // If a registry ends up with zero scopes, the registry itself is removed.
        public void RemoveScopes(string url, string[] scopes)
        {
            var list = Registries();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] is JObject reg &&
                    UrlEquals((string)reg["url"], url) &&
                    reg["scopes"] is JArray existing)
                {
                    foreach (var scope in scopes)
                    {
                        foreach (var t in existing.Where(x => x.Type == JTokenType.String && (string)x == scope).ToList())
                            t.Remove();
                    }

                    if (existing.Count == 0)
                        list.RemoveAt(i);
                }
            }
        }

        private static bool UrlEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            return a.TrimEnd('/') == b.TrimEnd('/');
        }

        // Unity's UPM matches scoped registries by prefix on `.` boundaries:
        // a scope "com.google" already covers "com.google.ads.mobile".
        private static bool IsScopeCovered(string scope, JArray existing)
        {
            foreach (var item in existing)
            {
                if (item.Type != JTokenType.String) continue;
                string s = (string)item;
                if (s == scope) return true;
                if (scope.StartsWith(s + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Serialized manifest exactly as <see cref="Save"/> would write it.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb) { NewLine = "\n" })
            using (var writer = new JsonTextWriter(sw) { Formatting = Formatting.Indented, Indentation = 2, IndentChar = ' ' })
            {
                _root.WriteTo(writer);
            }
            if (_trailingNewline) sb.Append('\n');
            return sb.ToString().Replace("\r\n", "\n");
        }

        // Atomic write so a crash mid-write or a concurrent UPM read cannot
        // leave manifest.json truncated.
        public void Save()
        {
            string payload = ToJson();
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, payload, new UTF8Encoding(false));
            if (File.Exists(_path))
                File.Replace(tmp, _path, null);
            else
                File.Move(tmp, _path);
        }

        private JObject Deps()
        {
            if (_root["dependencies"] is not JObject deps)
            {
                deps = new JObject();
                _root["dependencies"] = deps;
            }
            return deps;
        }

        private JArray Registries()
        {
            if (_root["scopedRegistries"] is not JArray list)
            {
                list = new JArray();
                _root["scopedRegistries"] = list;
            }
            return list;
        }
    }
}
