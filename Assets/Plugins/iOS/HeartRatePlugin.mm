#import <CoreBluetooth/CoreBluetooth.h>
#import <Foundation/Foundation.h>
#include <cmath>

// Forward declaration for the Unity communication hook
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

#ifdef __OBJC__
@interface HeartRateBLEManager : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>
@property(nonatomic, strong) CBCentralManager *centralManager;
@property(nonatomic, strong) NSMutableArray<CBPeripheral *> *connectedPeripherals;
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
    self.centralManager = [[CBCentralManager alloc] initWithDelegate:self queue:nil];
}

- (void)stopScan {
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

- (void)centralManager:(CBCentralManager *)central
 didDiscoverPeripheral:(CBPeripheral *)peripheral
     advertisementData:(NSDictionary<NSString *, id> *)advertisementData
                  RSSI:(NSNumber *)RSSI {
    
    // Check if we already have this peripheral in our connection array
    if (![self.connectedPeripherals containsObject:peripheral]) {
        [self.connectedPeripherals addObject:peripheral];
        peripheral.delegate = self;
        [self.centralManager connectPeripheral:peripheral options:nil];
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

    // --- Kalman Filter Pipeline Constants & Functions ---
    static float estimateX = 0.0f;
    static float estimateY = 0.0f;
    static float estimateZ = 0.0f;
    static float pX = 1.0f;
    static float pY = 1.0f;
    static float pZ = 1.0f;
    static float processNoise = 0.05f; // Q
    static float measurementNoise = 0.8f; // R
    static float lteWeight = 0.12f;

    void InitKalmanFilter(float pNoise, float mNoise, float lteW) {
        processNoise = pNoise;
        measurementNoise = mNoise;
        lteWeight = lteW;
        estimateX = 0.0f;
        estimateY = 0.0f;
        estimateZ = 0.0f;
        pX = 1.0f;
        pY = 1.0f;
        pZ = 1.0f;
    }

    void UpdateKalmanFilter(float accelX, float accelY, float accelZ, float* smoothX, float* smoothY, float* smoothZ) {
        // Predict stage
        pX = pX + processNoise;
        pY = pY + processNoise;
        pZ = pZ + processNoise;

        // Kalman Gain calculation
        float kX = pX / (pX + measurementNoise);
        float kY = pY / (pY + measurementNoise);
        float kZ = pZ / (pZ + measurementNoise);

        // Correction stage (state update)
        estimateX = estimateX + kX * (accelX - estimateX);
        estimateY = estimateY + kY * (accelY - estimateY);
        estimateZ = estimateZ + kZ * (accelZ - estimateZ);

        // Error covariance update stage
        pX = (1.0f - kX) * pX;
        pY = (1.0f - kY) * pY;
        pZ = (1.0f - kZ) * pZ;

        *smoothX = estimateX;
        *smoothY = estimateY;
        *smoothZ = estimateZ;
    }
}
