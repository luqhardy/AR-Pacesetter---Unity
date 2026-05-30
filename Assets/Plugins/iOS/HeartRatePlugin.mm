#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>

@interface HeartRateBLEManager : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>
@property (nonatomic, strong) CBCentralManager *centralManager;
@property (nonatomic, strong) CBPeripheral *heartRatePeripheral;
@end

@implementation HeartRateBLEManager

static HeartRateBLEManager *sharedInstance = nil;

+ (HeartRateBLEManager *)sharedInstance {
    if (sharedInstance == nil) {
        sharedInstance = [[HeartRateBLEManager alloc] init];
    }
    return sharedInstance;
}

- (void)startScan {
    self.centralManager = [[CBCentralManager alloc] initWithDelegate:self queue:nil];
}

- (void)stopScan {
    [self.centralManager stopScan];
}

// Check iOS Bluetooth Hardware State
- (void)centralManagerDidUpdateState:(CBCentralManager *)central {
    if (central.state == CBCharacterSetURLAllowedCharacterSet) { // Powered On
        // 0x180D is the standard universal Bluetooth GATT ID for Heart Rate Services
        [self.centralManager scanForPeripheralsWithServices:@[[CBUUID UUIDWithString:@"180D"]] options:nil];
    }
}

- (void)centralManager:(CBCentralManager *)central didDiscoverPeripheral:(CBPeripheral *)peripheral advertisementData:(NSDictionary<NSString *,id> *)advertisementData RSSI:(NSNumber *)RSSI {
    self.heartRatePeripheral = peripheral;
    self.heartRatePeripheral.delegate = self;
    [self.centralManager connectPeripheral:peripheral options:nil];
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral {
    [peripheral discoverServices:@[[CBUUID UUIDWithString:@"180D"]]];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error {
    for (CBService *service in peripheral.services) {
        // 0x2A37 is the standard universal GATT Characteristic for Live Heart Rate Measurement data packets
        [peripheral discoverCharacteristics:@[[CBUUID UUIDWithString:@"2A37"]] forService:service];
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error {
    for (CBCharacteristic *characteristic in service.characteristics) {
        [peripheral setNotifyValue:YES forCharacteristic:characteristic];
    }
}

// Parse Raw Bluetooth Data Packet
- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    if ([characteristic.UUID isEqual:[CBUUID UUIDWithString:@"2A37"]]) {
        NSData *data = characteristic.value;
        const uint8_t *reportData = (const uint8_t *)data.bytes;
        uint16_t heartRate = 0;
        
        if ((reportData[0] & 0x01) == 0) {
            heartRate = reportData[1]; // 8-bit format data structure
        } else {
            heartRate = CFSwapInt16LittleToHost(*(uint16_t *)(&reportData[1])); // 16-bit format data structure
        }
        
        NSString *bpmString = [NSString stringWithFormat:@"%d", heartRate];
        
        // Push the data back into Unity's C# layer instantly
        UnitySendMessage("AR_Vision_Manager", "OnHeartRateDataReceived", [bpmString UTF8String]);
    }
}
@end

// C-Linkage Interface Mapping for the C# DllImport Wrapper
extern "C" {
    void StartHeartRateBLEScan() {
        [[HeartRateBLEManager sharedInstance] startScan];
    }
    void StopHeartRateBLEScan() {
        [[HeartRateBLEManager sharedInstance] stopScan];
    }
}#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>

@interface HeartRateBLEManager : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>
@property (nonatomic, strong) CBCentralManager *centralManager;
@property (nonatomic, strong) CBPeripheral *heartRatePeripheral;
@end

@implementation HeartRateBLEManager

static HeartRateBLEManager *sharedInstance = nil;

+ (HeartRateBLEManager *)sharedInstance {
    if (sharedInstance == nil) {
        sharedInstance = [[HeartRateBLEManager alloc] init];
    }
    return sharedInstance;
}

- (void)startScan {
    self.centralManager = [[CBCentralManager alloc] initWithDelegate:self queue:nil];
}

- (void)stopScan {
    [self.centralManager stopScan];
}

// Check iOS Bluetooth Hardware State
- (void)centralManagerDidUpdateState:(CBCentralManager *)central {
    if (central.state == CBCharacterSetURLAllowedCharacterSet) { // Powered On
        // 0x180D is the standard universal Bluetooth GATT ID for Heart Rate Services
        [self.centralManager scanForPeripheralsWithServices:@[[CBUUID UUIDWithString:@"180D"]] options:nil];
    }
}

- (void)centralManager:(CBCentralManager *)central didDiscoverPeripheral:(CBPeripheral *)peripheral advertisementData:(NSDictionary<NSString *,id> *)advertisementData RSSI:(NSNumber *)RSSI {
    self.heartRatePeripheral = peripheral;
    self.heartRatePeripheral.delegate = self;
    [self.centralManager connectPeripheral:peripheral options:nil];
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral {
    [peripheral discoverServices:@[[CBUUID UUIDWithString:@"180D"]]];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error {
    for (CBService *service in peripheral.services) {
        // 0x2A37 is the standard universal GATT Characteristic for Live Heart Rate Measurement data packets
        [peripheral discoverCharacteristics:@[[CBUUID UUIDWithString:@"2A37"]] forService:service];
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error {
    for (CBCharacteristic *characteristic in service.characteristics) {
        [peripheral setNotifyValue:YES forCharacteristic:characteristic];
    }
}

// Parse Raw Bluetooth Data Packet
- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    if ([characteristic.UUID isEqual:[CBUUID UUIDWithString:@"2A37"]]) {
        NSData *data = characteristic.value;
        const uint8_t *reportData = (const uint8_t *)data.bytes;
        uint16_t heartRate = 0;
        
        if ((reportData[0] & 0x01) == 0) {
            heartRate = reportData[1]; // 8-bit format data structure
        } else {
            heartRate = CFSwapInt16LittleToHost(*(uint16_t *)(&reportData[1])); // 16-bit format data structure
        }
        
        NSString *bpmString = [NSString stringWithFormat:@"%d", heartRate];
        
        // Push the data back into Unity's C# layer instantly
        UnitySendMessage("AR_Vision_Manager", "OnHeartRateDataReceived", [bpmString UTF8String]);
    }
}
@end

// C-Linkage Interface Mapping for the C# DllImport Wrapper
extern "C" {
    void StartHeartRateBLEScan() {
        [[HeartRateBLEManager sharedInstance] startScan];
    }
    void StopHeartRateBLEScan() {
        [[HeartRateBLEManager sharedInstance] stopScan];
    }
}