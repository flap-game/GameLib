// GameLibHaptics.mm — native iOS haptics for the GameLib Haptics package.
//
// Semantic presets use UIFeedbackGenerator (Taptic Engine, iOS 10+). The generators are created once
// and kept prepared: prepare() warms the Taptic Engine for a short window, so each trigger re-prepares
// its generator for the next hit instead of allocating a new one per call.
// Custom Vibrate() uses Core Haptics (CHHapticEngine, iOS 13+) for real duration / intensity /
// count control, falling back to an impact generator on older devices.
//
// Exposed to C# via [DllImport("__Internal")] in IOSHapticBackend.cs.
// NOT compiled on this machine (no Xcode) — API usage checked against Apple docs only.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <CoreHaptics/CoreHaptics.h>

API_AVAILABLE(ios(13.0))
static CHHapticEngine *gEngine = nil;

#pragma mark - Capability

// Cached: hardware capability does not change during the process lifetime.
static BOOL GameLibHaptics_QuerySupported(void)
{
    // No iPad model ships a Taptic Engine; UIFeedbackGenerator silently no-ops there and
    // CHHapticEngine reports supportsHaptics == NO. Say so up front so the service can gate on it.
    if (UIDevice.currentDevice.userInterfaceIdiom == UIUserInterfaceIdiomPad) {
        return NO;
    }
    if (@available(iOS 13.0, *)) {
        return CHHapticEngine.capabilitiesForHardware.supportsHaptics;
    }
    // iOS 10–12: no capability API. UIFeedbackGenerator exists on every iPhone from the 7 onwards;
    // on older phones it no-ops. Treat phones as supported.
    return UIDevice.currentDevice.userInterfaceIdiom == UIUserInterfaceIdiomPhone;
}

static BOOL GameLibHaptics_IsSupported(void)
{
    static dispatch_once_t onceToken;
    static BOOL supported = NO;
    dispatch_once(&onceToken, ^{
        supported = GameLibHaptics_QuerySupported();
    });
    return supported;
}

#pragma mark - Cached UIFeedbackGenerators

// Index = UIImpactFeedbackStyle ordinal used by IOSHapticBackend.cs: light, medium, heavy, soft, rigid.
enum { kImpactLight = 0, kImpactMedium = 1, kImpactHeavy = 2, kImpactSoft = 3, kImpactRigid = 4, kImpactCount = 5 };

static UIImpactFeedbackGenerator *gImpact[kImpactCount];
static UINotificationFeedbackGenerator *gNotification = nil;
static UISelectionFeedbackGenerator *gSelection = nil;

API_AVAILABLE(ios(10.0))
static UIImpactFeedbackStyle GameLibHaptics_StyleForIndex(int index)
{
    switch (index) {
        case kImpactLight:  return UIImpactFeedbackStyleLight;
        case kImpactHeavy:  return UIImpactFeedbackStyleHeavy;
        case kImpactSoft:
            if (@available(iOS 13.0, *)) { return UIImpactFeedbackStyleSoft; }
            return UIImpactFeedbackStyleLight;
        case kImpactRigid:
            if (@available(iOS 13.0, *)) { return UIImpactFeedbackStyleRigid; }
            return UIImpactFeedbackStyleHeavy;
        case kImpactMedium:
        default:            return UIImpactFeedbackStyleMedium;
    }
}

API_AVAILABLE(ios(10.0))
static UIImpactFeedbackGenerator *GameLibHaptics_Impact(int index)
{
    if (index < 0 || index >= kImpactCount) index = kImpactMedium;
    if (gImpact[index] == nil) {
        gImpact[index] = [[UIImpactFeedbackGenerator alloc] initWithStyle:GameLibHaptics_StyleForIndex(index)];
        [gImpact[index] prepare];
    }
    return gImpact[index];
}

API_AVAILABLE(ios(10.0))
static UINotificationFeedbackGenerator *GameLibHaptics_Notification(void)
{
    if (gNotification == nil) {
        gNotification = [[UINotificationFeedbackGenerator alloc] init];
        [gNotification prepare];
    }
    return gNotification;
}

API_AVAILABLE(ios(10.0))
static UISelectionFeedbackGenerator *GameLibHaptics_Selection(void)
{
    if (gSelection == nil) {
        gSelection = [[UISelectionFeedbackGenerator alloc] init];
        [gSelection prepare];
    }
    return gSelection;
}

// Creates every generator and calls prepare() on each. Called once from the C# backend constructor
// and again after every trigger (per generator) so the engine stays warm between consecutive hits.
static void GameLibHaptics_PrepareAll(void)
{
    if (!GameLibHaptics_IsSupported()) return;
    if (@available(iOS 10.0, *)) {
        for (int i = 0; i < kImpactCount; i++) {
            [GameLibHaptics_Impact(i) prepare];
        }
        [GameLibHaptics_Notification() prepare];
        [GameLibHaptics_Selection() prepare];
    }
}

#pragma mark - Core Haptics engine

