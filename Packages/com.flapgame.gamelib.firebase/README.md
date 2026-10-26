# GameLib Firebase

The Firebase base shared by **Analytics - Firebase**, **Crashlytics** and **Remote Config - Firebase**. It has no
module asset: install one of those from **GameLib > Setup** and this package comes with it.

What it does:

- One Firebase start (`CheckAndFixDependenciesAsync` + default app) for every Firebase module. Firebase refuses calls
  while the check runs, so the modules share one task (`FirebaseStartup.EnsureReadyAsync`) instead of racing.
- The configuration checks the Firebase modules show in **GameLib > Setup** and that stop the build.

## Install the SDK

GameLib Setup installs Firebase **14.1.0** from Google's tarball archive: it downloads each `.tgz` into
`Packages/GoogleTgz/` and references it as `file:GoogleTgz/<file>.tgz`. External Dependency Manager comes from OpenUPM.

- **Commit `Packages/GoogleTgz/`** (Git LFS is a good idea: Firebase App alone is about 60 MB). A CI build such as
  Unity Cloud Build gets the SDK from there.
- Every Firebase package must have the same version.

## Config files

Download from the Firebase console (Project settings > Your apps) and put anywhere under `Assets/`:

- Android: `google-services.json`
- iOS: `GoogleService-Info.plist`

Without the file of the platform being built, Firebase cannot start: each module turns itself off for the session with
one log line (a warning in the Editor, where this is expected), and the build validator reports an error.

## How the SDK code is wired

The modules' code compiles without Firebase. The Firebase calls live in separate `*.Sdk` assemblies that compile only
when the matching Firebase package is installed (asmdef `versionDefines`) and plug themselves in before the first scene
loads. Removing Firebase therefore never leaves a missing script on a module asset.
