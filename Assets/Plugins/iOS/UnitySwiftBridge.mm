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

    NSString *message = [NSString stringWithUTF8String:json];
    dispatch_async(dispatch_get_main_queue(), ^{
        [[NSNotificationCenter defaultCenter]
            postNotificationName:@"UnityToSwiftMessage"
                          object:nil
                        userInfo:@{@"json": message}];
    });
}

}
