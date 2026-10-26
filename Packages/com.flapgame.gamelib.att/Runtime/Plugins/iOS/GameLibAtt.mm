// App Tracking Transparency for GameLib AppsFlyer (AttPrompt.cs). AppTrackingTransparency.framework is linked weakly by
// the iOS post-processor, so devices before iOS 14 still launch; they report -1 (not applicable).
#import <Foundation/Foundation.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>

typedef void (*GameLibAttCallback)(int status);

extern "C" {

int _GameLibAtt_GetStatus()
{
    if (@available(iOS 14, *))
        return (int)[ATTrackingManager trackingAuthorizationStatus];
    return -1;
}

void _GameLibAtt_Request(GameLibAttCallback callback)
{
    if (@available(iOS 14, *))
    {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
            dispatch_async(dispatch_get_main_queue(), ^{ callback((int)status); });
        }];
        return;
    }
    callback(-1);
}

}
