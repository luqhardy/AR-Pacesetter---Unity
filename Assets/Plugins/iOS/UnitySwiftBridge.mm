// UnitySwiftBridge.mm
// Unity → Swift 送信ブリッジ (Unity as a Library)
//
// C#側: SwiftMessageSender.cs から DllImport("__Internal") で呼ばれる。
// Swift側: UnityBridge.swift が NSNotification "UnityToSwiftMessage" を購読し、
//          userInfo["json"] を onUnityMessage(_:) に渡す。

#import <Foundation/Foundation.h>

extern "C" {

void UnitySendMessageToSwift(const char* json)
{
    if (json == NULL) return;

    // 不正なUTF-8だと nil が返る。nil を @{...} へ入れると例外でアプリごと落ちるため捨てる
    NSString *message = [NSString stringWithUTF8String:json];
    if (message == nil) {
        NSLog(@"[UnitySwiftBridge] Dropped a Unity→Swift message that was not valid UTF-8");
        return;
    }
    dispatch_async(dispatch_get_main_queue(), ^{
        [[NSNotificationCenter defaultCenter]
            postNotificationName:@"UnityToSwiftMessage"
                          object:nil
                        userInfo:@{@"json": message}];
    });
}

}
