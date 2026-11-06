// GameLib native ads (AdMob) - iOS bridge.
// Formats: native banner, native collapsible banner, native interstitial. Every view is built in code with
// Auto Layout and is a GADNativeAdView with its asset views registered, so the SDK tracks clicks and impressions.
// One native ad / one view per opaque `key` chosen by C#. All UI work runs on the main queue; events go back to C#
// through a single C callback (key, event, json). Requires ARC (CompileFlags -fobjc-arc in the .meta).
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#if __has_include(<GoogleMobileAds/GoogleMobileAds.h>)
#import <GoogleMobileAds/GoogleMobileAds.h>
#define GLNA_HAS_GMA 1
#else
#define GLNA_HAS_GMA 0
#endif

typedef void (*GameLibNativeAdsCallback)(const char *key, const char *event, const char *json);

static GameLibNativeAdsCallback gGLNACallback = NULL;

// ---------------------------------------------------------------------------------------------------------------
// Shared helpers (with and without the SDK)
// ---------------------------------------------------------------------------------------------------------------

static NSString *GLNACopyString(const char *value)
{
    if (value == NULL)
        return @"";
    NSString *copy = [NSString stringWithUTF8String:value];
    return copy != nil ? copy : @"";
}

static void GLNAEmit(NSString *key, NSString *event, NSDictionary *fields)
{
    GameLibNativeAdsCallback callback = gGLNACallback;
    if (callback == NULL)
        return;

    NSString *json = @"{}";
    if (fields != nil && fields.count > 0 && [NSJSONSerialization isValidJSONObject:fields])
    {
        NSData *data = [NSJSONSerialization dataWithJSONObject:fields options:0 error:nil];
        if (data != nil)
        {
            NSString *text = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
            if (text != nil)
                json = text;
        }
    }

    const char *keyUtf8 = key != nil ? key.UTF8String : "";
    const char *eventUtf8 = event != nil ? event.UTF8String : "";
    callback(keyUtf8 != NULL ? keyUtf8 : "", eventUtf8 != NULL ? eventUtf8 : "", json.UTF8String);
}

#if GLNA_HAS_GMA

// ---------------------------------------------------------------------------------------------------------------
// UI helpers
// ---------------------------------------------------------------------------------------------------------------

static UIColor *GLNAColor(int argb)
{
    uint32_t value = (uint32_t)argb;
    CGFloat a = ((value >> 24) & 0xFF) / 255.0;
    CGFloat r = ((value >> 16) & 0xFF) / 255.0;
    CGFloat g = ((value >> 8) & 0xFF) / 255.0;
    CGFloat b = (value & 0xFF) / 255.0;
    return [UIColor colorWithRed:r green:g blue:b alpha:a];
}

static CGFloat GLNAClamp(CGFloat value, CGFloat minValue, CGFloat maxValue)
{
    if (value < minValue)
        return minValue;
    if (value > maxValue)
        return maxValue;
    return value;
}

static BOOL GLNAHasText(NSString *text)
{
    return text != nil && [text isKindOfClass:[NSString class]] && text.length > 0;
}

static UIWindow *GLNAKeyWindow(void)
{
    UIApplication *app = [UIApplication sharedApplication];
    if (@available(iOS 13.0, *))
    {
        for (UIScene *scene in app.connectedScenes)
        {
            if (![scene isKindOfClass:[UIWindowScene class]])
                continue;
            if (scene.activationState != UISceneActivationStateForegroundActive)
                continue;
            for (UIWindow *window in ((UIWindowScene *)scene).windows)
            {
                if (window.isKeyWindow)
                    return window;
            }
        }
    }
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    UIWindow *keyWindow = app.keyWindow;
#pragma clang diagnostic pop
    if (keyWindow != nil)
        return keyWindow;
    id<UIApplicationDelegate> appDelegate = app.delegate;
    if (appDelegate != nil && [appDelegate respondsToSelector:@selector(window)])
        return [appDelegate window];
    return nil;
}

static UIViewController *GLNARootViewController(void)
{
    return GLNAKeyWindow().rootViewController;
}

static UIViewController *GLNATopViewController(void)
{
    UIViewController *controller = GLNARootViewController();
    while (controller.presentedViewController != nil && !controller.presentedViewController.isBeingDismissed)
        controller = controller.presentedViewController;
    return controller;
}

static UILabel *GLNAMakeLabel(CGFloat size, UIFontWeight weight, UIColor *color, NSInteger lines)
{
    UILabel *label = [[UILabel alloc] init];
    label.translatesAutoresizingMaskIntoConstraints = NO;
    label.font = [UIFont systemFontOfSize:size weight:weight];
    label.textColor = color;
    label.numberOfLines = lines;
    label.lineBreakMode = NSLineBreakByTruncatingTail;
    label.backgroundColor = [UIColor clearColor];
    return label;
}

// Small rounded "AD" attribution badge (bold 10pt, dark text on near-white, thin border), GLNABadgeHeight tall.
static const CGFloat GLNABadgeHeight = 15.0;
static const CGFloat GLNABadgeInset = 2.0;   // badge offset from the ad view's top-left corner
static const CGFloat GLNABadgeGap = 4.0;     // space between the badge and the asset row on its right

// `inLine`: the 24x16pt badge of a one-line bar, placed in the text line right after the headline.
// AdChoices in the top-right corner of the content (inside the safe area), not the SDK's default corner of the ad view,
// which spans the full screen width: in landscape the top of an expanded collapsible or of the interstitial is level
// with the Dynamic Island / notch. In portrait the two corners are the same. Bars at the bottom keep the SDK's corner
// (below the island; the content's corner would be over the CTA). Set before -setNativeAd:, as the SDK requires.
static NSLayoutConstraint *GLNAPlaceAdChoices(GADNativeAdView *adView, NSLayoutYAxisAnchor *top, NSLayoutXAxisAnchor *trailing)
{
    GADAdChoicesView *choices = [[GADAdChoicesView alloc] init];
    choices.translatesAutoresizingMaskIntoConstraints = NO;
    [adView addSubview:choices];
    NSLayoutConstraint *trailingConstraint = [choices.trailingAnchor constraintEqualToAnchor:trailing];
    [NSLayoutConstraint activateConstraints:@[
        [choices.topAnchor constraintEqualToAnchor:top],
        trailingConstraint,
        [choices.widthAnchor constraintEqualToConstant:15.0],
        [choices.heightAnchor constraintEqualToConstant:15.0],
    ]];
    adView.adChoicesView = choices;
    return trailingConstraint;
}

// The bar's content between the left and right safe-area insets (the notch / Dynamic Island side in landscape). Both
// the host view's and its window's safe areas bound it: after the app comes back from another app (e.g. the App
// Store, opened by an ad click) the host can report portrait insets (left / right 0) for a while in landscape, which
// put the media, the AD badge and the collapse button under the screen's rounded corners.
static void GLNAInsetSides(UILayoutGuide *content, UIView *host)
{
    NSMutableArray<NSLayoutConstraint *> *constraints = [NSMutableArray array];
    NSMutableArray<UILayoutGuide *> *guides = [NSMutableArray arrayWithObject:host.safeAreaLayoutGuide];
    if (host.window != nil)
        [guides addObject:host.window.safeAreaLayoutGuide];
    for (UILayoutGuide *guide in guides)
    {
        [constraints addObject:[content.leadingAnchor constraintGreaterThanOrEqualToAnchor:guide.leadingAnchor]];
        [constraints addObject:[content.trailingAnchor constraintLessThanOrEqualToAnchor:guide.trailingAnchor]];
    }
    // as wide as the insets allow
    NSLayoutConstraint *leading = [content.leadingAnchor constraintEqualToAnchor:host.leadingAnchor];
    NSLayoutConstraint *trailing = [content.trailingAnchor constraintEqualToAnchor:host.trailingAnchor];
    leading.priority = UILayoutPriorityRequired - 1;
    trailing.priority = UILayoutPriorityRequired - 1;
    [constraints addObjectsFromArray:@[ leading, trailing ]];
    [NSLayoutConstraint activateConstraints:constraints];
}

// ----- click area -----
// Only the asset views of a native ad register a click, as on Android. GMA counts a touch on any subview of the
// GADNativeAdView, so the views GameLib lays out around the assets (stacks, panels, chips, fillers, the "AD" badge,
// the meta separators) are "inside" a point only where an asset under them is; elsewhere the touch falls through to
// the ad view's own background, which GMA does not count.
static BOOL GLNAIsAsset(UIView *view)
{
    UIView *ad = view.superview;
    while (ad != nil && ![ad isKindOfClass:[GADNativeAdView class]])
        ad = ad.superview;
    if (ad == nil)
        return NO;
    GADNativeAdView *adView = (GADNativeAdView *)ad;
    return view == adView.headlineView || view == adView.bodyView || view == adView.iconView
        || view == adView.callToActionView || view == adView.starRatingView || view == adView.advertiserView
        || view == adView.storeView || view == adView.priceView || view == adView.mediaView || view == adView.imageView;
}

@protocol GLNAAssetOnly
@end

static BOOL GLNAPointOnAsset(UIView *container, CGPoint point, UIEvent *event)
{
    for (UIView *sub in container.subviews.reverseObjectEnumerator)
    {
        if (sub.hidden || sub.alpha < 0.01)
            continue;
        CGPoint inSub = [container convertPoint:point toView:sub];
        if ([sub conformsToProtocol:@protocol(GLNAAssetOnly)])
        {
            if ([sub pointInside:inSub withEvent:event])
                return YES;
        }
        else if ([sub pointInside:inSub withEvent:event] && (GLNAIsAsset(sub) || [sub isKindOfClass:[UIControl class]]))
        {
            return YES;
        }
    }
    return NO;
}

@interface GLNAAssetOnlyView : UIView <GLNAAssetOnly>
@end

@implementation GLNAAssetOnlyView
- (BOOL)pointInside:(CGPoint)point withEvent:(UIEvent *)event
{
    return [super pointInside:point withEvent:event] && GLNAPointOnAsset(self, point, event);
}
@end

@interface GLNAAssetOnlyStackView : UIStackView <GLNAAssetOnly>
@end

@implementation GLNAAssetOnlyStackView
- (BOOL)pointInside:(CGPoint)point withEvent:(UIEvent *)event
{
    return [super pointInside:point withEvent:event] && GLNAPointOnAsset(self, point, event);
}
@end

// The bottom-most subview of every GADNativeAdView, filling it: a touch that no asset takes lands here, and this view
// refuses to let the ad view's gesture recognizers (GMA's click detection) begin for it - the way a UIControl keeps a
// tap on itself from reaching its superview's tap recognizer. A touch on the bare ad view would not click either, but
// GMA also counts a touch inside the frame of any of the ad view's subviews (the stacks around the assets, the badge).
@interface GLNATouchSink : UIView
@end

@implementation GLNATouchSink
- (BOOL)gestureRecognizerShouldBegin:(UIGestureRecognizer *)gestureRecognizer
{
    return gestureRecognizer.view == self;
}

// The touch ends here: not passed up the responder chain to the game's view under the ad.
- (void)touchesBegan:(NSSet<UITouch *> *)touches withEvent:(UIEvent *)event
{
}

- (void)touchesMoved:(NSSet<UITouch *> *)touches withEvent:(UIEvent *)event
{
}

- (void)touchesEnded:(NSSet<UITouch *> *)touches withEvent:(UIEvent *)event
{
}

- (void)touchesCancelled:(NSSet<UITouch *> *)touches withEvent:(UIEvent *)event
{
}
@end

// The "AD" badge: takes the touches on itself (see GLNANativeAdView), so none reaches an asset under it.
@interface GLNABadgeView : GLNATouchSink
@end

@implementation GLNABadgeView
@end

