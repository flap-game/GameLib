#!/usr/bin/env python3
"""AdMob + MAX mediation version set: check it against the registries, or write a test manifest.

The set lives in Packages/com.flapgame.gamelib.ads/Documentation~/mediation-versions.json.

  python Tools/mediation_versions.py check            # re-read every pinned package's native pins, flag conflicts,
                                                      # list newer versions that would still agree
  python Tools/mediation_versions.py manifest <dir> [--gamehub <release-config.json>] [--platform android|ios]
      # Packages/manifest.json of a test project (GameLib packages as file: references): every adapter of both
      # stacks, or only the networks a GameHub release config enables (GET /api/public/release-config).

Why the pins matter: on iOS both Google's and AppLovin's adapter pods pin the network SDK exactly ('= x.y.z'), so two
adapters for one network that want different SDK versions make `pod install` fail. On Android Gradle takes the highest
version, which builds but runs one adapter against an SDK it was not built for. "check" demands equal iOS pins and
equal Android pins (a patch-level Android difference is reported as a warning).
"""
import hashlib, io, json, os, re, sys, tarfile, urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MATRIX = os.path.join(ROOT, "Packages", "com.flapgame.gamelib.ads", "Documentation~", "mediation-versions.json")
OPENUPM = "https://package.openupm.com"
APPLOVIN = "https://unity.packages.applovin.com"
MAVENS = ["https://dl.google.com/dl/android/maven2", "https://repo1.maven.org/maven2"]
SKIP = re.compile(r"org\.jetbrains\.kotlin|androidx|play-services|mediation:common|glide|okhttp|cronet|coherence|"
                  r"picasso|android\.support|protobuf|jacoco|applovin-sdk")
GMA_PREFIX = "com.google.ads.mobile.mediation."
MAX_PREFIX = "com.applovin.mediation.adapters."


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": "gamelib-mediation-versions"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


_meta = {}


def meta(registry, name):
    if name not in _meta:
        _meta[name] = json.loads(get(f"{registry}/{name}"))
    return _meta[name]


def upm_native(registry, name, version):
    """Android specs and iOS pods from the package's Dependencies.xml."""
    v = meta(registry, name)["versions"][version]
    android, ios = [], []
    with tarfile.open(fileobj=io.BytesIO(get(v["dist"]["tarball"]))) as t:
        for m in t.getmembers():
            if m.name.endswith("Dependencies.xml"):
                x = t.extractfile(m).read().decode("utf-8", "replace")
                android += re.findall(r'<androidPackage\s+spec="([^"]+)"', x)
                ios += re.findall(r'<iosPod\s+name="([^"]+)"\s+version="([^"]+)"', x)
    return android, ios, v.get("dependencies", {})


def pom_deps(spec):
    g, a, v = spec.split(":")[:3]
    v = v.strip("[]")
    path = f"{g.replace('.', '/')}/{a}/{v}/{a}-{v}.pom"
    for repo in MAVENS:
        try:
            x = get(f"{repo}/{path}").decode("utf-8", "replace")
        except Exception:
            continue
        out = {}
        for d in re.findall(r"<dependency>(.*?)</dependency>", x, re.S):
            f = {k: (re.search(f"<{k}>(.*?)</{k}>", d) or [None, ""])[1] for k in ("groupId", "artifactId", "version", "scope")}
            if f["scope"] != "test":
                out[f"{f['groupId']}:{f['artifactId']}"] = f["version"]
        return out
    return None


def podspec_deps(name, version):
    h = hashlib.md5(name.encode()).hexdigest()
    try:
        j = json.loads(get(f"https://cdn.cocoapods.org/Specs/{h[0]}/{h[1]}/{h[2]}/{name}/{version}/{name}.podspec.json"))
    except Exception:
        return None, None
    deps = dict(j.get("dependencies") or {})
    for ss in j.get("subspecs") or []:
        deps.update(ss.get("dependencies") or {})
    flat = {}
    for k, val in deps.items():
        val = val[0] if isinstance(val, list) and val else (val if isinstance(val, str) else "")
        flat[k.split("/")[0]] = val.replace("=", "").strip()
    return flat, (j.get("platforms") or {}).get("ios")


def network_pins(registry, name, version):
    """{'android': {artifact: ver}, 'ios': {pod: ver}, 'errors': [...]} for the network SDKs only."""
    out = {"android": {}, "ios": {}, "errors": [], "ios_min": None, "upm": {}}
    try:
        android, ios, upm = upm_native(registry, name, version)
    except Exception as e:
        out["errors"].append(f"{name}@{version}: {e}")
        return out
    out["upm"] = upm
    for spec in android:
        if SKIP.search(spec):
            continue
        deps = pom_deps(spec)
        if deps is None:
            g, a, v = spec.split(":")[:3]
            out["android"][f"{g}:{a}"] = v.strip("[]")  # a network SDK listed directly (Unity Ads)
            continue
        for k, v in deps.items():
            if not SKIP.search(k):
                out["android"][k] = v
    for pod, ver in ios:
        deps, ios_min = podspec_deps(pod, ver.replace("~>", "").strip())
        if deps is None:
            out["errors"].append(f"{name}@{version}: iOS pod {pod} {ver} not on CocoaPods trunk (pod install fails)")
            continue
        out["ios_min"] = ios_min
        for k, v in deps.items():
            # MAX adapters ask AppLovinSDK '>= 13.0.0' (any MAX core); Google's AppLovin adapter pins it exactly.
            if k == "AppLovinSDK" and not pod.startswith("GoogleMobileAds"):
                continue
            out["ios"][k] = v
    return out