API_AVAILABLE(ios(13.0))
static CHHapticEngine *GameLibHaptics_Engine(void)
{
    if (!GameLibHaptics_IsSupported()) {
        return nil;
    }

    if (gEngine == nil) {
        NSError *error = nil;
        gEngine = [[CHHapticEngine alloc] initAndReturnError:&error];
        if (error != nil || gEngine == nil) {
            NSLog(@"[GameLibHaptics] engine create failed: %@", error);
            gEngine = nil;
            return nil;
        }

        gEngine.autoShutdownEnabled = YES;

        __weak CHHapticEngine *weakEngine = gEngine;
        gEngine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
            // Engine stopped (interruption, idle). It is restarted lazily on next use.
            (void)weakEngine;
        };
        gEngine.resetHandler = ^{
            NSError *startError = nil;
            [weakEngine startAndReturnError:&startError];
        };
    }

    NSError *startError = nil;
    [gEngine startAndReturnError:&startError];
    if (startError != nil) {
        NSLog(@"[GameLibHaptics] engine start failed: %@", startError);
        return nil;
    }
    return gEngine;
}

#pragma mark - C interface

extern "C" {

bool _GameLibHaptics_IsSupported(void)
{
    return GameLibHaptics_IsSupported() ? true : false;
}

void _GameLibHaptics_Prepare(void)
{
    GameLibHaptics_PrepareAll();
}

void _GameLibHaptics_Vibrate(float durationMs, float intensity, int count, float gapMs)
{
    if (!GameLibHaptics_IsSupported()) return;
    if (count < 1) count = 1;
    float intensityClamped = fminf(fmaxf(intensity, 0.0f), 1.0f);

    if (@available(iOS 13.0, *)) {
        CHHapticEngine *engine = GameLibHaptics_Engine();
        if (engine != nil) {
            NSMutableArray<CHHapticEvent *> *events = [NSMutableArray arrayWithCapacity:count];
            double durationSec = (double)durationMs / 1000.0;
            double gapSec = (double)gapMs / 1000.0;
            // Slightly higher sharpness for stronger hits gives a crisper feel.
            float sharpness = fminf(0.3f + intensityClamped * 0.5f, 1.0f);

            CHHapticEventParameter *intensityParam =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                              value:intensityClamped];
            CHHapticEventParameter *sharpnessParam =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                              value:sharpness];

            for (int i = 0; i < count; i++) {
                double relativeTime = (double)i * (durationSec + gapSec);
                CHHapticEvent *event =
                    [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                                  parameters:@[intensityParam, sharpnessParam]
                                                relativeTime:relativeTime
                                                    duration:durationSec];
                [events addObject:event];
            }

            NSError *error = nil;
            CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:events
                                                                    parameters:@[]
                                                                         error:&error];
            if (error == nil && pattern != nil) {
                id<CHHapticPatternPlayer> player = [engine createPlayerWithPattern:pattern error:&error];
                if (error == nil && player != nil) {
                    [player startAtTime:0 error:&error];
                    if (error == nil) {
                        return; // success
                    }
                }
            }
            NSLog(@"[GameLibHaptics] Core Haptics play failed: %@", error);
        }
    }

    // Fallback for iOS < 13 or when the engine failed to start: a single impact whose
    // strength approximates the requested intensity.
    if (@available(iOS 10.0, *)) {
        int index = kImpactMedium;
        if (intensityClamped < 0.34f)      index = kImpactLight;
        else if (intensityClamped > 0.66f) index = kImpactHeavy;

        UIImpactFeedbackGenerator *gen = GameLibHaptics_Impact(index);
        [gen impactOccurred];
        [gen prepare];
    }
}

void _GameLibHaptics_Impact(int style)
{
    if (!GameLibHaptics_IsSupported()) return;
    if (@available(iOS 10.0, *)) {
        UIImpactFeedbackGenerator *gen = GameLibHaptics_Impact(style);
        [gen impactOccurred];
        [gen prepare];
    }
}

void _GameLibHaptics_Notification(int type)
{
    if (!GameLibHaptics_IsSupported()) return;
    if (@available(iOS 10.0, *)) {
        UINotificationFeedbackType t = UINotificationFeedbackTypeSuccess;
        switch (type) {
            case 0: t = UINotificationFeedbackTypeSuccess; break;
            case 1: t = UINotificationFeedbackTypeWarning; break;
            case 2: t = UINotificationFeedbackTypeError; break;
            default: break;
        }
        UINotificationFeedbackGenerator *gen = GameLibHaptics_Notification();
        [gen notificationOccurred:t];
        [gen prepare];
    }
}

void _GameLibHaptics_Selection(void)
{
    if (!GameLibHaptics_IsSupported()) return;
    if (@available(iOS 10.0, *)) {
        UISelectionFeedbackGenerator *gen = GameLibHaptics_Selection();
        [gen selectionChanged];
        [gen prepare];
    }
}

void _GameLibHaptics_Cancel(void)
{
    if (@available(iOS 13.0, *)) {
        if (gEngine != nil) {
            [gEngine stopWithCompletionHandler:nil];
        }
    }
}

} // extern "C"
