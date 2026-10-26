using UnityEngine.Scripting;

// Nothing references this assembly: it plugs into FirebaseStartup through RuntimeInitializeOnLoadMethod. Without this
// attribute IL2CPP managed stripping drops it, and every Firebase module reports "not installed" on device.
[assembly: AlwaysLinkAssembly]