def newer(registry, name, version):
    m = meta(registry, name)
    t = m.get("time", {})
    return [v for v in m["versions"] if t.get(v, "") > t.get(version, "")]


def check():
    data = json.load(open(MATRIX, encoding="utf-8"))
    failures, warnings = [], []
    jobs = []
    for n in data["networks"]:
        for key, ver in (n.get("admob") or {}).items():
            jobs.append((n["network"], "admob", OPENUPM, GMA_PREFIX + key, ver))
        for key, (va, vi) in (n.get("max") or {}).items():
            jobs.append((n["network"], "max-android", APPLOVIN, f"{MAX_PREFIX}{key}.android", va))
            jobs.append((n["network"], "max-ios", APPLOVIN, f"{MAX_PREFIX}{key}.ios", vi))
    with ThreadPoolExecutor(12) as ex:
        pins = list(ex.map(lambda j: network_pins(j[2], j[3], j[4]), jobs))
    by_net = {}
    for j, p in zip(jobs, pins):
        by_net.setdefault(j[0], []).append((j, p))
        failures += p["errors"]
        gma_req = p["upm"].get("com.google.ads.mobile")
        if gma_req and tuple(map(int, gma_req.split("."))) > tuple(map(int, data["core"]["com.google.ads.mobile"].split("."))):
            failures.append(f"{j[3]}@{j[4]} needs com.google.ads.mobile {gma_req}")
        gi = p["ios"].get("Google-Mobile-Ads-SDK")
        if gi and j[1] == "admob" and gi.startswith("~>"):
            want = gi[2:].strip().split(".")[0]
            if want != data["native"]["gma_ios"].split(".")[0]:
                failures.append(f"{j[3]}@{j[4]} wants Google-Mobile-Ads-SDK {gi}, set has {data['native']['gma_ios']}")
        elif gi and gi != data["native"]["gma_ios"]:
            failures.append(f"{j[3]}@{j[4]} pins Google-Mobile-Ads-SDK {gi}, set has {data['native']['gma_ios']}")
        al = p["ios"].get("AppLovinSDK")
        if al and al != data["native"]["applovin_sdk"]:
            failures.append(f"{j[3]}@{j[4]} pins AppLovinSDK {al}, MAX core ships {data['native']['applovin_sdk']}")
    for net, rows in by_net.items():
        for plat in ("android", "ios"):
            seen = {}
            for j, p in rows:
                for k, v in p[plat].items():
                    if k in ("Google-Mobile-Ads-SDK", "AppLovinSDK"):
                        continue
                    seen.setdefault(k, {})[f"{j[3]}@{j[4]}"] = v
            for k, who in seen.items():
                vs = set(who.values())
                if len(vs) > 1:
                    msg = f"{net} {plat}: {k} " + ", ".join(f"{w} -> {v}" for w, v in who.items())
                    patch_only = len({".".join(v.split(".")[:2]) for v in vs}) == 1
                    (warnings if plat == "android" and patch_only else failures).append(msg)
    # GameLib Setup installs the AdMob adapters from its catalog: it must carry the same pins.
    catalog = open(os.path.join(ROOT, "Packages", "com.flapgame.gamelib.core", "Editor", "Setup",
                                "GameLibModuleCatalog.cs"), encoding="utf-8").read()
    pinned = dict(re.findall(r'AdMobMediation\("([^"]+)",\s*"[^"]*",\s*"([^"]+)"\)', catalog))
    want = {k: v for n in data["networks"] for k, v in (n.get("admob") or {}).items()}
    for k in sorted(set(pinned) | set(want)):
        if pinned.get(k) != want.get(k):
            failures.append(f"GameLibModuleCatalog AdMobMediation {k}: catalog {pinned.get(k)}, version set {want.get(k)}")
    print(f"Checked {len(jobs)} packages of {MATRIX}")
    for w in warnings:
        print("WARN", w)
    for f in failures:
        print("FAIL", f)
    print("Newer versions published since the pinned ones (try them, then re-run check):")
    for j in jobs:
        nv = newer(j[2], j[3], j[4])
        if nv:
            print(f"  {j[3]}@{j[4]}: {', '.join(nv)}")
    for core, reg in (("com.google.ads.mobile", OPENUPM), ("com.applovin.mediation.ads", APPLOVIN)):
        nv = newer(reg, core, data["core"][core])
        if nv:
            print(f"  {core}@{data['core'][core]}: {', '.join(nv)}")
    return 1 if failures else 0


