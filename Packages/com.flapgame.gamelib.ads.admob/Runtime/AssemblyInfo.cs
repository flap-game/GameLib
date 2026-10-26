using UnityEngine.Scripting;

// Nothing references a provider assembly: its factory registers itself through RuntimeInitializeOnLoadMethod. Without
// this attribute IL2CPP managed stripping drops the whole assembly, so a player build has no provider and falls back to
// the Null adapter, while the Editor (no stripping) works.
[assembly: AlwaysLinkAssembly]
