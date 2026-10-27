// GameLibAppIcon.mm - alternate app icons for the GameLib App Icon package.
//
// Exposed to C# via [DllImport("__Internal")] in AppIconBackends.cs. The icons themselves are asset catalog app icon sets
// written into the Xcode project by AppIconIosPostProcessor (ASSETCATALOG_COMPILER_ALTERNATE_APPICON_NAMES).
// NOT compiled on this machine (no Xcode) - API usage checked against Apple docs only.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#include <stdlib.h>
#include <string.h>

typedef void (*GameLibAppIconCallback)(int requestId, const char *error);

// The C# marshaller frees a returned string with free(): hand it a malloc'd copy.
static char *GameLibAppIcon_Copy(NSString *text)
{
    if (text == nil) return NULL;
    const char *utf8 = text.UTF8String;
    char *copy = (char *)malloc(strlen(utf8) + 1);
    strcpy(copy, utf8);
    return copy;
}

extern "C" {

int _GameLibAppIcon_IsSupported(void)
{
    if (@available(iOS 10.3, *)) {
        return UIApplication.sharedApplication.supportsAlternateIcons ? 1 : 0;
    }
    return 0;
}

char *_GameLibAppIcon_Current(void)
{
    if (@available(iOS 10.3, *)) {
        return GameLibAppIcon_Copy(UIApplication.sharedApplication.alternateIconName);
    }
    return NULL;
}

void _GameLibAppIcon_Set(const char *iconName, int requestId, GameLibAppIconCallback callback)
{
    NSString *name = (iconName != NULL && iconName[0] != '\0') ? [NSString stringWithUTF8String:iconName] : nil;
    dispatch_async(dispatch_get_main_queue(), ^{
        if (@available(iOS 10.3, *)) {
            [UIApplication.sharedApplication setAlternateIconName:name completionHandler:^(NSError *error) {
                // Apple does not say which queue runs the handler: answer C# on the main (Unity) thread.
                NSString *message = error != nil ? error.localizedDescription : nil;
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (callback != NULL) callback(requestId, message != nil ? message.UTF8String : NULL);
                });
            }];
        } else if (callback != NULL) {
            callback(requestId, "alternate icons need iOS 10.3");
        }
    });
}

}
