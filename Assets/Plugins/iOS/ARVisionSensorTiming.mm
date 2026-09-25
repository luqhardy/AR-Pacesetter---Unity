// ARVisionSensorTiming.mm
// 第1期PoCの計測基盤 — Motion-to-Photon の実測と 100Hz IMU の供給
//
// C#側: MotionToPhotonProbe.cs / RunTelemetryLogger.cs から DllImport("__Internal") で呼ぶ。
//
// ■ なぜネイティブが要るのか
//   1) M2P: 「提示予定時刻」は CADisplayLink.targetTimestamp にしか無い。
//      Unity の Time.* は別クロックなので、ARKitフレームのタイムスタンプ
//      (CACurrentMediaTime 軸) と引き算できない。
//   2) 100Hz IMU: Unity の Input.gyro は「読む」のが Update(=60Hz)のため、
//      CoreMotion を 100Hz に設定しても実際に取れるのは 60Hz 相当になる。
//      CMMotionManager のコールバックで**取りこぼさずリングバッファへ貯め**、
//      Unity 側が毎フレームまとめて引き取ることで、真に 100Hz のサンプルが残る。
//
// ■ 時間軸
//   CMDeviceMotion.timestamp / ARFrame.timestamp / CADisplayLink.targetTimestamp /
//   CACurrentMediaTime() はすべて mach_absolute_time 由来の同一時間軸(秒)。
//   この前提が崩れると M2P の引き算が無意味になるため、混ぜないこと。

#import <Foundation/Foundation.h>
#import <QuartzCore/QuartzCore.h>
#import <CoreMotion/CoreMotion.h>
#import <os/lock.h>

static const int kImuRingCapacity = 512; // 100Hz なら約5秒ぶん。フレーム落ちに耐える

@interface ARVSensorTiming : NSObject
+ (instancetype)shared;
- (void)start;
- (void)stop;
- (double)targetTimestamp;
- (int)drainSamples:(double *)outTimestamps xyz:(float *)outXyz max:(int)maxSamples;
- (BOOL)isImuStreaming;
@end

@implementation ARVSensorTiming {
    CADisplayLink   *_displayLink;
    CMMotionManager *_motion;
    NSOperationQueue *_motionQueue;

    // 提示予定時刻。CADisplayLink のコールバック中しか読めないので毎tick保存する
    double _targetTimestamp;

    // IMU リングバッファ (CoreMotion のキュー → Unity メインスレッド)
    double _ringTime[kImuRingCapacity];
    float  _ringXyz[kImuRingCapacity * 3];
    int    _ringHead;     // 次に書く位置
    int    _ringCount;    // 未回収のサンプル数
    long   _droppedCount; // 回収が追いつかず捨てた数(診断用)
    os_unfair_lock _ringLock;

    BOOL _running;
}

+ (instancetype)shared {
    static ARVSensorTiming *instance = nil;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ instance = [[ARVSensorTiming alloc] init]; });
    return instance;
}

- (instancetype)init {
    self = [super init];
    if (self) {
        _ringLock = OS_UNFAIR_LOCK_INIT;
        _targetTimestamp = 0.0;
        _ringHead = 0;
        _ringCount = 0;
        _droppedCount = 0;
        _running = NO;
    }
    return self;
}

- (void)start {
    if (_running) return;
    _running = YES;

    // ── 提示予定時刻 ──────────────────────────────────────────────────
    dispatch_async(dispatch_get_main_queue(), ^{
        if (self->_displayLink != nil) return;
        self->_displayLink = [CADisplayLink displayLinkWithTarget:self
                                                        selector:@selector(onDisplayTick:)];
        [self->_displayLink addToRunLoop:[NSRunLoop mainRunLoop]
                                 forMode:NSRunLoopCommonModes];
    });

    // ── 100Hz IMU ────────────────────────────────────────────────────
    if (_motion == nil) {
        _motion = [[CMMotionManager alloc] init];
        _motionQueue = [[NSOperationQueue alloc] init];
        _motionQueue.maxConcurrentOperationCount = 1;
        _motionQueue.name = @"jp.arvision.imu";
    }

    if (_motion.isDeviceMotionAvailable && !_motion.isDeviceMotionActive) {
        _motion.deviceMotionUpdateInterval = 0.01; // 100Hz (§5.2)
        __weak ARVSensorTiming *weakSelf = self;
        [_motion startDeviceMotionUpdatesToQueue:_motionQueue
                                     withHandler:^(CMDeviceMotion *motion, NSError *error) {
            if (motion == nil || error != nil) return;
            // userAcceleration は重力除去済み・g単位。CSVは m/s² なので換算する
            // (Unity の Input.gyro.userAcceleration と同じ意味・同じ単位系に揃える)
            const double g = 9.80665;
            [weakSelf pushSampleAtTime:motion.timestamp
                                     x:(float)(motion.userAcceleration.x * g)
                                     y:(float)(motion.userAcceleration.y * g)
                                     z:(float)(motion.userAcceleration.z * g)];
        }];
    }
}

