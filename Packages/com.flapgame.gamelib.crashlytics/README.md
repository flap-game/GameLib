# GameLib Crashlytics

Firebase Crashlytics as `ICrashReporter`. Install from **GameLib > Setup > Crashlytics** (it adds
`com.flapgame.gamelib.firebase`, External Dependency Manager, Firebase App and Firebase Crashlytics), then put the
config files in place - see the [Firebase package README](../com.flapgame.gamelib.firebase/README.md).

Uncaught exceptions and native crashes are reported by Crashlytics itself. For the rest:

```csharp
if (services.TryResolve(out ICrashReporter crashes))
{
    crashes.SetCustomKey("level", "12");
    crashes.Log("opened shop");
    try { LoadSave(); }
    catch (Exception e) { crashes.RecordException(e); }   // non-fatal issue
}
```

- The module is also an analytics sink: the shared user id, user properties as custom keys, and - with
  **Breadcrumbs From Analytics** on (default) - every event, ad revenue and purchase as one log line
  (`level_complete level=12 stars=3`), so a report shows what the player did just before.
- **Report Uncaught Exceptions As Fatal**: off by default (uncaught C# exceptions show as non-fatal, the game usually
  keeps running).
- It installs first among the modules. Calls made before Firebase is up are buffered and sent in order; if Firebase
  cannot start at all, crash reporting is off for the session (one log line); past **Init Timeout Seconds** it only
  warns and keeps buffering.

Test it on a device build: throw from a button, restart the app, and the crash shows in the Firebase console within
minutes. Symbol upload for native (IL2CPP) stacks: see Firebase's Crashlytics Unity docs.
