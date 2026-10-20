#import <AVFoundation/AVFoundation.h>
#import <Foundation/Foundation.h>

// Called on a launcher worker thread, never the AppKit event thread. No audio is captured.
__attribute__((visibility("default"))) int vortex_request_microphone(void) {
    @autoreleasepool {
        AVAuthorizationStatus status = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio];
        if (status == AVAuthorizationStatusNotDetermined) {
            dispatch_semaphore_t done = dispatch_semaphore_create(0);
            [AVCaptureDevice requestAccessForMediaType:AVMediaTypeAudio completionHandler:^(BOOL granted) {
                dispatch_semaphore_signal(done);
            }];
            if (dispatch_semaphore_wait(done, dispatch_time(DISPATCH_TIME_NOW, 120 * NSEC_PER_SEC)) != 0) return 4;
            status = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio];
        }
        return status == AVAuthorizationStatusAuthorized ? 0 : status == AVAuthorizationStatusDenied ? 1 : 2;
    }
}