static UIView *GLNAFindBadge(UIView *view, CGPoint point, UIEvent *event)
{
    for (UIView *sub in view.subviews.reverseObjectEnumerator)
    {
        if (sub.hidden || sub.alpha < 0.01)
            continue;
        CGPoint inSub = [view convertPoint:point toView:sub];
        if ([sub isKindOfClass:[GLNABadgeView class]])
        {
            if ([sub pointInside:inSub withEvent:event])
                return sub;
        }
        else if (![sub isKindOfClass:[GADMediaView class]])
        {
            UIView *found = GLNAFindBadge(sub, inSub, event);
            if (found != nil)
                return found;
        }
    }
    return nil;
}

// GADNativeAdView that hands a touch on the AD badge to the badge, whatever the SDK did to the subviews' order (it
// moves asset views such as the media view to the front, over the badge).
@interface GLNANativeAdView : GADNativeAdView
@end

@implementation GLNANativeAdView
- (UIView *)hitTest:(CGPoint)point withEvent:(UIEvent *)event
{
    UIView *hit = [super hitTest:point withEvent:event];
    if (hit == nil || !self.userInteractionEnabled || self.hidden)
        return hit;
    UIView *badge = GLNAFindBadge(self, point, event);
    return badge != nil ? badge : hit;
}
@end

static void GLNAAddTouchSink(GADNativeAdView *adView)
{
    GLNATouchSink *sink = [[GLNATouchSink alloc] init];
    sink.translatesAutoresizingMaskIntoConstraints = NO;
    sink.backgroundColor = [UIColor clearColor];
    sink.isAccessibilityElement = NO;
    [adView insertSubview:sink atIndex:0];
    [NSLayoutConstraint activateConstraints:@[
        [sink.topAnchor constraintEqualToAnchor:adView.topAnchor],
        [sink.bottomAnchor constraintEqualToAnchor:adView.bottomAnchor],
        [sink.leadingAnchor constraintEqualToAnchor:adView.leadingAnchor],
        [sink.trailingAnchor constraintEqualToAnchor:adView.trailingAnchor],
    ]];
}

