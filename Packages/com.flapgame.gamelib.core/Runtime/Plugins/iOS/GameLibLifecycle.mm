// App lifecycle for GameLib core (AppLifecycleBehaviour.cs). Unity pauses on applicationWillResignActive, which also fires
// for Control Center, Notification Center, an incoming-call banner or a system alert; only a real trip to the background
// posts UIApplicationDidEnterBackgroundNotification. The flag tells the two apart.
#import <UIKit/UIKit.h>

static volatile bool gameLibEnteredBackground = false;

@interface GameLibLifecycleObserver : NSObject
@end

@implementation GameLibLifecycleObserver

+ (void)load
{
    [[NSNotificationCenter defaultCenter] addObserverForName:UIApplicationDidEnterBackgroundNotification
                                                      object:nil
                                                       queue:nil
                                                  usingBlock:^(NSNotification *note) { gameLibEnteredBackground = true; }];
}

@end

extern "C" {

void _GameLibLifecycle_ResetEnteredBackground()
{
    gameLibEnteredBackground = false;
}

int _GameLibLifecycle_EnteredBackground()
{
    return gameLibEnteredBackground ? 1 : 0;
}

}