- (void)stop {
    _running = NO;

    dispatch_async(dispatch_get_main_queue(), ^{
        if (self->_displayLink != nil) {
            [self->_displayLink invalidate];
            self->_displayLink = nil;
        }
    });

    if (_motion != nil && _motion.isDeviceMotionActive) {
        [_motion stopDeviceMotionUpdates];
    }
}

- (void)onDisplayTick:(CADisplayLink *)link {
    // targetTimestamp = このフレームが表示される予定の時刻
    _targetTimestamp = link.targetTimestamp;
}

- (double)targetTimestamp {
    return _targetTimestamp;
}

- (BOOL)isImuStreaming {
    return (_motion != nil && _motion.isDeviceMotionActive);
}

- (void)pushSampleAtTime:(double)t x:(float)x y:(float)y z:(float)z {
    os_unfair_lock_lock(&_ringLock);

    _ringTime[_ringHead] = t;
    _ringXyz[_ringHead * 3 + 0] = x;
    _ringXyz[_ringHead * 3 + 1] = y;
    _ringXyz[_ringHead * 3 + 2] = z;
    _ringHead = (_ringHead + 1) % kImuRingCapacity;

    if (_ringCount < kImuRingCapacity) {
        _ringCount++;
    } else {
        // 満杯 = Unity側の回収が止まっている。最古を捨てて進む
        _droppedCount++;
    }

    os_unfair_lock_unlock(&_ringLock);
}

- (int)drainSamples:(double *)outTimestamps xyz:(float *)outXyz max:(int)maxSamples {
    if (outTimestamps == NULL || outXyz == NULL || maxSamples <= 0) return 0;

    os_unfair_lock_lock(&_ringLock);

    int available = _ringCount;
    int take = (available < maxSamples) ? available : maxSamples;

    // 古い順に取り出す。読み出し開始位置 = head から count ぶん戻った位置
    int start = (_ringHead - _ringCount + kImuRingCapacity * 2) % kImuRingCapacity;
    for (int i = 0; i < take; i++) {
        int idx = (start + i) % kImuRingCapacity;
        outTimestamps[i]     = _ringTime[idx];
        outXyz[i * 3 + 0]    = _ringXyz[idx * 3 + 0];
        outXyz[i * 3 + 1]    = _ringXyz[idx * 3 + 1];
        outXyz[i * 3 + 2]    = _ringXyz[idx * 3 + 2];
    }

    _ringCount -= take; // 取り出した分だけ未回収を減らす(残りは次フレームへ)

    os_unfair_lock_unlock(&_ringLock);
    return take;
}

- (long)droppedCount {
    return _droppedCount;
}

@end

// ════════════════════════════════════════════════════════════════════════════
// C API (Unity から DllImport("__Internal") で呼ばれる)
// ════════════════════════════════════════════════════════════════════════════
extern "C" {

void ARV_StartSensorTiming(void) { [[ARVSensorTiming shared] start]; }
void ARV_StopSensorTiming(void)  { [[ARVSensorTiming shared] stop]; }

/// ARKitフレーム・CADisplayLink と同じ時間軸の現在時刻(秒)。
double ARV_CurrentMediaTime(void) { return CACurrentMediaTime(); }

/// いま描いているフレームの提示予定時刻(秒)。未起動なら 0。
double ARV_DisplayTargetTimestamp(void) { return [[ARVSensorTiming shared] targetTimestamp]; }

/// 100Hz IMU が流れているか。
int ARV_IsImuStreaming(void) { return [[ARVSensorTiming shared] isImuStreaming] ? 1 : 0; }

/// 貯まった IMU サンプルを古い順に引き取る。戻り値 = 実際に取れた件数。
int ARV_DrainImuSamples(double* outTimestamps, float* outXyz, int maxSamples) {
    return [[ARVSensorTiming shared] drainSamples:outTimestamps xyz:outXyz max:maxSamples];
}

}
