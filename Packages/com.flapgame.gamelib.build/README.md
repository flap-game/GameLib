# GameLib Build

Unity Cloud Build hooks for a GameLib game:

- **Unity splash screen off** (`HideUnitySplashScreen`, on by default).
- **App Store upload.** A post-build script that uploads the IPA of an iOS build to App Store Connect with `altool`.

Editor only: nothing of this package goes into the player. The version name and build numbers are not touched here:
the PO sets them on GameHub.

Install:

```
https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.build#v0.8.12
```

## Project setup

1. Optional: **GameLib > Cloud Build > Settings** creates `Assets/GameLib/Editor/CloudBuildSettings.asset`. Without
   it the defaults apply (`HideUnitySplashScreen` on).
2. **GameLib > Cloud Build > Install App Store Upload Script** writes `BuildScripts/post-build-upload-appstore.bash`.
   It also adds `BuildScripts/*.bash text eol=lf` to `.gitattributes`: a script checked out with CRLF fails on the Mac
   with `$'\r': command not found`.
3. Commit the asset, the script and `.gitattributes`.

**GameLib > Cloud Build > Run Pre-Export Locally** applies the same pre-export to the open project, so you can check
the result.

## Unity Cloud Build dashboard

Under each build target's **Configure > Advanced settings**:

| Setting | Android | iOS |
|---|---|---|
| Pre-Export Method Name | `GameLibCloudBuild.PreExport` | `GameLibCloudBuild.PreExport` |
| Post-build script | — | `BuildScripts/post-build-upload-appstore.bash` |
| Post-build script: mark build as failed if the script fails | — | on |
| Build App Bundle (Android App Bundle) | on (upload the AAB to Google Play by hand) | — |

Environment variables of the iOS target:

| Variable | Value |
|---|---|
| `ITUNES_USERNAME` | An Apple ID with upload rights on the app (App Manager or Developer role) |
| `ITUNES_PASSWORD` | An **app-specific password** of that Apple ID, made at appleid.apple.com > Sign-In and Security. The account password does not work with two-factor sign-in. |

After the upload the build shows in App Store Connect / TestFlight once Apple has processed it, usually 10 to 30
minutes later.

## Notes

- Core's build validator (`GameLibBuildValidator`) also runs in Cloud Build. A GameLib configuration error, such as a
  missing SDK key, fails the build there, the same as locally.
- `PreExport` takes no parameter: Cloud Build's `BuildManifestObject` type lives in the project, where this package
  cannot see it.