static UIView *GLNAMakeAdBadge(BOOL inLine)
{
    UIView *badge = [[GLNABadgeView alloc] init];
    badge.translatesAutoresizingMaskIntoConstraints = NO;
    badge.backgroundColor = [UIColor colorWithWhite:0.97 alpha:1.0];
    badge.layer.cornerRadius = 3.0;
    badge.layer.borderWidth = 0.5;
    badge.layer.borderColor = [UIColor colorWithWhite:0.55 alpha:1.0].CGColor;
    badge.clipsToBounds = YES;
    badge.userInteractionEnabled = NO;

    UILabel *label = GLNAMakeLabel(10.0, UIFontWeightBold, [UIColor colorWithWhite:0.13 alpha:1.0], 1);
    label.text = @"AD";
    // The badge must hug "AD": with the default 251 hugging it ties with the row's labels and Auto Layout can
    // stretch the badge (measured 140pt wide on iOS), pushing the icon and text to the right.
    [label setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [label setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [badge addSubview:label];
    if (inLine)
    {
        [NSLayoutConstraint activateConstraints:@[
            [badge.widthAnchor constraintEqualToConstant:24.0],
            [badge.heightAnchor constraintEqualToConstant:16.0],
            [label.centerXAnchor constraintEqualToAnchor:badge.centerXAnchor],
            [label.centerYAnchor constraintEqualToAnchor:badge.centerYAnchor],
        ]];
        [badge setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        [badge setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        return badge;
    }
    [NSLayoutConstraint activateConstraints:@[
        [badge.heightAnchor constraintEqualToConstant:GLNABadgeHeight],
        [label.centerYAnchor constraintEqualToAnchor:badge.centerYAnchor],
        [label.leadingAnchor constraintEqualToAnchor:badge.leadingAnchor constant:4.0],
        [label.trailingAnchor constraintEqualToAnchor:badge.trailingAnchor constant:-4.0],
    ]];
    return badge;
}

// CTA: a rounded button that does not take touches itself, so the GADNativeAdView handles the click.
static UIButton *GLNAMakeCta(CGFloat fontSize, UIColor *background, UIColor *textColor)
{
    UIButton *button = [UIButton buttonWithType:UIButtonTypeCustom];
    button.translatesAutoresizingMaskIntoConstraints = NO;
    button.backgroundColor = background;
    button.layer.cornerRadius = 6.0;
    button.clipsToBounds = YES;
    button.userInteractionEnabled = NO;
    button.titleLabel.font = [UIFont systemFontOfSize:fontSize weight:UIFontWeightSemibold];
    button.titleLabel.adjustsFontSizeToFitWidth = YES;
    button.titleLabel.minimumScaleFactor = 0.6;
    button.titleLabel.lineBreakMode = NSLineBreakByTruncatingTail;
    [button setTitleColor:textColor forState:UIControlStateNormal];
    return button;
}

// Chevron / cross glyph drawn with a CAShapeLayer (no image assets).
@interface GLNAGlyphView : UIView
@property (nonatomic, assign) BOOL cross;
@property (nonatomic, strong) CAShapeLayer *shape;
@end

@implementation GLNAGlyphView

- (instancetype)initWithCross:(BOOL)cross color:(UIColor *)color
{
    self = [super initWithFrame:CGRectZero];
    if (self)
    {
        _cross = cross;
        self.translatesAutoresizingMaskIntoConstraints = NO;
        self.userInteractionEnabled = NO;
        self.backgroundColor = [UIColor clearColor];
        _shape = [CAShapeLayer layer];
        _shape.fillColor = [UIColor clearColor].CGColor;
        _shape.strokeColor = color.CGColor;
        _shape.lineWidth = 2.0;
        _shape.lineCap = kCALineCapRound;
        _shape.lineJoin = kCALineJoinRound;
        [self.layer addSublayer:_shape];
    }
    return self;
}

- (void)layoutSubviews
{
    [super layoutSubviews];
    CGRect bounds = self.bounds;
    self.shape.frame = bounds;
    CGFloat w = bounds.size.width;
    CGFloat h = bounds.size.height;
    UIBezierPath *path = [UIBezierPath bezierPath];
    if (self.cross)
    {
        [path moveToPoint:CGPointMake(w * 0.30, h * 0.30)];
        [path addLineToPoint:CGPointMake(w * 0.70, h * 0.70)];
        [path moveToPoint:CGPointMake(w * 0.70, h * 0.30)];
        [path addLineToPoint:CGPointMake(w * 0.30, h * 0.70)];
    }
    else
    {
        [path moveToPoint:CGPointMake(w * 0.28, h * 0.40)];
        [path addLineToPoint:CGPointMake(w * 0.50, h * 0.62)];
        [path addLineToPoint:CGPointMake(w * 0.72, h * 0.40)];
    }
    self.shape.path = path.CGPath;
}

@end

// Round glyph button: a `size` circle centred in a transparent `touchSize` square that takes the touches
// (touchSize <= size: the button is the circle itself).
// The circle sits centred in its touch square, or (cornerAnchored) in the square's top-right corner so the touch area
// extends left and down only (the collapse button next to the AdChoices corner).
static UIButton *GLNAMakeRoundGlyphButton(BOOL cross, CGFloat size, CGFloat touchSize, UIColor *background, UIColor *glyphColor,
                                          UIColor *borderColor, BOOL cornerAnchored)
{
    CGFloat side = MAX(size, touchSize);
    UIButton *button = [UIButton buttonWithType:UIButtonTypeCustom];
    button.translatesAutoresizingMaskIntoConstraints = NO;
    button.backgroundColor = [UIColor clearColor];

    UIView *circle = [[UIView alloc] init];
    circle.translatesAutoresizingMaskIntoConstraints = NO;
    circle.userInteractionEnabled = NO;
    circle.backgroundColor = background;
    circle.layer.cornerRadius = size * 0.5;
    circle.clipsToBounds = YES;
    if (borderColor != nil)
    {
        circle.layer.borderWidth = 1.0;
        circle.layer.borderColor = borderColor.CGColor;
    }
    [button addSubview:circle];

    GLNAGlyphView *glyph = [[GLNAGlyphView alloc] initWithCross:cross color:glyphColor];
    [circle addSubview:glyph];
    [NSLayoutConstraint activateConstraints:@[
        [button.widthAnchor constraintEqualToConstant:side],
        [button.heightAnchor constraintEqualToConstant:side],
        [circle.widthAnchor constraintEqualToConstant:size],
        [circle.heightAnchor constraintEqualToConstant:size],
        cornerAnchored ? [circle.trailingAnchor constraintEqualToAnchor:button.trailingAnchor]
                       : [circle.centerXAnchor constraintEqualToAnchor:button.centerXAnchor],
        cornerAnchored ? [circle.topAnchor constraintEqualToAnchor:button.topAnchor]
                       : [circle.centerYAnchor constraintEqualToAnchor:button.centerYAnchor],
        [glyph.topAnchor constraintEqualToAnchor:circle.topAnchor],
        [glyph.bottomAnchor constraintEqualToAnchor:circle.bottomAnchor],
        [glyph.leadingAnchor constraintEqualToAnchor:circle.leadingAnchor],
        [glyph.trailingAnchor constraintEqualToAnchor:circle.trailingAnchor],
    ]];
    return button;
}

static UIStackView *GLNAMakeStack(UILayoutConstraintAxis axis, CGFloat spacing, UIStackViewAlignment alignment)
{
    UIStackView *stack = [[GLNAAssetOnlyStackView alloc] init];
    stack.translatesAutoresizingMaskIntoConstraints = NO;
    stack.axis = axis;
    stack.spacing = spacing;
    stack.alignment = alignment;
    stack.distribution = UIStackViewDistributionFill;
    return stack;
}

static NSString *GLNARatingText(GADNativeAd *ad)
{
    NSDecimalNumber *rating = ad.starRating;
    if (rating == nil || rating.doubleValue <= 0.0)
        return nil;
    return [NSString stringWithFormat:@"%.1f★", rating.doubleValue];
}

static NSString *GLNAAdSource(GADResponseInfo *info)
{
    NSString *name = info.loadedAdNetworkResponseInfo.adSourceName;
    return GLNAHasText(name) ? name : @"";
}

// ---------------------------------------------------------------------------------------------------------------
// Row used by the banner and the collapsible: icon | [headline, body, meta] | CTA
// ---------------------------------------------------------------------------------------------------------------

@interface GLNARow : NSObject
@property (nonatomic, strong) UIStackView *stack;
@property (nonatomic, strong) UIImageView *icon;
@property (nonatomic, strong) UILabel *headline;
@property (nonatomic, strong) UILabel *body;
@property (nonatomic, strong) UIStackView *meta;
@property (nonatomic, strong) UILabel *rating;
@property (nonatomic, strong) UILabel *advertiser;
@property (nonatomic, strong) UILabel *store;
@property (nonatomic, strong) UILabel *price;
@property (nonatomic, strong) UIButton *cta;
@property (nonatomic, strong) NSLayoutConstraint *iconSize;
@property (nonatomic, strong) NSLayoutConstraint *ctaWidth;
@property (nonatomic, strong) NSLayoutConstraint *ctaHeight;
@property (nonatomic, strong) UIColor *secondaryColor;
@property (nonatomic, assign) BOOL compactMetaFits; // NO: the bar is too short for the meta line in compact mode
@property (nonatomic, assign) BOOL compactBodyFits; // NO: the bar (e.g. a 32pt smart banner) only fits the headline
@property (nonatomic, strong) UIStackView *column;   // headline / body / meta: stacked, or side by side (oneLine)
@property (nonatomic, strong) UIView *inlineBadge;   // oneLine only: "AD" right after the headline
@property (nonatomic, assign) BOOL oneLine;          // bar under 40pt: headline | AD | body | meta | CTA on one line
@end

@implementation GLNARow
@end

// Android's line height for a text size (about 4/3 of the size, e.g. 16 for 12sp, 14 for 10sp): the line-fit rules
// below use it so iOS keeps and drops the same lines as Android on the same bar.
static CGFloat GLNAAndroidLineHeight(UIFont *font)
{
    return ceil(font.pointSize * 4.0 / 3.0);
}

// Sizes a row for a bar `barHeight` tall - Android's layoutRow rules:
// - bars <= 60pt ("small"): headline 12, body 10 (1 line), meta 10, CTA text 11; icon = bar - 16 (bar - 8 under
//   40pt); CTA max(32, bar * 0.55) tall (at most bar - 6); the meta line goes when headline + body + meta + 2 do not
//   fit in bar - 6, the body when headline + body do not;
// - taller bars: headline 13, body 11, meta 10, CTA text 13; icon = clamp(bar * 0.7, 24, 72); CTA
//   clamp(bar * 0.55, 24, 44) tall;
// - CTA width clamp(bar * 1.2, 52, 96), 80 under 40pt.
// Called when the row is built and again when a collapsible changes height (expanded 76pt row, collapsed bar).
static void GLNASizeRow(GLNARow *row, CGFloat barHeight)
{
    BOOL small = barHeight <= 60.0;
    CGFloat inner = MAX(0.0, barHeight - 6.0);
    row.headline.font = [UIFont systemFontOfSize:small ? 12.0 : 13.0 weight:UIFontWeightBold];
    row.body.font = [UIFont systemFontOfSize:small ? 10.0 : 11.0 weight:UIFontWeightRegular];
    UIFont *meta = [UIFont systemFontOfSize:10.0 weight:UIFontWeightRegular];
    for (UILabel *label in @[ row.rating, row.advertiser, row.store, row.price ])
        label.font = meta;
    row.cta.titleLabel.font = [UIFont systemFontOfSize:small ? 11.0 : 13.0 weight:UIFontWeightSemibold];

    row.iconSize.constant = small ? MAX(16.0, barHeight - 2.0 * (barHeight < 40.0 ? 4.0 : 8.0))
                                  : GLNAClamp(round(barHeight * 0.7), 24.0, 72.0);
    row.ctaWidth.constant = barHeight < 40.0 ? 80.0 : GLNAClamp(round(barHeight * 1.2), 52.0, 96.0);
    row.ctaHeight.constant = small ? MIN(MAX(32.0, round(barHeight * 0.55)), inner)
                                   : GLNAClamp(round(barHeight * 0.55), 24.0, 44.0);

    CGFloat headline = GLNAAndroidLineHeight(row.headline.font), body = GLNAAndroidLineHeight(row.body.font);
    row.compactMetaFits = !small || row.oneLine || headline + body + GLNAAndroidLineHeight(meta) + 2.0 <= inner;
    row.compactBodyFits = !small || row.oneLine || headline + body <= inner;
}

// `allowOneLine`: a bar under 40pt (a landscape smart banner) lays its texts out on one line, as on Android:
// icon 24 | headline 12 bold, AD badge 24x16 (6pt gap), body 10 (takes the room left, ellipsized, 8pt gap),
// meta "rating • advertiser • store" 10 (8pt gap) | CTA 80 wide. GLNAFitOneLine drops what does not fit.
static GLNARow *GLNAMakeRow(CGFloat barHeight, UIColor *text, UIColor *cta, UIColor *ctaText, BOOL allowOneLine)
{
    // Initial sizes; GLNASizeRow (called at the end) sets the final ones for the bar.
    BOOL small = barHeight <= 60.0;
    BOOL oneLine = allowOneLine && barHeight < 40.0;
    CGFloat headlineSize = small ? 12.0 : 13.0;
    CGFloat bodySize = small ? 10.0 : 11.0;
    CGFloat metaSize = (small && !oneLine) ? 8.0 : 10.0;
    CGFloat columnSpacing = 1.0;
    UIColor *secondary = [text colorWithAlphaComponent:CGColorGetAlpha(text.CGColor) * 0.6];

    GLNARow *row = [[GLNARow alloc] init];
    row.secondaryColor = secondary;
    row.compactMetaFits = YES;
    row.compactBodyFits = YES;
    row.stack = GLNAMakeStack(UILayoutConstraintAxisHorizontal, 8.0, UIStackViewAlignmentCenter);

    row.icon = [[UIImageView alloc] init];
    row.icon.translatesAutoresizingMaskIntoConstraints = NO;
    row.icon.contentMode = UIViewContentModeScaleAspectFill;
    row.icon.clipsToBounds = YES;
    row.icon.layer.cornerRadius = 6.0;
    CGFloat iconSide = small ? MAX(barHeight - 8.0, 16.0) : GLNAClamp(barHeight * 0.7, 24.0, 72.0);
    row.iconSize = [row.icon.widthAnchor constraintEqualToConstant:iconSide];
    [NSLayoutConstraint activateConstraints:@[
        row.iconSize,
        [row.icon.heightAnchor constraintEqualToAnchor:row.icon.widthAnchor],
    ]];
    [row.icon setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [row.icon setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

    UIStackView *column = oneLine ? GLNAMakeStack(UILayoutConstraintAxisHorizontal, 0.0, UIStackViewAlignmentCenter)
                                  : GLNAMakeStack(UILayoutConstraintAxisVertical, columnSpacing, UIStackViewAlignmentFill);
    row.column = column;
    row.oneLine = oneLine;
    [column setContentHuggingPriority:UILayoutPriorityDefaultLow forAxis:UILayoutConstraintAxisHorizontal];
    [column setContentCompressionResistancePriority:UILayoutPriorityDefaultLow forAxis:UILayoutConstraintAxisHorizontal];

    row.headline = GLNAMakeLabel(headlineSize, UIFontWeightBold, text, 1);
    row.body = GLNAMakeLabel(bodySize, UIFontWeightRegular, text, 1);

    row.meta = GLNAMakeStack(UILayoutConstraintAxisHorizontal, 0.0, UIStackViewAlignmentCenter);
    row.rating = GLNAMakeLabel(metaSize, UIFontWeightRegular, secondary, 1);
    row.advertiser = GLNAMakeLabel(metaSize, UIFontWeightRegular, secondary, 1);
    row.store = GLNAMakeLabel(metaSize, UIFontWeightRegular, secondary, 1);
    row.price = GLNAMakeLabel(metaSize, UIFontWeightRegular, secondary, 1);
    [row.rating setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [row.meta addArrangedSubview:row.rating];
    [row.meta addArrangedSubview:row.advertiser];
    [row.meta addArrangedSubview:row.store];
    [row.meta addArrangedSubview:row.price];
    UIView *metaFiller = [[GLNAAssetOnlyView alloc] init];
    metaFiller.translatesAutoresizingMaskIntoConstraints = NO;
    [metaFiller setContentHuggingPriority:UILayoutPriorityDefaultLow - 1 forAxis:UILayoutConstraintAxisHorizontal];
    [row.meta addArrangedSubview:metaFiller];

    if (oneLine)
    {
        // The meta hugs its texts (no filler); the body takes the room left and is the first to shrink, then the
        // meta, then the headline. GLNAFitOneLine drops meta parts and the body after layout.
        metaFiller.hidden = YES;
        row.inlineBadge = GLNAMakeAdBadge(YES);
        [column addArrangedSubview:row.headline];
        [column addArrangedSubview:row.inlineBadge];
        [column addArrangedSubview:row.body];
        [column addArrangedSubview:row.meta];
        [column setCustomSpacing:6.0 afterView:row.headline];
        [column setCustomSpacing:8.0 afterView:row.inlineBadge];
        [column setCustomSpacing:8.0 afterView:row.body];
        [row.headline setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        [row.headline setContentCompressionResistancePriority:UILayoutPriorityDefaultHigh - 10 forAxis:UILayoutConstraintAxisHorizontal];
        [row.body setContentHuggingPriority:UILayoutPriorityDefaultLow - 2 forAxis:UILayoutConstraintAxisHorizontal];
        [row.body setContentCompressionResistancePriority:UILayoutPriorityDefaultLow forAxis:UILayoutConstraintAxisHorizontal];
        [row.meta setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        [row.meta setContentCompressionResistancePriority:UILayoutPriorityDefaultLow + 50 forAxis:UILayoutConstraintAxisHorizontal];
    }
    else
    {
        [column addArrangedSubview:row.headline];
        [column addArrangedSubview:row.body];
        [column addArrangedSubview:row.meta];
    }

    row.cta = GLNAMakeCta(headlineSize, cta, ctaText);
    // tiny bars (< 40pt) get a wider button: the label is the only text room left besides the headline
    row.ctaWidth = [row.cta.widthAnchor constraintEqualToConstant:barHeight < 40.0 ? 80.0 : GLNAClamp(barHeight * 1.2, 52.0, 96.0)];
    CGFloat ctaHeight = small ? MIN(GLNAClamp(barHeight - 16.0, 32.0, 40.0), barHeight - 8.0)
                              : GLNAClamp(barHeight - 16.0, 22.0, 40.0);
    row.ctaHeight = [row.cta.heightAnchor constraintEqualToConstant:ctaHeight];
    [NSLayoutConstraint activateConstraints:@[ row.ctaWidth, row.ctaHeight ]];
    [row.cta setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [row.cta setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

    [row.stack addArrangedSubview:row.icon];
    [row.stack addArrangedSubview:column];
    [row.stack addArrangedSubview:row.cta];
    GLNASizeRow(row, barHeight);
    return row;
}

// Fills the row and hides every asset view whose asset is missing. `full` shows advertiser / store / price and
// two body lines (expanded collapsible); otherwise the meta line is "rating  •  advertiser  •  store".
static void GLNAFillRow(GLNARow *row, GADNativeAd *ad, BOOL full, BOOL showPrice)
{
    row.headline.text = ad.headline;
    row.headline.hidden = !GLNAHasText(ad.headline);
    row.body.text = ad.body;
    row.body.hidden = !GLNAHasText(ad.body) || (!full && !row.compactBodyFits);
    row.body.numberOfLines = full ? 2 : 1;

    UIImage *iconImage = ad.icon.image;
    row.icon.image = iconImage;
    row.icon.hidden = iconImage == nil;

    [row.cta setTitle:ad.callToAction forState:UIControlStateNormal];
    row.cta.hidden = !GLNAHasText(ad.callToAction);

    NSString *rating = GLNARatingText(ad);
    BOOL any = NO;
    row.rating.text = rating;
    row.rating.hidden = rating == nil;
    any = rating != nil;

    BOOL showAdvertiser = GLNAHasText(ad.advertiser);
    row.advertiser.text = showAdvertiser ? [NSString stringWithFormat:@"%@%@", any ? @"  •  " : @"", ad.advertiser] : nil;
    row.advertiser.hidden = !showAdvertiser;
    any = any || showAdvertiser;

    BOOL showStore = GLNAHasText(ad.store);
    row.store.text = showStore ? [NSString stringWithFormat:@"%@%@", any ? @"  •  " : @"", ad.store] : nil;
    row.store.hidden = !showStore;
    any = any || showStore;

    BOOL priceVisible = showPrice && GLNAHasText(ad.price);
    row.price.text = priceVisible ? [NSString stringWithFormat:@"%@%@", any ? @"  •  " : @"", ad.price] : nil;
    row.price.hidden = !priceVisible;
    any = any || priceVisible;

    // `full` rows (expanded collapsible, >= 76pt) always have room for the meta line.
    row.meta.hidden = !any || (!full && !row.compactMetaFits);
}

// Compact (collapsed) row: one body line, only the rating on the meta line.
static void GLNASetRowCompact(GLNARow *row, GADNativeAd *ad, BOOL compact)
{
    if (compact)
    {
        row.body.numberOfLines = 1;
        row.body.hidden = !GLNAHasText(ad.body) || !row.compactBodyFits;
        row.advertiser.hidden = YES;
        row.store.hidden = YES;
        row.price.hidden = YES;
        row.meta.hidden = row.rating.hidden || !row.compactMetaFits;
    }
    else
    {
        GLNAFillRow(row, ad, YES, YES);
    }
}

static NSString *const GLNAMetaSeparator = @"  •  ";

// Re-prefixes the visible meta parts so only the ones after a visible part start with the separator.
static void GLNAUpdateMetaSeparators(GLNARow *row)
{
    BOOL any = NO;
    for (UILabel *label in @[ row.rating, row.advertiser, row.store, row.price ])
    {
        NSString *text = label.text;
        if ([text hasPrefix:GLNAMetaSeparator])
            text = [text substringFromIndex:GLNAMetaSeparator.length];
        if (label.hidden || !GLNAHasText(text))
            continue;
        label.text = any ? [GLNAMetaSeparator stringByAppendingString:text] : text;
        any = YES;
    }
    row.meta.hidden = !any;
}

static CGFloat GLNAFittingWidth(UIView *view)
{
    if (view == nil || view.hidden)
        return 0.0;
    return ceil([view systemLayoutSizeFittingSize:UILayoutFittingCompressedSize].width);
}

// One-line bar, after layout (the text line has its final width): drops what does not fit, lowest priority first -
// price, store, rating, advertiser, then the body when 60pt of it no longer fits next to the meta. Headline, AD and the
// button always stay. Same rule as Android's fitOneLine.
static void GLNAFitOneLine(GLNARow *row)
{
    if (row == nil || !row.oneLine)
        return;
    CGFloat width = row.column.bounds.size.width;
    if (width <= 0.0)
        return;
    CGFloat fixed = GLNAFittingWidth(row.headline) + GLNAFittingWidth(row.inlineBadge) + 6.0;
    CGFloat minBody = row.body.hidden ? 0.0 : 60.0 + 8.0;
    CGFloat (^metaWidth)(void) = ^CGFloat {
        return row.meta.hidden ? 0.0 : GLNAFittingWidth(row.meta) + 8.0;
    };
    NSArray<UILabel *> *dropOrder = @[ row.price, row.store, row.rating, row.advertiser ];
    NSUInteger i = 0;
    while (fixed + minBody + metaWidth() > width && i < dropOrder.count)
    {
        dropOrder[i++].hidden = YES;
        GLNAUpdateMetaSeparators(row);
    }
    if (fixed + minBody + metaWidth() > width)
        row.body.hidden = YES;
    [row.column setNeedsLayout];
}

static void GLNARegisterRow(GADNativeAdView *adView, GLNARow *row)
{
    adView.headlineView = row.headline;
    adView.bodyView = row.body;
    adView.iconView = row.icon;
    adView.callToActionView = row.cta;
    adView.starRatingView = row.rating;
    adView.advertiserView = row.advertiser;
    adView.storeView = row.store;
    adView.priceView = row.price;
}

// ---------------------------------------------------------------------------------------------------------------
// Per-key state
// ---------------------------------------------------------------------------------------------------------------

typedef NS_ENUM(NSInteger, GLNAShownFormat) {
    GLNAShownNone = 0,
    GLNAShownBanner = 1,
    GLNAShownCollapsible = 2,
    GLNAShownInterstitial = 3,
};

@class GLNAInterstitialController;

@interface GLNAState : NSObject
@property (nonatomic, copy) NSString *key;
@property (nonatomic, copy) NSString *adUnitId;
@property (nonatomic, strong) GADAdLoader *loader;       // held until the load finishes
@property (nonatomic, strong) GADNativeAd *pendingAd;    // loaded, not shown yet
@property (nonatomic, strong) GADNativeAd *shownAd;      // bound to the visible view (spent)
@property (nonatomic, assign) GLNAShownFormat format;
@property (nonatomic, strong) GADNativeAdView *adView;   // banner / collapsible view on the root VC view
@property (nonatomic, strong) GLNARow *row;
@property (nonatomic, strong) GADMediaView *mediaView;   // collapsible only
@property (nonatomic, strong) UIButton *collapseButton;  // collapsible only
@property (nonatomic, strong) NSLayoutConstraint *heightConstraint;
@property (nonatomic, strong) NSLayoutConstraint *rowHeightConstraint;
@property (nonatomic, strong) NSLayoutConstraint *rowLeadingCompact;  // collapsible: row right of the AD badge
@property (nonatomic, strong) NSLayoutConstraint *rowLeadingFull;     // collapsible expanded: badge sits over the media
@property (nonatomic, strong) NSLayoutConstraint *rowHostLeadingFull; // collapsible: row across the whole ad
@property (nonatomic, strong) NSLayoutConstraint *rowHostLeadingSide; // collapsible expanded in landscape: row right of the media
@property (nonatomic, assign) CGFloat collapsedHeight;
@property (nonatomic, assign) BOOL expanded;
@property (nonatomic, strong) UIView *rowHost;           // collapsible: holds the row (or the info column)
@property (nonatomic, strong) UIView *cornerBadge;       // banner / collapsible: "AD" in the top-left corner
@property (nonatomic, strong) NSLayoutConstraint *adChoicesTrailing; // collapsible: AdChoices' right edge
@property (nonatomic, strong) UIStackView *side;         // collapsible expanded in landscape: header / body / CTA column
@property (nonatomic, strong) NSTimer *collapseButtonTimer;
@property (nonatomic, strong) GLNAInterstitialController *interstitial;
@end

@implementation GLNAState
@end

// ---------------------------------------------------------------------------------------------------------------
// Interstitial controller
// ---------------------------------------------------------------------------------------------------------------

@interface GLNAInterstitialController : UIViewController
@property (nonatomic, copy) NSString *key;
@property (nonatomic, strong) GADNativeAd *nativeAd;
@property (nonatomic, assign) int closeDelaySeconds;
@property (nonatomic, assign) int remainingSeconds;
@property (nonatomic, strong) UIColor *bgColor;
@property (nonatomic, strong) UIColor *textColor;
@property (nonatomic, strong) UIColor *ctaColor;
@property (nonatomic, strong) UIColor *ctaTextColor;
@property (nonatomic, strong) GADNativeAdView *adView;
@property (nonatomic, strong) UILabel *countdownLabel;
@property (nonatomic, strong) UIButton *closeButton;
@property (nonatomic, strong) NSTimer *countdownTimer;
@property (nonatomic, assign) BOOL closing;
@property (nonatomic, copy) void (^onClose)(GLNAInterstitialController *controller);
- (void)stopTimer;
@end

static UIView *GLNAMakeChip(UILabel *label, UIColor *textColor)
{
    UIView *chip = [[GLNAAssetOnlyView alloc] init];
    chip.translatesAutoresizingMaskIntoConstraints = NO;
    chip.layer.cornerRadius = 4.0;
    chip.layer.borderWidth = 1.0;
    chip.layer.borderColor = [textColor colorWithAlphaComponent:CGColorGetAlpha(textColor.CGColor) * 0.35].CGColor;
    [chip addSubview:label];
    [NSLayoutConstraint activateConstraints:@[
        [label.topAnchor constraintEqualToAnchor:chip.topAnchor constant:3.0],
        [label.bottomAnchor constraintEqualToAnchor:chip.bottomAnchor constant:-3.0],
        [label.leadingAnchor constraintEqualToAnchor:chip.leadingAnchor constant:8.0],
        [label.trailingAnchor constraintEqualToAnchor:chip.trailingAnchor constant:-8.0],
    ]];
    return chip;
}

@implementation GLNAInterstitialController

// Full screen: no status bar, home indicator auto-hidden. The close / countdown row stays inside the safe area.
- (BOOL)prefersStatusBarHidden
{
    return YES;
}

- (BOOL)prefersHomeIndicatorAutoHidden
{
    return YES;
}

- (void)viewWillAppear:(BOOL)animated
{
    [super viewWillAppear:animated];
    [self setNeedsStatusBarAppearanceUpdate];
    [self setNeedsUpdateOfHomeIndicatorAutoHidden];
}

- (UIInterfaceOrientationMask)supportedInterfaceOrientations
{
    UIViewController *presenting = self.presentingViewController;
    if (presenting != nil)
        return presenting.supportedInterfaceOrientations;
    return [super supportedInterfaceOrientations];
}

- (void)viewDidLoad
{
    [super viewDidLoad];
    UIView *root = self.view;
    root.backgroundColor = [UIColor blackColor];
    UILayoutGuide *safe = root.safeAreaLayoutGuide;
    GADNativeAd *ad = self.nativeAd;
    UIColor *secondary = [self.textColor colorWithAlphaComponent:CGColorGetAlpha(self.textColor.CGColor) * 0.6];

    // The ad view starts below the close row so the SDK's AdChoices icon (top-right) never sits under the button.
    GADNativeAdView *adView = [[GLNANativeAdView alloc] init];
    adView.translatesAutoresizingMaskIntoConstraints = NO;
    adView.backgroundColor = [UIColor blackColor];
    [root addSubview:adView];
    GLNAAddTouchSink(adView);
    self.adView = adView;

    GADMediaView *media = [[GADMediaView alloc] init];
    media.translatesAutoresizingMaskIntoConstraints = NO;
    media.backgroundColor = [UIColor blackColor];
    media.contentMode = UIViewContentModeScaleAspectFit;
    media.clipsToBounds = YES;
    [media setContentHuggingPriority:UILayoutPriorityDefaultLow - 10 forAxis:UILayoutConstraintAxisVertical];
    [media setContentCompressionResistancePriority:UILayoutPriorityDefaultLow - 10 forAxis:UILayoutConstraintAxisVertical];
    [adView addSubview:media];

    UIView *panel = [[GLNAAssetOnlyView alloc] init];
    panel.translatesAutoresizingMaskIntoConstraints = NO;
    panel.backgroundColor = self.bgColor;
    panel.layer.cornerRadius = 14.0;
    if (@available(iOS 11.0, *))
        panel.layer.maskedCorners = kCALayerMinXMinYCorner | kCALayerMaxXMinYCorner;
    [adView addSubview:panel];

    UIStackView *content = GLNAMakeStack(UILayoutConstraintAxisVertical, 10.0, UIStackViewAlignmentFill);
    [panel addSubview:content];

    // Header: icon + [headline, advertiser]
    UIStackView *header = GLNAMakeStack(UILayoutConstraintAxisHorizontal, 12.0, UIStackViewAlignmentCenter);
    UIImageView *icon = [[UIImageView alloc] init];
    icon.translatesAutoresizingMaskIntoConstraints = NO;
    icon.contentMode = UIViewContentModeScaleAspectFill;
    icon.clipsToBounds = YES;
    icon.layer.cornerRadius = 10.0;
    [NSLayoutConstraint activateConstraints:@[
        [icon.widthAnchor constraintEqualToConstant:56.0],
        [icon.heightAnchor constraintEqualToConstant:56.0],
    ]];
    UIStackView *titleColumn = GLNAMakeStack(UILayoutConstraintAxisVertical, 2.0, UIStackViewAlignmentFill);
    UILabel *headline = GLNAMakeLabel(17.0, UIFontWeightBold, self.textColor, 2);
    UILabel *advertiser = GLNAMakeLabel(13.0, UIFontWeightRegular, secondary, 1);
    UILabel *rating = GLNAMakeLabel(13.0, UIFontWeightRegular, secondary, 1);
    [titleColumn addArrangedSubview:headline];
    [titleColumn addArrangedSubview:advertiser];
    [titleColumn addArrangedSubview:rating];
    [header addArrangedSubview:icon];
    [header addArrangedSubview:titleColumn];

    UILabel *body = GLNAMakeLabel(14.0, UIFontWeightRegular, self.textColor, 3);

    UIStackView *chips = GLNAMakeStack(UILayoutConstraintAxisHorizontal, 8.0, UIStackViewAlignmentCenter);
    UILabel *store = GLNAMakeLabel(12.0, UIFontWeightRegular, secondary, 1);
    UILabel *price = GLNAMakeLabel(12.0, UIFontWeightRegular, secondary, 1);
    UIView *storeChip = GLNAMakeChip(store, self.textColor);
    UIView *priceChip = GLNAMakeChip(price, self.textColor);
    UIView *chipFiller = [[GLNAAssetOnlyView alloc] init];
    chipFiller.translatesAutoresizingMaskIntoConstraints = NO;
    [chipFiller setContentHuggingPriority:UILayoutPriorityDefaultLow - 1 forAxis:UILayoutConstraintAxisHorizontal];
    [chips addArrangedSubview:storeChip];
    [chips addArrangedSubview:priceChip];
    [chips addArrangedSubview:chipFiller];

    UIButton *cta = GLNAMakeCta(17.0, self.ctaColor, self.ctaTextColor);
    [cta.heightAnchor constraintEqualToConstant:52.0].active = YES;

    [content addArrangedSubview:header];
    [content addArrangedSubview:body];
    [content addArrangedSubview:chips];
    [content addArrangedSubview:cta];

    UIView *badge = GLNAMakeAdBadge(NO);
    [adView addSubview:badge];

    [NSLayoutConstraint activateConstraints:@[
        [adView.topAnchor constraintEqualToAnchor:safe.topAnchor constant:52.0],
        [adView.leadingAnchor constraintEqualToAnchor:root.leadingAnchor],
        [adView.trailingAnchor constraintEqualToAnchor:root.trailingAnchor],
        [adView.bottomAnchor constraintEqualToAnchor:root.bottomAnchor],

        [media.topAnchor constraintEqualToAnchor:adView.topAnchor],
        [media.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor],
        [media.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor],
        [media.bottomAnchor constraintEqualToAnchor:panel.topAnchor constant:-8.0],

        [panel.leadingAnchor constraintEqualToAnchor:adView.leadingAnchor],
        [panel.trailingAnchor constraintEqualToAnchor:adView.trailingAnchor],
        [panel.bottomAnchor constraintEqualToAnchor:adView.bottomAnchor],

        [content.topAnchor constraintEqualToAnchor:panel.topAnchor constant:18.0],
        [content.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor constant:16.0],
        [content.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor constant:-16.0],
        [content.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor constant:-12.0],

        [badge.topAnchor constraintEqualToAnchor:adView.topAnchor constant:6.0],
        [badge.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor constant:6.0],
    ]];
    GLNAPlaceAdChoices(adView, adView.topAnchor, safe.trailingAnchor);

    // Bind assets; hide the views whose asset is missing.
    headline.text = ad.headline;
    headline.hidden = !GLNAHasText(ad.headline);
    advertiser.text = ad.advertiser;
    advertiser.hidden = !GLNAHasText(ad.advertiser);
    NSString *ratingText = GLNARatingText(ad);
    rating.text = ratingText;
    rating.hidden = ratingText == nil;
    body.text = ad.body;
    body.hidden = !GLNAHasText(ad.body);
    UIImage *iconImage = ad.icon.image;
    icon.image = iconImage;
    icon.hidden = iconImage == nil;
    store.text = ad.store;
    storeChip.hidden = !GLNAHasText(ad.store);
    price.text = ad.price;
    priceChip.hidden = !GLNAHasText(ad.price);
    chips.hidden = storeChip.hidden && priceChip.hidden;
    [cta setTitle:ad.callToAction forState:UIControlStateNormal];
    cta.hidden = !GLNAHasText(ad.callToAction);

    adView.headlineView = headline;
    adView.advertiserView = advertiser;
    adView.starRatingView = rating;
    adView.bodyView = body;
    adView.iconView = icon;
    adView.storeView = store;
    adView.priceView = price;
    adView.callToActionView = cta;
    adView.mediaView = media;
    media.mediaContent = ad.mediaContent;
    adView.nativeAd = ad;

    // Close row: "Close in Ns" countdown, then a round close button.
    self.countdownLabel = GLNAMakeLabel(13.0, UIFontWeightSemibold, [UIColor whiteColor], 1);
    self.countdownLabel.backgroundColor = [UIColor colorWithWhite:0.0 alpha:0.67];
    self.countdownLabel.textAlignment = NSTextAlignmentCenter;
    self.countdownLabel.layer.cornerRadius = 14.0;
    self.countdownLabel.clipsToBounds = YES;
    [root addSubview:self.countdownLabel];

    // 38pt circle in a 44pt touch square (Apple HIG minimum).
    self.closeButton = GLNAMakeRoundGlyphButton(YES, 38.0, 44.0, [UIColor colorWithWhite:0.0 alpha:0.67], [UIColor whiteColor],
                                                [UIColor colorWithWhite:1.0 alpha:0.35], NO);
    [self.closeButton addTarget:self action:@selector(closeTapped) forControlEvents:UIControlEventTouchUpInside];
    self.closeButton.accessibilityLabel = @"Close ad";
    self.closeButton.hidden = YES;
    [root addSubview:self.closeButton];

    [NSLayoutConstraint activateConstraints:@[
        [self.countdownLabel.topAnchor constraintEqualToAnchor:safe.topAnchor constant:10.0],
        [self.countdownLabel.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor constant:-12.0],
        [self.countdownLabel.heightAnchor constraintEqualToConstant:28.0],
        [self.countdownLabel.widthAnchor constraintGreaterThanOrEqualToConstant:96.0],
        // Circle stays where it was (safe top + 7, safe trailing - 12); the 44pt touch square extends 3pt around it,
        // still inside the safe area.
        [self.closeButton.topAnchor constraintEqualToAnchor:safe.topAnchor constant:4.0],
        [self.closeButton.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor constant:-9.0],
    ]];

    self.remainingSeconds = self.closeDelaySeconds > 0 ? self.closeDelaySeconds : 0;
    [self refreshCountdown];
    if (self.remainingSeconds > 0)
    {
        __weak GLNAInterstitialController *weakSelf = self;
        self.countdownTimer = [NSTimer scheduledTimerWithTimeInterval:1.0 repeats:YES block:^(NSTimer *timer) {
            GLNAInterstitialController *strongSelf = weakSelf;
            if (strongSelf == nil)
            {
                [timer invalidate];
                return;
            }
            strongSelf.remainingSeconds -= 1;
            [strongSelf refreshCountdown];
        }];
    }
}

- (void)refreshCountdown
{
    if (self.remainingSeconds <= 0)
    {
        [self stopTimer];
        self.countdownLabel.hidden = YES;
        self.closeButton.hidden = NO;
        return;
    }
    self.countdownLabel.text = [NSString stringWithFormat:@"  Close in %ds  ", self.remainingSeconds];
    self.countdownLabel.hidden = NO;
    self.closeButton.hidden = YES;
}

- (void)stopTimer
{
    [self.countdownTimer invalidate];
    self.countdownTimer = nil;
}

- (void)closeTapped
{
    if (self.closing)
        return;
    self.closing = YES;
    [self stopTimer];
    void (^onClose)(GLNAInterstitialController *) = self.onClose;
    self.onClose = nil;
    if (onClose != nil)
        onClose(self);
}

- (void)viewDidDisappear:(BOOL)animated
{
    [super viewDidDisappear:animated];
    // Only stop when really leaving (the in-app browser presented on top also triggers viewDidDisappear).
    if (self.isBeingDismissed || self.presentingViewController == nil)
        [self stopTimer];
}

- (void)dealloc
{
    [_countdownTimer invalidate];
}

@end

// ---------------------------------------------------------------------------------------------------------------
// Manager
// ---------------------------------------------------------------------------------------------------------------

@interface GLNAManager : NSObject <GADNativeAdLoaderDelegate, GADNativeAdDelegate>
@property (nonatomic, strong) NSMutableDictionary<NSString *, GLNAState *> *states;
+ (instancetype)shared;
@end

@implementation GLNAManager

+ (instancetype)shared
{
    static GLNAManager *instance = nil;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        instance = [[GLNAManager alloc] init];
    });
    return instance;
}

- (instancetype)init
{
    self = [super init];
    if (self)
        _states = [NSMutableDictionary dictionary];
    return self;
}

- (GLNAState *)stateForKey:(NSString *)key create:(BOOL)create
{
    GLNAState *state = self.states[key];
    if (state == nil && create)
    {
        state = [[GLNAState alloc] init];
        state.key = key;
        self.states[key] = state;
    }
    return state;
}

- (GLNAState *)stateForLoader:(GADAdLoader *)loader
{
    for (GLNAState *state in self.states.allValues)
    {
        if (state.loader == loader)
            return state;
    }
    return nil;
}

- (GLNAState *)stateForAd:(GADNativeAd *)ad
{
    for (GLNAState *state in self.states.allValues)
    {
        if (state.shownAd == ad || state.pendingAd == ad)
            return state;
    }
    return nil;
}

// ----- load -----

- (void)loadKey:(NSString *)key adUnitId:(NSString *)adUnitId
{
    GLNAState *state = [self stateForKey:key create:YES];
    if (state.pendingAd != nil)
    {
        GLNAEmit(key, @"loaded", @{ @"adSource" : GLNAAdSource(state.pendingAd.responseInfo) });
        return;
    }
    if (state.loader != nil)
        return; // a load for this key is already running; it reports loaded / failed

    if (adUnitId.length == 0)
    {
        GLNAEmit(key, @"failed", @{ @"error" : @"empty_ad_unit_id" });
        return;
    }

    state.adUnitId = adUnitId;

    GADNativeAdMediaAdLoaderOptions *mediaOptions = [[GADNativeAdMediaAdLoaderOptions alloc] init];
    mediaOptions.mediaAspectRatio = GADMediaAspectRatioAny;
    GADNativeAdViewAdOptions *viewOptions = [[GADNativeAdViewAdOptions alloc] init];
    viewOptions.preferredAdChoicesPosition = GADAdChoicesPositionTopRightCorner;
    GADVideoOptions *videoOptions = [[GADVideoOptions alloc] init];
    videoOptions.startMuted = YES;

    GADAdLoader *loader = [[GADAdLoader alloc] initWithAdUnitID:adUnitId
                                             rootViewController:GLNATopViewController()
                                                        adTypes:@[ GADAdLoaderAdTypeNative ]
                                                        options:@[ mediaOptions, viewOptions, videoOptions ]];
    loader.delegate = self;
    state.loader = loader;
    [loader loadRequest:[GADRequest request]];
}

- (void)adLoader:(GADAdLoader *)adLoader didReceiveNativeAd:(GADNativeAd *)nativeAd
{
    GLNAState *state = [self stateForLoader:adLoader];
    if (state == nil)
        return; // destroyed while loading
    state.loader = nil;
    state.pendingAd = nativeAd;
    nativeAd.delegate = self;

    NSString *key = [state.key copy];
    __weak GADNativeAd *weakAd = nativeAd;
    nativeAd.paidEventHandler = ^(GADAdValue *value) {
        GADNativeAd *strongAd = weakAd;
        long long micros = [[value.value decimalNumberByMultiplyingByPowerOf10:6] longLongValue];
        NSString *currency = GLNAHasText(value.currencyCode) ? value.currencyCode : @"";
        NSString *source = strongAd != nil ? GLNAAdSource(strongAd.responseInfo) : @"";
        GLNAEmit(key, @"paid", @{
            @"valueMicros" : @(micros),
            @"currency" : currency,
            @"precision" : @((NSInteger)value.precision),
            @"adSource" : source,
        });
    };

    GLNAEmit(key, @"loaded", @{ @"adSource" : GLNAAdSource(nativeAd.responseInfo) });
}

- (void)adLoader:(GADAdLoader *)adLoader didFailToReceiveAdWithError:(NSError *)error
{
    GLNAState *state = [self stateForLoader:adLoader];
    if (state == nil)
        return;
    state.loader = nil;
    NSString *message = error.localizedDescription;
    if (!GLNAHasText(message))
        message = [NSString stringWithFormat:@"error_%ld", (long)error.code];
    GLNAEmit(state.key, @"failed", @{ @"error" : message });
}

// ----- SDK ad events -----

- (void)nativeAdDidRecordImpression:(GADNativeAd *)nativeAd
{
    GLNAState *state = [self stateForAd:nativeAd];
    if (state != nil)
        GLNAEmit(state.key, @"impression", nil);
}

- (void)nativeAdDidRecordClick:(GADNativeAd *)nativeAd
{
    GLNAState *state = [self stateForAd:nativeAd];
    if (state != nil)
        GLNAEmit(state.key, @"clicked", nil);
}

// ----- teardown -----

- (void)releaseAd:(GADNativeAd *)ad
{
    if (ad == nil)
        return;
    ad.delegate = nil;
    ad.paidEventHandler = nil;
}

// Removes whatever is on screen for the key. The shown ad is spent and released. No event.
- (void)tearDownView:(GLNAState *)state
{
    [state.collapseButtonTimer invalidate];
    state.collapseButtonTimer = nil;

    if (state.adView != nil)
    {
        state.adView.nativeAd = nil;
        [state.adView removeFromSuperview];
        state.adView = nil;
    }
    state.row = nil;
    state.mediaView = nil;
    state.collapseButton = nil;
    state.heightConstraint = nil;
    state.rowHeightConstraint = nil;
    state.rowLeadingCompact = nil;
    state.rowLeadingFull = nil;
    state.rowHostLeadingFull = nil;
    state.rowHostLeadingSide = nil;

    GLNAInterstitialController *interstitial = state.interstitial;
    state.interstitial = nil;
    if (interstitial != nil)
    {
        interstitial.closing = YES; // no `closed` for hide / destroy
        interstitial.onClose = nil;
        [interstitial stopTimer];
        interstitial.adView.nativeAd = nil;
        if (interstitial.presentingViewController != nil)
            [interstitial.presentingViewController dismissViewControllerAnimated:NO completion:nil];
    }

    if (state.shownAd != nil)
    {
        [self releaseAd:state.shownAd];
        state.shownAd = nil;
    }
    state.format = GLNAShownNone;
}

- (void)hideKey:(NSString *)key
{
    GLNAState *state = [self stateForKey:key create:NO];
    if (state != nil)
        [self tearDownView:state];
}

- (void)destroyKey:(NSString *)key
{
    GLNAState *state = [self stateForKey:key create:NO];
    if (state == nil)
        return;
    [self tearDownView:state];
    if (state.loader != nil)
    {
        state.loader.delegate = nil;
        state.loader = nil;
    }
    [self releaseAd:state.pendingAd];
    state.pendingAd = nil;
    [self.states removeObjectForKey:key];
}

// Takes the loaded ad for a show. Returns nil (and emits show_failed) when there is none.
- (GADNativeAd *)takeAdForShow:(GLNAState *)state key:(NSString *)key
{
    if (state == nil || state.pendingAd == nil)
    {
        GLNAEmit(key, @"show_failed", @{ @"error" : @"no_ad_loaded" });
        return nil;
    }
    GADNativeAd *ad = state.pendingAd;
    state.pendingAd = nil;
    [self tearDownView:state]; // the previous view (and its spent ad) goes only now
    state.shownAd = ad;
    return ad;
}

// ----- banner -----

// A shown NativeCollapsible stays above the banners - native banners and GMA's standard banners, which all go on the
// root view: its view goes back to the front of its superview (front = drawn on top AND first to take touches).
// Called when a native banner shows and, from C#, when a standard banner shows; again on the next run loop turn in
// case the banner's view is added asynchronously.
- (void)raiseCollapsibles
{
    for (GLNAState *state in self.states.allValues)
    {
        UIView *view = state.adView;
        if (state.format == GLNAShownCollapsible && view.superview != nil)
            [view.superview bringSubviewToFront:view];
    }
}

- (void)raiseCollapsiblesNowAndLater
{
    [self raiseCollapsibles];
    dispatch_async(dispatch_get_main_queue(), ^{
        [self raiseCollapsibles];
    });
}

- (void)showBannerKey:(NSString *)key heightDp:(int)heightDp top:(BOOL)top
                   bg:(int)bg text:(int)text cta:(int)cta ctaText:(int)ctaText
{
    GLNAState *state = [self stateForKey:key create:NO];
    GADNativeAd *ad = nil;
    if (state != nil && state.pendingAd == nil && state.shownAd != nil && state.format == GLNAShownBanner)
    {
        ad = state.shownAd; // same ad, already visible: rebuild the layout in place
        state.shownAd = nil;
        [self tearDownView:state];
        state.shownAd = ad;
        if (![self buildBanner:state ad:ad heightDp:heightDp top:top bg:bg text:text cta:cta ctaText:ctaText])
            [self tearDownView:state];
        [self raiseCollapsibles];
        return;
    }

    ad = [self takeAdForShow:state key:key];
    if (ad == nil)
        return;
    if (![self buildBanner:state ad:ad heightDp:heightDp top:top bg:bg text:text cta:cta ctaText:ctaText])
    {
        [self tearDownView:state];
        GLNAEmit(key, @"show_failed", @{ @"error" : @"no_root_view" });
        return;
    }
    [self raiseCollapsibles];
    GLNAEmit(key, @"shown", nil);
}

- (BOOL)buildBanner:(GLNAState *)state ad:(GADNativeAd *)ad heightDp:(int)heightDp top:(BOOL)top
                 bg:(int)bg text:(int)text cta:(int)cta ctaText:(int)ctaText
{
    UIView *host = GLNARootViewController().view;
    if (host == nil)
        return NO;

    CGFloat height = heightDp > 0 ? (CGFloat)heightDp : 100.0;

    GADNativeAdView *adView = [[GLNANativeAdView alloc] init];
    adView.translatesAutoresizingMaskIntoConstraints = NO;
    adView.backgroundColor = GLNAColor(bg);
    adView.clipsToBounds = YES;

    GLNARow *row = GLNAMakeRow(height, GLNAColor(text), GLNAColor(cta), GLNAColor(ctaText), YES);
    [adView addSubview:row.stack];
    UIView *badge = GLNAMakeAdBadge(NO);
    badge.hidden = row.oneLine; // one-line bars carry the badge inline, after the headline
    [adView addSubview:badge];

    [host addSubview:adView];
    GLNAAddTouchSink(adView);
    UILayoutGuide *safe = host.safeAreaLayoutGuide;
    // `content`: the bar's `height` tall content area, between the safe area's left and right. At the bottom the whole
    // bar sits on the physical bottom edge (over the home indicator area; the indicator auto-hides while the bar is up),
    // so the game keeps the most room, with the background across the full screen width. At the top the bar keeps to
    // the safe area.
    UILayoutGuide *content = [[UILayoutGuide alloc] init];
    [adView addLayoutGuide:content];
    GLNAInsetSides(content, host);
    NSLayoutConstraint *heightConstraint = [content.heightAnchor constraintEqualToConstant:height];
    NSArray<NSLayoutConstraint *> *placement = top
        ? @[ [adView.topAnchor constraintEqualToAnchor:safe.topAnchor],
             [adView.bottomAnchor constraintEqualToAnchor:content.bottomAnchor],
             [adView.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor],
             [adView.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor],
             [content.topAnchor constraintEqualToAnchor:adView.topAnchor] ]
        : @[ [adView.bottomAnchor constraintEqualToAnchor:host.bottomAnchor],
             [adView.topAnchor constraintEqualToAnchor:content.topAnchor],
             [adView.leadingAnchor constraintEqualToAnchor:host.leadingAnchor],
             [adView.trailingAnchor constraintEqualToAnchor:host.trailingAnchor],
             [content.bottomAnchor constraintEqualToAnchor:host.bottomAnchor] ];
    [NSLayoutConstraint activateConstraints:placement];
    [NSLayoutConstraint activateConstraints:@[
        heightConstraint,

        // The row starts right of the AD badge so the badge never covers the icon / text (one-line: no corner badge).
        row.oneLine ? [row.stack.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:8.0]
                    : [row.stack.leadingAnchor constraintEqualToAnchor:badge.trailingAnchor constant:GLNABadgeGap],
        [row.stack.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-8.0],
        [row.stack.topAnchor constraintGreaterThanOrEqualToAnchor:content.topAnchor constant:4.0],
        [row.stack.bottomAnchor constraintLessThanOrEqualToAnchor:content.bottomAnchor constant:-4.0],
        [row.stack.centerYAnchor constraintEqualToAnchor:content.centerYAnchor],

        [badge.topAnchor constraintEqualToAnchor:content.topAnchor constant:GLNABadgeInset],
        [badge.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:GLNABadgeInset],
    ]];

    GLNAFillRow(row, ad, NO, NO);
    GLNARegisterRow(adView, row);
    ad.rootViewController = GLNATopViewController();
    adView.nativeAd = ad;
    [host bringSubviewToFront:adView];
    if (row.oneLine)
    {
        [host layoutIfNeeded]; // the text line needs its final width
        GLNAFitOneLine(row);
    }

    state.adView = adView;
    state.row = row;
    state.heightConstraint = heightConstraint;
    state.format = GLNAShownBanner;
    return YES;
}

// ----- collapsible -----

// The media view of the expanded collapsible must be >= GLNAMinMediaSide tall (AdMob's video minimum, 120x120pt).
static const CGFloat GLNAMinMediaSide = 120.0;

// Collapse button: its circle is GLNACollapseButtonTop from the ad's top edge AND from its right edge; the 44pt touch
// square extends left and down from there, so neither covers the AdChoices icon in the ad's top-right corner.
static const CGFloat GLNACollapseButtonTop = 20.0;
static const CGFloat GLNACollapseButtonTouch = 44.0;

// Portrait: media above the row, so the ad is at least row + 120pt. Landscape: media column left of the row, the
// full ad height, so the ad is at least 120pt.
- (CGFloat)expandedHeightFor:(CGFloat)collapsed screenHeight:(CGFloat)screenHeight landscape:(BOOL)landscape
{
    CGFloat height = MAX(collapsed, floor(screenHeight * 0.45));
    return MAX(height, landscape ? GLNAMinMediaSide : MAX(collapsed, 76.0) + GLNAMinMediaSide);
}

// Expanded portrait: the row under the media. (Expanded landscape uses the info column, GLNAEnterSide.)
- (CGFloat)expandedRowHeightFor:(CGFloat)collapsed expanded:(CGFloat)expanded
{
    return MIN(MAX(collapsed, 76.0), expanded);
}

// Row inside the collapsible's rowHost: right of the AD badge (collapsed) or across the host (expanded portrait).
static void GLNAPinRow(GLNAState *state, BOOL full)
{
    GLNARow *row = state.row;
    UIView *rowHost = state.rowHost;
    state.rowLeadingCompact = [row.stack.leadingAnchor constraintEqualToAnchor:state.cornerBadge.trailingAnchor constant:GLNABadgeGap];
    state.rowLeadingFull = [row.stack.leadingAnchor constraintEqualToAnchor:rowHost.leadingAnchor constant:8.0];
    [NSLayoutConstraint activateConstraints:@[
        full ? state.rowLeadingFull : state.rowLeadingCompact,
        [row.stack.trailingAnchor constraintEqualToAnchor:rowHost.trailingAnchor constant:-8.0],
        [row.stack.topAnchor constraintGreaterThanOrEqualToAnchor:rowHost.topAnchor constant:4.0],
        [row.stack.bottomAnchor constraintLessThanOrEqualToAnchor:rowHost.bottomAnchor constant:-4.0],
        [row.stack.centerYAnchor constraintEqualToAnchor:rowHost.centerYAnchor],
    ]];
}

// Collapsible expanded in landscape, right of the media: an info column like an app-store card, centred vertically
// with 12pt padding - header (icon 44 | headline 15 bold, meta 11), body 12 on up to 2 lines (8pt below), then the
// CTA across the column (40pt, 10pt below). The header stops 68pt from the ad's right edge, clear of the collapse
// button's touch target. The body and the CTA move out of the row into the column; GLNALeaveSide puts them back.
// Same layout as Android's layoutSide.
static void GLNAEnterSide(GLNAState *state, CGFloat adHeight)
{
    GLNARow *row = state.row;
    const CGFloat pad = 12.0, icon = 44.0, bodyGap = 8.0, ctaGap = 10.0, ctaHeight = 40.0;

    UIView *header = [[GLNAAssetOnlyView alloc] init];
    header.translatesAutoresizingMaskIntoConstraints = NO;
    [header addSubview:row.stack]; // drops the row's constraints in the host
    [row.stack removeArrangedSubview:row.cta];
    [row.cta removeFromSuperview];
    [row.column removeArrangedSubview:row.body];
    [row.body removeFromSuperview];

    row.iconSize.constant = icon;
    row.headline.font = [UIFont systemFontOfSize:15.0 weight:UIFontWeightBold];
    UIFont *meta = [UIFont systemFontOfSize:11.0 weight:UIFontWeightRegular];
    for (UILabel *label in @[ row.rating, row.advertiser, row.store, row.price ])
        label.font = meta;
    row.body.font = [UIFont systemFontOfSize:12.0 weight:UIFontWeightRegular];
    row.cta.titleLabel.font = [UIFont systemFontOfSize:14.0 weight:UIFontWeightSemibold];
    row.ctaWidth.active = NO;
    // across the column: its required hugging would otherwise shrink the whole content to the button's text
    [row.cta setContentHuggingPriority:UILayoutPriorityDefaultLow forAxis:UILayoutConstraintAxisHorizontal];
    row.ctaHeight.constant = ctaHeight;

    // the body gets what the header and the button leave: 2 lines, 1, or none
    CGFloat room = adHeight - 2.0 * pad - icon - ctaGap - ctaHeight - bodyGap;
    NSInteger lines = MIN(2, (NSInteger)floor(room / row.body.font.lineHeight));
    row.body.numberOfLines = MAX(lines, 1);
    if (lines <= 0)
        row.body.hidden = YES;

    UIStackView *side = GLNAMakeStack(UILayoutConstraintAxisVertical, 0.0, UIStackViewAlignmentFill);
    [side addArrangedSubview:header];
    [side addArrangedSubview:row.body];
    [side addArrangedSubview:row.cta];
    [side setCustomSpacing:bodyGap afterView:header];
    [side setCustomSpacing:ctaGap afterView:row.body];
    [state.rowHost addSubview:side];
    UIView *rowHost = state.rowHost;
    [NSLayoutConstraint activateConstraints:@[
        [row.stack.topAnchor constraintEqualToAnchor:header.topAnchor],
        [row.stack.bottomAnchor constraintEqualToAnchor:header.bottomAnchor],
        [row.stack.leadingAnchor constraintEqualToAnchor:header.leadingAnchor],
        // rowHost ends at the content's right edge, where the collapse button is measured from
        [row.stack.trailingAnchor constraintEqualToAnchor:rowHost.trailingAnchor
                                                 constant:-(GLNACollapseButtonTop + GLNACollapseButtonTouch + 4.0)],
        [side.leadingAnchor constraintEqualToAnchor:rowHost.leadingAnchor constant:pad],
        [side.trailingAnchor constraintEqualToAnchor:rowHost.trailingAnchor constant:-pad],
        [side.centerYAnchor constraintEqualToAnchor:rowHost.centerYAnchor],
        [side.topAnchor constraintGreaterThanOrEqualToAnchor:rowHost.topAnchor constant:pad],
        [side.bottomAnchor constraintLessThanOrEqualToAnchor:rowHost.bottomAnchor constant:-pad],
    ]];
    state.side = side;
}

// Back to the bar: body into the text column (after the headline), CTA at the row's end. The caller re-sizes the row
// for the bar (GLNASizeRow).
static void GLNALeaveSide(GLNAState *state)
{
    if (state.side == nil)
        return;
    GLNARow *row = state.row;
    UIView *header = row.stack.superview;
    [state.side removeArrangedSubview:row.body];
    [state.side removeArrangedSubview:row.cta];
    [row.column insertArrangedSubview:row.body atIndex:1];
    [row.stack addArrangedSubview:row.cta];
    [state.rowHost addSubview:row.stack];
    [header removeFromSuperview];
    [state.side removeFromSuperview];
    state.side = nil;

    row.ctaWidth.active = YES;
    [row.cta setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    GLNAPinRow(state, NO);
}

- (void)showCollapsibleKey:(NSString *)key collapsedDp:(int)collapsedDp startExpanded:(BOOL)startExpanded
        buttonDelaySeconds:(int)buttonDelaySeconds bg:(int)bg text:(int)text cta:(int)cta ctaText:(int)ctaText
{
    GLNAState *state = [self stateForKey:key create:NO];
    BOOL relayout = state != nil && state.pendingAd == nil && state.shownAd != nil && state.format == GLNAShownCollapsible;
    GADNativeAd *ad = nil;
    if (relayout)
    {
        ad = state.shownAd; // same ad, already visible: rebuild the layout in place
        state.shownAd = nil;
        [self tearDownView:state];
        state.shownAd = ad;
    }
    else
    {
        ad = [self takeAdForShow:state key:key];
        if (ad == nil)
            return;
    }

    if (![self buildCollapsible:state ad:ad collapsedDp:collapsedDp expanded:startExpanded
             buttonDelaySeconds:buttonDelaySeconds bg:bg text:text cta:cta ctaText:ctaText])
    {
        [self tearDownView:state];
        if (!relayout)
            GLNAEmit(key, @"show_failed", @{ @"error" : @"no_root_view" });
        return;
    }
    if (!relayout)
        GLNAEmit(key, @"shown", nil);
    GLNAEmit(key, startExpanded ? @"expanded" : @"collapsed", nil);
}

- (BOOL)buildCollapsible:(GLNAState *)state ad:(GADNativeAd *)ad collapsedDp:(int)collapsedDp expanded:(BOOL)expanded
      buttonDelaySeconds:(int)buttonDelaySeconds bg:(int)bg text:(int)text cta:(int)cta ctaText:(int)ctaText
{
    UIView *host = GLNARootViewController().view;
    if (host == nil)
        return NO;

    CGFloat collapsed = collapsedDp > 0 ? (CGFloat)collapsedDp : 50.0;
    // The game's orientation is fixed: decide the expanded layout once, from the host's shape.
    CGSize hostSize = host.bounds.size;
    if (hostSize.width <= 0 || hostSize.height <= 0)
        hostSize = [UIScreen mainScreen].bounds.size;
    BOOL landscape = hostSize.width > hostSize.height;
    CGFloat expandedHeight = [self expandedHeightFor:collapsed screenHeight:hostSize.height landscape:landscape];
    CGFloat expandedRow = [self expandedRowHeightFor:collapsed expanded:expandedHeight];
    // landscape media column: 16:9 of the ad height, at most half the width
    CGFloat mediaWidth = MAX(GLNAMinMediaSide, MIN(floor(expandedHeight * 16.0 / 9.0), floor(hostSize.width / 2.0)));

    GADNativeAdView *adView = [[GLNANativeAdView alloc] init];
    adView.translatesAutoresizingMaskIntoConstraints = NO;
    adView.backgroundColor = GLNAColor(bg);
    adView.clipsToBounds = YES;

    GADMediaView *media = [[GADMediaView alloc] init];
    media.translatesAutoresizingMaskIntoConstraints = NO;
    media.backgroundColor = [UIColor colorWithWhite:0.08 alpha:1.0];
    media.contentMode = UIViewContentModeScaleAspectFit;
    media.clipsToBounds = YES;
    [adView addSubview:media];

    UIView *rowHost = [[GLNAAssetOnlyView alloc] init];
    rowHost.translatesAutoresizingMaskIntoConstraints = NO;
    rowHost.backgroundColor = [UIColor clearColor];
    [adView addSubview:rowHost];

    GLNARow *row = GLNAMakeRow(collapsed, GLNAColor(text), GLNAColor(cta), GLNAColor(ctaText), NO);
    [rowHost addSubview:row.stack];

    UIView *badge = GLNAMakeAdBadge(NO);
    [adView addSubview:badge];

    // Collapse button: 28pt circle with a down chevron in a 44x44pt touch square (Apple HIG), inside the ad over its
    // top-right corner but below the AdChoices icon, which owns the corner itself and must not be covered.
    UIColor *buttonBg = [UIColor colorWithWhite:0.0 alpha:0.55];
    UIButton *collapseButton = GLNAMakeRoundGlyphButton(NO, 28.0, 44.0, buttonBg, [UIColor whiteColor], nil, YES);
    [collapseButton addTarget:self action:@selector(collapseTapped:) forControlEvents:UIControlEventTouchUpInside];
    collapseButton.accessibilityLabel = @"Collapse ad";
    collapseButton.hidden = YES;
    [adView addSubview:collapseButton];

    [host addSubview:adView];
    GLNAAddTouchSink(adView);
    UILayoutGuide *safe = host.safeAreaLayoutGuide;
    // `content`: the ad's content area, inside the safe area. The background reaches the physical bottom edge and the
    // full screen width (under the home indicator, which auto-hides while the ad is up); media, row and buttons stay
    // inside the safe area. The ad grows by the bottom inset, so the expanded media keeps its full height (>= 120pt).
    UILayoutGuide *content = [[UILayoutGuide alloc] init];
    [adView addLayoutGuide:content];
    GLNAInsetSides(content, host);
    NSLayoutConstraint *heightConstraint = [content.heightAnchor constraintEqualToConstant:expanded ? expandedHeight : collapsed];
    BOOL side = landscape && expanded;
    // Expanded in landscape the host is the info column right of the media, the ad's full height.
    NSLayoutConstraint *rowHeight = [rowHost.heightAnchor constraintEqualToConstant:side ? expandedHeight : expanded ? expandedRow : collapsed];
    // The row spans the ad, except expanded in landscape where it sits right of the media column.
    NSLayoutConstraint *rowHostFull = [rowHost.leadingAnchor constraintEqualToAnchor:content.leadingAnchor];
    NSLayoutConstraint *rowHostSide = [rowHost.leadingAnchor constraintEqualToAnchor:media.trailingAnchor];
    NSArray<NSLayoutConstraint *> *mediaShape = landscape
        ? @[ [media.widthAnchor constraintEqualToConstant:mediaWidth],
             [media.bottomAnchor constraintEqualToAnchor:content.bottomAnchor] ]
        : @[ [media.trailingAnchor constraintEqualToAnchor:content.trailingAnchor],
             [media.bottomAnchor constraintEqualToAnchor:rowHost.topAnchor] ];
    [NSLayoutConstraint activateConstraints:mediaShape];
    [NSLayoutConstraint activateConstraints:@[
        [adView.bottomAnchor constraintEqualToAnchor:host.bottomAnchor],
        [adView.leadingAnchor constraintEqualToAnchor:host.leadingAnchor],
        [adView.trailingAnchor constraintEqualToAnchor:host.trailingAnchor],
        [adView.topAnchor constraintEqualToAnchor:content.topAnchor],
        [content.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor],
        heightConstraint,

        side ? rowHostSide : rowHostFull,
        [rowHost.trailingAnchor constraintEqualToAnchor:content.trailingAnchor],
        [rowHost.bottomAnchor constraintEqualToAnchor:content.bottomAnchor],
        rowHeight,

        [media.topAnchor constraintEqualToAnchor:content.topAnchor],
        [media.leadingAnchor constraintEqualToAnchor:content.leadingAnchor],

        [badge.topAnchor constraintEqualToAnchor:content.topAnchor constant:GLNABadgeInset],
        [badge.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:GLNABadgeInset],

        // Circle as far from the top edge as from the right edge (of the content area) - same spot as on Android.
        [collapseButton.topAnchor constraintEqualToAnchor:content.topAnchor constant:GLNACollapseButtonTop],
        [collapseButton.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-GLNACollapseButtonTop],
    ]];

    // expanded: in the content's corner; collapsed (a bar at the bottom): the ad view's corner, clear of the CTA
    state.adChoicesTrailing = GLNAPlaceAdChoices(adView, content.topAnchor,
                                                 expanded ? content.trailingAnchor : adView.trailingAnchor);
    state.adView = adView;
    state.row = row;
    state.rowHost = rowHost;
    state.cornerBadge = badge;
    GLNAFillRow(row, ad, YES, YES);
    if (!expanded)
        GLNASetRowCompact(row, ad, YES);
    // Collapsed, the row starts right of the AD badge (no asset under it); expanded, the badge sits over the media.
    if (side)
    {
        GLNAEnterSide(state, expandedHeight);
    }
    else
    {
        if (expanded)
            GLNASizeRow(row, expandedRow); // the 76pt row under the media: Android's sizes for that height
        GLNAPinRow(state, expanded);
    }
    media.hidden = !expanded;

    GLNARegisterRow(adView, row);
    // The media view is registered only while expanded (>= 120x120pt). Collapsed it would be a hidden, too small
    // media view, which AdMob's validator flags ("MediaView is too small for video").
    if (expanded)
    {
        media.mediaContent = ad.mediaContent;
        adView.mediaView = media;
    }
    else
    {
        adView.mediaView = nil;
    }
    ad.rootViewController = GLNATopViewController();
    adView.nativeAd = ad;
    [host bringSubviewToFront:adView];

    state.adView = adView;
    state.row = row;
    state.mediaView = media;
    state.collapseButton = collapseButton;
    state.heightConstraint = heightConstraint;
    state.rowHeightConstraint = rowHeight;
    state.rowHostLeadingFull = rowHostFull;
    state.rowHostLeadingSide = rowHostSide;
    state.collapsedHeight = collapsed;
    state.expanded = expanded;
    state.format = GLNAShownCollapsible;

    if (expanded)
    {
        if (buttonDelaySeconds <= 0)
        {
            collapseButton.hidden = NO;
        }
        else
        {
            __weak UIButton *weakButton = collapseButton;
            __weak GLNAState *weakState = state;
            state.collapseButtonTimer = [NSTimer scheduledTimerWithTimeInterval:(NSTimeInterval)buttonDelaySeconds
                                                                        repeats:NO
                                                                          block:^(NSTimer *timer) {
                GLNAState *strongState = weakState;
                if (strongState != nil && strongState.collapseButtonTimer == timer)
                    strongState.collapseButtonTimer = nil;
                UIButton *button = weakButton;
                if (button != nil && strongState != nil && strongState.expanded && strongState.collapseButton == button)
                    button.hidden = NO;
            }];
        }
    }
    return YES;
}

- (void)collapseTapped:(UIButton *)sender
{
    GLNAState *state = nil;
    for (GLNAState *candidate in self.states.allValues)
    {
        if (candidate.collapseButton == sender)
        {
            state = candidate;
            break;
        }
    }
    if (state == nil || state.format != GLNAShownCollapsible || !state.expanded || state.collapseButton != sender)
        return;

    state.expanded = NO;
    [state.collapseButtonTimer invalidate];
    state.collapseButtonTimer = nil;
    sender.hidden = YES;

    UIView *host = state.adView.superview;
    GADMediaView *media = state.mediaView;
    GLNARow *row = state.row;
    GADNativeAd *ad = state.shownAd;

    // Unregister the media view before it shrinks away, then re-assign the ad so the SDK re-reads the outlets.
    GADNativeAdView *adView = state.adView;
    adView.mediaView = nil;
    if (ad != nil)
        adView.nativeAd = ad;

    state.heightConstraint.constant = state.collapsedHeight;
    state.rowHeightConstraint.constant = state.collapsedHeight;
    if (state.side != nil)
    {
        GLNALeaveSide(state); // pins the row compact
    }
    else
    {
        state.rowLeadingFull.active = NO;
        state.rowLeadingCompact.active = YES;
    }
    if (state.adChoicesTrailing != nil && adView.adChoicesView != nil)
    {
        state.adChoicesTrailing.active = NO;
        state.adChoicesTrailing = [adView.adChoicesView.trailingAnchor constraintEqualToAnchor:adView.trailingAnchor];
        state.adChoicesTrailing.active = YES;
    }
    GLNASizeRow(row, state.collapsedHeight); // back to the bar's sizes (fonts, icon, CTA, which lines fit)
    state.rowHostLeadingSide.active = NO; // deactivate before activating: never two leading constraints at once
    state.rowHostLeadingFull.active = YES;
    [UIView animateWithDuration:0.25
        animations:^{
            GLNASetRowCompact(row, ad, YES);
            media.alpha = 0.0;
            [host layoutIfNeeded];
        }
        completion:^(BOOL finished) {
            media.hidden = YES;
            media.alpha = 1.0;
        }];

    GLNAEmit(state.key, @"collapsed", nil);
}

// ----- interstitial -----

- (void)showInterstitialKey:(NSString *)key closeDelaySeconds:(int)closeDelaySeconds
                         bg:(int)bg text:(int)text cta:(int)cta ctaText:(int)ctaText
{
    GLNAState *state = [self stateForKey:key create:NO];
    if (state != nil && state.format == GLNAShownInterstitial && state.interstitial != nil && state.pendingAd == nil)
        return; // already on screen with this ad

    UIViewController *top = GLNATopViewController();
    if (top == nil)
    {
        GLNAEmit(key, @"show_failed", @{ @"error" : (state != nil && state.pendingAd != nil) ? @"no_view_controller" : @"no_ad_loaded" });
        return;
    }

    GADNativeAd *ad = [self takeAdForShow:state key:key];
    if (ad == nil)
        return;
    // `top` may have been our own interstitial that takeAdForShow just dismissed.
    top = GLNATopViewController();
    if (top == nil)
    {
        [self tearDownView:state];
        GLNAEmit(key, @"show_failed", @{ @"error" : @"no_view_controller" });
        return;
    }

    GLNAInterstitialController *controller = [[GLNAInterstitialController alloc] init];
    controller.key = key;
    controller.nativeAd = ad;
    controller.closeDelaySeconds = closeDelaySeconds;
    controller.bgColor = GLNAColor(bg);
    controller.textColor = GLNAColor(text);
    controller.ctaColor = GLNAColor(cta);
    controller.ctaTextColor = GLNAColor(ctaText);
    controller.modalPresentationStyle = UIModalPresentationFullScreen;
    controller.modalPresentationCapturesStatusBarAppearance = YES;
    controller.modalTransitionStyle = UIModalTransitionStyleCrossDissolve;

    __weak GLNAManager *weakSelf = self;
    controller.onClose = ^(GLNAInterstitialController *closing) {
        [weakSelf closeInterstitial:closing];
    };

    state.interstitial = controller;
    state.format = GLNAShownInterstitial;
    ad.rootViewController = controller;

    [top presentViewController:controller animated:YES completion:^{
        GLNAEmit(key, @"shown", nil);
    }];
}

- (void)closeInterstitial:(GLNAInterstitialController *)controller
{
    NSString *key = controller.key;
    GLNAState *state = [self stateForKey:key create:NO];
    if (state != nil && state.interstitial == controller)
    {
        state.interstitial = nil;
        if (state.shownAd != nil)
        {
            [self releaseAd:state.shownAd];
            state.shownAd = nil;
        }
        state.format = GLNAShownNone;
    }

    UIViewController *presenting = controller.presentingViewController;
    if (presenting != nil)
    {
        [presenting dismissViewControllerAnimated:YES completion:^{
            controller.adView.nativeAd = nil;
            GLNAEmit(key, @"closed", nil);
        }];
    }
    else
    {
        controller.adView.nativeAd = nil;
        GLNAEmit(key, @"closed", nil);
    }
}

@end

#endif // GLNA_HAS_GMA

// ---------------------------------------------------------------------------------------------------------------
// C API (P/Invoke from C#). C strings are copied before any dispatch to the main queue.
// ---------------------------------------------------------------------------------------------------------------

static void GLNAOnMain(dispatch_block_t block)
{
    if ([NSThread isMainThread])
        block();
    else
        dispatch_async(dispatch_get_main_queue(), block);
}

extern "C" {

void GameLibNativeAds_Initialize(GameLibNativeAdsCallback callback)
{
    gGLNACallback = callback;
}

void GameLibNativeAds_Load(const char *key, const char *adUnitId)
{
    NSString *keyCopy = GLNACopyString(key);
    NSString *unitCopy = GLNACopyString(adUnitId);
#if GLNA_HAS_GMA
    GLNAOnMain(^{
        [[GLNAManager shared] loadKey:keyCopy adUnitId:unitCopy];
    });
#else
    (void)unitCopy;
    GLNAOnMain(^{
        GLNAEmit(keyCopy, @"failed", @{ @"error" : @"sdk_missing" });
    });
#endif
}

void GameLibNativeAds_ShowBanner(const char *key, int heightDp, bool top, int bg, int text, int cta, int ctaText)
{
#if GLNA_HAS_GMA
    NSString *keyCopy = GLNACopyString(key);
    BOOL topCopy = top ? YES : NO;
    GLNAOnMain(^{
        [[GLNAManager shared] showBannerKey:keyCopy heightDp:heightDp top:topCopy bg:bg text:text cta:cta ctaText:ctaText];
    });
#else
    (void)key; (void)heightDp; (void)top; (void)bg; (void)text; (void)cta; (void)ctaText;
#endif
}

void GameLibNativeAds_ShowCollapsible(const char *key, int collapsedDp, bool startExpanded, int buttonDelaySeconds,
                                      int bg, int text, int cta, int ctaText)
{
#if GLNA_HAS_GMA
    NSString *keyCopy = GLNACopyString(key);
    BOOL expandedCopy = startExpanded ? YES : NO;
    GLNAOnMain(^{
        [[GLNAManager shared] showCollapsibleKey:keyCopy collapsedDp:collapsedDp startExpanded:expandedCopy
                              buttonDelaySeconds:buttonDelaySeconds bg:bg text:text cta:cta ctaText:ctaText];
    });
#else
    (void)key; (void)collapsedDp; (void)startExpanded; (void)buttonDelaySeconds;
    (void)bg; (void)text; (void)cta; (void)ctaText;
#endif
}

void GameLibNativeAds_ShowInterstitial(const char *key, int closeDelaySeconds, int bg, int text, int cta, int ctaText)
{
#if GLNA_HAS_GMA
    NSString *keyCopy = GLNACopyString(key);
    GLNAOnMain(^{
        [[GLNAManager shared] showInterstitialKey:keyCopy closeDelaySeconds:closeDelaySeconds
                                               bg:bg text:text cta:cta ctaText:ctaText];
    });
#else
    (void)key; (void)closeDelaySeconds; (void)bg; (void)text; (void)cta; (void)ctaText;
#endif
}

void GameLibNativeAds_Hide(const char *key)
{
#if GLNA_HAS_GMA
    NSString *keyCopy = GLNACopyString(key);
    GLNAOnMain(^{
        [[GLNAManager shared] hideKey:keyCopy];
    });
#else
    (void)key;
#endif
}

void GameLibNativeAds_Destroy(const char *key)
{
#if GLNA_HAS_GMA
    NSString *keyCopy = GLNACopyString(key);
    GLNAOnMain(^{
        [[GLNAManager shared] destroyKey:keyCopy];
    });
#else
    (void)key;
#endif
}

void GameLibNativeAds_RaiseCollapsibles(void)
{
#if GLNA_HAS_GMA
    GLNAOnMain(^{
        [[GLNAManager shared] raiseCollapsiblesNowAndLater];
    });
#endif
}

} // extern "C"
