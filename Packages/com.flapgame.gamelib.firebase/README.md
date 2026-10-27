# GameLib Firebase

The Firebase base shared by **Analytics - Firebase**, **Crashlytics** and **Remote Config - Firebase**. It has no
module asset: install one of those from **GameLib > Setup** and this package comes with it.

What it does:

- One Firebase start (`CheckAndFixDependenciesAsync` + default app) for every Firebase module. Firebase refuses calls
  while the check runs, so the modules share one task (`FirebaseStartup.EnsureReadyAsync`) instead of racing.
- The configuration checks the Firebase modules show in **GameLib > Setup** and that stop the build.

## Install the SDK

GameLib Setup installs Firebase **13.16.0** from the GameWorkstore git mirror of Google's packages, the source CoreGame
games use: `https://github.com/GameWorkstore/com.google.firebase.<app|analytics|crashlytics|remote-config>.git#13.16.0`.
Nothing to commit, and a CI build such as Unity Cloud Build fetches it like any git package. External Dependency
Manager comes from OpenUPM.

- **Supported: Firebase 13.x and 14.x.** The GameLib Firebase code compiles against 13.16.0 and 14.1.0. Doctor and
  Setup warn on another major version.
- **Every Firebase package must have the same version** - Doctor reports an error otherwise.
- **Firebase 14.x:** the mirror stops at 13.16.0 (its newest tag). For 14.x download the `.tgz` files from
  [Google's archive](https://developers.google.com/unity/archive), put them in `Packages/GoogleTgz/`, reference them as
  `file:GoogleTgz/<file>.tgz` and commit that folder (Git LFS is a good idea: Firebase App alone is about 60 MB).
  Setup leaves Firebase entries already in the manifest alone, so a project on tarballs stays on them.
- The mirror is a third-party copy of Google's packages: pin a tag (never a branch) as Setup does.

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
