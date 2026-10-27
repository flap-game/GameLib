# GameLib Save

Save game data on Android and iOS: one file, written so a crash or a kill never loses or truncates it, with versioned
sections that migrate old saves, autosave, and an optional encryption.

Install from **GameLib > Setup > Save**, or add to `Packages/manifest.json` (together with core, same tag):

```
https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.save#v0.8.9
```

## Use

```csharp
var save = (await GameLibBootstrap.WhenReadyAsync()).Resolve<ISaveService>();

// One section per game system: a plain class, changed in place.
ISaveSection<PlayerData> player = save.Section<PlayerData>("player");
player.Data.Coins += 50;
player.MarkDirty();                    // written ~2 s later in the background, and at once on pause

save.SetProgress(player.Data.Level);  // kept in the metadata; a cloud conflict keeps the copy with more

// Small values by key, stored as a copy.
ISaveService tutorial = save.ForScope("tutorial");
tutorial.Save("step", 3);
int step = tutorial.Load("step", 0);

save.Flush();                          // a checkpoint you never want to lose (after a purchase, a level win)
```

```csharp
[Serializable]
public sealed class PlayerData
{
    public int Level = 1;
    public int Coins;
    public List<string> Skins = new() { "default" };   // replaced on load, never appended to
}
```

- Read `section.Data` each time instead of keeping it: an import, `Reset()` or `DeleteAll()` swaps it (`Replaced` fires).
- Fields with defaults are the defaults of an old save that lacks them: adding a field needs no migration.
- Main thread only. Keys and scopes must not contain `.`.

## Changing a section's shape

Raise its version and add a step per version. Steps run on the stored JSON, in order, from the stored version:

```csharp
var migrations = new SaveMigrations()
    .Step(2, (JObject o) => { o["Coins"] = o["gold"]; o.Remove("gold"); })          // renamed
    .Step(3, (JObject o) => { o["Level"] = Math.Max(1, o.Value<int>("Level")); });   // new rule
ISaveSection<PlayerData> player = save.Section<PlayerData>("player", version: 3, migrations);
```

A section that cannot be read (a bug in a step, a type changed without a step) starts from `new T()`: the other sections
are unaffected, `section.LoadError` says why, and the stored data is kept under `quarantine` in the file.

## What happens on disk

| Situation | Result (`save.LoadStatus`) |
|---|---|
| First launch | `New` |
| Normal | `Loaded` |
| Killed after the new copy was on disk but before the rename | `Loaded`: the newer `.tmp` (it has the higher save count) |
| App killed mid-write, file truncated or damaged | `RecoveredFromBackup`: the last good copy (`.tmp` or `.bak`); the damaged file is moved to `.corrupt-<utc>` (3 kept) |
| Every copy damaged | `Corrupted`: fresh start, the damaged file is kept |
| File exists but cannot be read (I/O error), or written by a newer GameLib | `ReadFailed`: the game runs, **nothing is written** this session, so the save is never overwritten |
| File edited by hand | Builds: treated as damaged (checksum). Editor: read anyway (`Accept Edited Save In Editor`) |

Anything but `New` / `Loaded` goes to Crashlytics as a non-fatal and to analytics as `save_load_issue` (`status`).

Writes: the document is serialized on the main thread, then written in the background as `.tmp` (fsynced), the old file
becomes `.bak`, `.tmp` becomes the file. On pause / focus loss / quit the write runs on the main thread, because mobile OSes
kill backgrounded apps without another callback. Writes are numbered: a late background write never replaces a newer one.

## Settings (SaveModule asset)

| Setting | Default | |
|---|---|---|
| File Name | `gamelib.save` | Inside `Application.persistentDataPath` |
| Encrypt | off | AES-256. Either mode reads the other, so it can be turned on later |
| Secret | bundle id | Key of the checksum and the encryption. **Never change it after release** |
| Auto Save Delay Seconds | 2 | Changes within that time make one write. 0 = only pause / quit / `Flush` |
| Flush On Pause | on | Leave on |
| Pretty Print In Editor | on | Indented JSON in the Editor |
| Accept Edited Save In Editor | on | |

The secret ships in the build: it stops casual editing and detects damage, it does not stop a determined cheater.
Anything worth money (purchases, currency bought with real money) must be checked by a server or the store, not the save.

## Editor

**GameLib > Save**: *Open Save Folder*, *Show Save As JSON* (decrypted, indented, in `Temp/`), *Delete Save*.

## Backups and reinstalls

- **iOS**: `persistentDataPath` is the app's Documents folder, included in the iCloud / iTunes device backup.
- **Android**: the file is under `/data/data/<package>/files`, restored on reinstall by Android Auto Backup when the app
  allows backup (Unity's default) and the player's Google backup is on.
- A cross-device cloud save is not included yet; see below.

## Cloud (for a later backend package)

`ExportSnapshot()` gives the whole save as JSON with its `SaveMetadata` (progress, playtime, save count, times);
`ImportSnapshot()` replaces the save with one and writes it. A backend (Google Play Games Saved Games, iCloud, UGS Cloud
Save, a game server) implements `ICloudSaveBackend`, and `SaveSync.SyncAsync(save, backend)` downloads, keeps the copy with
more progress, then playtime, then the later save (`SaveSync.PreferMoreProgress`, replaceable), and moves it across.

## IL2CPP stripping

The save reads its classes through reflection. With Managed Stripping Level **High**, keep them: `[Preserve]` on each
class, or a `link.xml` with the game's data assembly. Minimal stripping keeps them.
