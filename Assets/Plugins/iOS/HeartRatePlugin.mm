#import <CoreBluetooth/CoreBluetooth.h>
#import <Foundation/Foundation.h>
#include <cmath>

// Forward declaration for the Unity communication hook
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

#ifdef __OBJC__
@interface HeartRateBLEManager : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>
@property(nonatomic, strong) CBCentralManager *centralManager;
@property(nonatomic, strong) NSMutableArray<CBPeripheral *> *connectedPeripherals;
@property(nonatomic, assign) BOOL scanningEnabled; // StopHeartRateBLEScan 後は探し直さない
@end
#endif

@implementation HeartRateBLEManager

static HeartRateBLEManager *sharedInstance = nil;

+ (HeartRateBLEManager *)sharedInstance {
    if (sharedInstance == nil) {
        sharedInstance = [[HeartRateBLEManager alloc] init];
    }
    return sharedInstance;
}

- (instancetype)init {
    self = [super init];
    if (self) {
        _connectedPeripherals = [[NSMutableArray alloc] init];
    }
    return self;
}

- (void)startScan {
    self.scanningEnabled = YES;
    self.centralManager = [[CBCentralManager alloc] initWithDelegate:self queue:nil];
}

- (void)stopScan {
    self.scanningEnabled = NO;
    [self.centralManager stopScan];
    for (CBPeripheral *p in self.connectedPeripherals) {
        [self.centralManager cancelPeripheralConnection:p];
    }
    [self.connectedPeripherals removeAllObjects];
}

// Monitor iOS Bluetooth Hardware State
- (void)centralManagerDidUpdateState:(CBCentralManager *)central {
    if (central.state == CBManagerStatePoweredOn) {
        // Scan for both Heart Rate (180D) and Running Speed & Cadence (1814) (Requirement 2 & 4.3)
        [self.centralManager scanForPeripheralsWithServices:@[ 
            [CBUUID UUIDWithString:@"180D"], 
            [CBUUID UUIDWithString:@"1814"] 
        ] options:nil];
    }
}

// 自分のセンサーだけに繋ぐための受信強度の下限(dBm)。胸ストラップ・フットポッドは
// 身体に着けているので -70 より強い。陸上トラックでは他の走者のストラップも同じ
// サービスを広告しているため、これが無いと他人の心拍が混ざる。
static const int kMinOwnSensorRssi = -70;

- (void)centralManager:(CBCentralManager *)central
 didDiscoverPeripheral:(CBPeripheral *)peripheral
     advertisementData:(NSDictionary<NSString *, id> *)advertisementData
                  RSSI:(NSNumber *)RSSI {

    // 繋ぐのは1台だけ。以前は見つけたセンサーを全部繋いでおり、他人の心拍が
    // 自分の値と交互に届いてバイタル警告(深青)を誤って点けうる状態だった
    if (self.connectedPeripherals.count > 0) return;

    int rssi = RSSI.intValue;
    if (rssi == 127 || rssi < kMinOwnSensorRssi) return; // 127 = 受信強度不明

    [self.connectedPeripherals addObject:peripheral];
    peripheral.delegate = self;
    [self.centralManager stopScan];
    [self.centralManager connectPeripheral:peripheral options:nil];
}

// 接続に失敗/切断したら、1台ぶんの枠を空けて探し直す
- (void)centralManager:(CBCentralManager *)central
didFailToConnectPeripheral:(CBPeripheral *)peripheral
                 error:(NSError *)error {
    [self forgetPeripheralAndRescan:peripheral];
}

- (void)centralManager:(CBCentralManager *)central
didDisconnectPeripheral:(CBPeripheral *)peripheral
                 error:(NSError *)error {
    [self forgetPeripheralAndRescan:peripheral];
}

