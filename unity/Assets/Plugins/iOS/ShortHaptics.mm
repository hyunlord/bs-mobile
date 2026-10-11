#import <UIKit/UIKit.h>
#import <dispatch/dispatch.h>

static UIImpactFeedbackGenerator *SowSiegeImpactGenerator()
{
    static UIImpactFeedbackGenerator *generator;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
    });
    return generator;
}

extern "C" void SowSiegePrepareShortHaptic()
{
    dispatch_async(dispatch_get_main_queue(), ^{
        [SowSiegeImpactGenerator() prepare];
    });
}

extern "C" void SowSiegePlayShortHaptic()
{
    dispatch_async(dispatch_get_main_queue(), ^{
        [SowSiegeImpactGenerator() impactOccurred];
        [SowSiegeImpactGenerator() prepare];
    });
}