# GameHub release config keys (max_mediation / admob_mediation) -> package suffixes of the set.
GAMEHUB_MAX = {
    "bidmachine": "bidmachine", "bigo_ads": "bigoads", "chartboost": "chartboost", "dt_exchange": "fyber",
    "facebook": "facebook", "google_ad_manager": "googleadmanager", "google_admob": "google", "inmobi": "inmobi",
    "ironsource": "ironsource", "liftoff_monetize": "vungle", "line": "line", "mintegral": "mintegral",
    "mobilefuse": "mobilefuse", "moloco": "moloco", "ogury": "ogurypresage", "pangle": "bytedance",
    "pubmatic": "pubmatic", "smaato": "smaato", "unity_ads": "unityads", "verve": "verve", "yandex": "yandex",
    "yso_network": "ysonetwork",
}
GAMEHUB_MAX_UNSUPPORTED = {"csj", "hyprmx", "maio", "tencent", "vk_ad_network"}  # obsolete in MAX 8.6.6
GAMEHUB_ADMOB = {
    "applovin": "applovin", "bidmachine": "bidmachine", "bigo": "bigo", "chartboost": "chartboost",
    "dt_exchange": "dtexchange", "i_mobile": "imobile", "inmobi": "inmobi", "ironsource": "ironsource",
    "liftoff_monetize": "liftoffmonetize", "line": "line", "maio": "maio", "meta_audience_network": "metaaudiencenetwork",
    "mintegral": "mintegral", "moloco": "moloco", "mytarget": "mytarget", "pangle": "pangle", "pubmatic": "pubmatic",
    "unity_ads": "unity", "verve": "verve",
}


def gamehub_selection(config_path):
    """(admob keys, max keys) a GameHub release config enables, as package suffixes of the set."""
    cfg = json.load(open(config_path, encoding="utf-8"))
    on = lambda sec: {k for k, v in (cfg.get(sec) or {}).items() if str(v).lower() == "true"}
    max_on, admob_on = on("max_mediation") - {"max"}, on("admob_mediation")
    for k in sorted((max_on - set(GAMEHUB_MAX)) | (admob_on - set(GAMEHUB_ADMOB))):
        why = "obsolete in MAX 8.6.6, skipped" if k in GAMEHUB_MAX_UNSUPPORTED else "unknown key, skipped"
        print(f"WARN GameHub network '{k}': {why}")
    return {GAMEHUB_ADMOB[k] for k in admob_on if k in GAMEHUB_ADMOB}, {GAMEHUB_MAX[k] for k in max_on if k in GAMEHUB_MAX}


def manifest(project, gamehub=None, platform=None):
    data = json.load(open(MATRIX, encoding="utf-8"))
    admob_keys, max_keys = gamehub_selection(gamehub) if gamehub else (None, None)
    pk = lambda n: "file:" + os.path.join(ROOT, "Packages", n).replace("\\", "/")
    deps = {
        "com.unity.ugui": "2.0.0", "com.unity.modules.unitywebrequest": "1.0.0", "com.unity.modules.androidjni": "1.0.0",
        "com.unity.modules.jsonserialize": "1.0.0", "com.unity.modules.imgui": "1.0.0", "com.unity.modules.audio": "1.0.0",
        "com.unity.modules.ui": "1.0.0",
    }
    # build: raises the Pods deployment target (Xcode 27 rejects the pods' 10.0-12.0) - see mediation-versions.md.
    for n in ("core", "ads", "ads.admob", "ads.max", "build"):
        deps[f"com.flapgame.gamelib.{n}"] = pk(f"com.flapgame.gamelib.{n}")
    deps.update(data["core"])
    for n in data["networks"]:
        for key, ver in (n.get("admob") or {}).items():
            if admob_keys is None or key in admob_keys:
                deps[GMA_PREFIX + key] = ver
        for key, (va, vi) in (n.get("max") or {}).items():
            if max_keys is not None and key not in max_keys:
                continue
            if platform in (None, "android"):
                deps[f"{MAX_PREFIX}{key}.android"] = va
            if platform in (None, "ios"):
                deps[f"{MAX_PREFIX}{key}.ios"] = vi
    out = {
        "scopedRegistries": [
            {"name": "AppLovin MAX Unity", "url": APPLOVIN + "/",
             "scopes": ["com.applovin.mediation.ads", "com.applovin.mediation.adapters", "com.applovin.mediation.dsp"]},
            {"name": "package.openupm.com", "url": OPENUPM,
             "scopes": ["com.google.ads.mobile", "com.google.external-dependency-manager"]},
        ],
        "dependencies": dict(sorted(deps.items())),
    }
    os.makedirs(os.path.join(project, "Packages"), exist_ok=True)
    path = os.path.join(project, "Packages", "manifest.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=2)
    print(f"Wrote {path} ({len(deps)} packages)")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "check":
        sys.exit(check())
    if len(sys.argv) >= 3 and sys.argv[1] == "manifest":
        args = sys.argv[3:]
        opt = lambda name: args[args.index(name) + 1] if name in args else None
        manifest(sys.argv[2], opt("--gamehub"), opt("--platform"))
        sys.exit(0)
    print(__doc__)
    sys.exit(2)