- (void)forgetPeripheralAndRescan:(CBPeripheral *)peripheral {
    [self.connectedPeripherals removeObject:peripheral];
    if (self.scanningEnabled && self.centralManager.state == CBManagerStatePoweredOn) {
        [self.centralManager scanForPeripheralsWithServices:@[
            [CBUUID UUIDWithString:@"180D"],
            [CBUUID UUIDWithString:@"1814"]
        ] options:nil];
    }
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral {
    // Discover both biometric services on the connected peripheral
    [peripheral discoverServices:@[ 
        [CBUUID UUIDWithString:@"180D"], 
        [CBUUID UUIDWithString:@"1814"] 
    ]];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error {
    if (error) return;
    for (CBService *service in peripheral.services) {
        if ([service.UUID isEqual:[CBUUID UUIDWithString:@"180D"]]) {
            [peripheral discoverCharacteristics:@[ [CBUUID UUIDWithString:@"2A37"] ] forService:service];
        }
        else if ([service.UUID isEqual:[CBUUID UUIDWithString:@"1814"]]) {
            [peripheral discoverCharacteristics:@[ [CBUUID UUIDWithString:@"2A53"] ] forService:service];
        }
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error {
    if (error) return;
    for (CBCharacteristic *characteristic in service.characteristics) {
        [peripheral setNotifyValue:YES forCharacteristic:characteristic];
    }
}

// Parse Raw Bluetooth Data Packet
- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    if (error) return;
    
    // 1. Heart Rate Parsing (2A37)
    if ([characteristic.UUID isEqual:[CBUUID UUIDWithString:@"2A37"]]) {
        NSData *data = characteristic.value;
        if (data.length < 2) return;
        
        const uint8_t *reportData = (const uint8_t *)data.bytes;
        uint16_t heartRate = 0;

        if ((reportData[0] & 0x01) == 0) {
            heartRate = reportData[1];
        } else {
            if (data.length >= 3) {
                uint16_t rawValue;
                memcpy(&rawValue, &reportData[1], sizeof(uint16_t));
                heartRate = CFSwapInt16LittleToHost(rawValue);
            }
        }

        NSString *bpmString = [NSString stringWithFormat:@"%d", heartRate];
        UnitySendMessage("AR_Vision_Manager", "OnHeartRateDataReceived", [bpmString UTF8String]);
    }
    
    // 2. Running Speed and Cadence (RSC) Cadence Parsing (2A53) (Requirement 2 & 4.3)
    else if ([characteristic.UUID isEqual:[CBUUID UUIDWithString:@"2A53"]]) {
        NSData *data = characteristic.value;
        if (data.length < 4) return;
        
        const uint8_t *reportData = (const uint8_t *)data.bytes;
        // Byte 0: Flags
        // Bytes 1-2: Instantaneous Speed
        // Byte 3: Instantaneous Cadence (Strides/Revolutions per minute, RPM)
        uint8_t rawCadence = reportData[3];
        uint8_t finalPitch = rawCadence;
        
        // Stride-rate normalization: if the cadence reports in RPM (strides/revolutions per minute, typically < 110 RPM)
        // Convert to standard running pitch SPM (steps per minute) by multiplying by 2.
        if (rawCadence < 110) {
            finalPitch = rawCadence * 2;
        }

        NSString *pitchString = [NSString stringWithFormat:@"%d", finalPitch];
        UnitySendMessage("AR_Vision_Manager", "OnRunningPitchReceived", [pitchString UTF8String]);
    }
}
@end

// C-Linkage Interface Mapping for Unity C# DllImport Wrapper
extern "C" {
    // --- Bluetooth Controls ---
    void StartHeartRateBLEScan() {
        [[HeartRateBLEManager sharedInstance] startScan];
    }
    void StopHeartRateBLEScan() {
        [[HeartRateBLEManager sharedInstance] stopScan];
    }

    // このファイルは心拍BLEの責務のみを持つ。カルマンフィルタは C# の SpatialKalmanFilter
    // (2026-09-11 にネイティブ実装を廃止)。以前ここにあった同名関数は duplicate symbol の原因だった。
}
