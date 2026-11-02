#import <Foundation/Foundation.h>
#import <Security/Security.h>
#import <LocalAuthentication/LocalAuthentication.h>
#import <TargetConditionals.h>
#include <stdlib.h>
#include <string.h>

static LAContext *SilentContext() { LAContext *context = [[LAContext alloc] init]; context.interactionNotAllowed = YES; return context; }

static NSMutableDictionary *SessionQuery(NSString *scope) {
    NSMutableDictionary *query = [@{(__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: @"com.sowsiege.device-auth.v1", (__bridge id)kSecAttrAccount: scope,
        (__bridge id)kSecAttrSynchronizable: @NO, (__bridge id)kSecUseAuthenticationContext: SilentContext()} mutableCopy];
#if TARGET_OS_OSX
    query[(__bridge id)kSecUseDataProtectionKeychain] = @YES;
#endif
    return query;
}

static SecKeyRef DeviceKey(NSString *scope, BOOL create) {
    NSData *tag = [[scope stringByAppendingString:@".sign"] dataUsingEncoding:NSUTF8StringEncoding];
    NSMutableDictionary *query = [@{(__bridge id)kSecClass: (__bridge id)kSecClassKey,
        (__bridge id)kSecAttrApplicationTag: tag, (__bridge id)kSecAttrKeyType: (__bridge id)kSecAttrKeyTypeECSECPrimeRandom,
        (__bridge id)kSecAttrKeyClass: (__bridge id)kSecAttrKeyClassPrivate,
        (__bridge id)kSecReturnRef: @YES, (__bridge id)kSecUseAuthenticationContext: SilentContext()} mutableCopy];
#if TARGET_OS_OSX
    query[(__bridge id)kSecUseDataProtectionKeychain] = @YES;
#endif
    SecKeyRef key = NULL;
    OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, (CFTypeRef *)&key);
    if (status == errSecSuccess) return key;
    if (status != errSecItemNotFound || !create) return NULL;
    CFErrorRef error = NULL;
    SecAccessControlRef access = SecAccessControlCreateWithFlags(kCFAllocatorDefault,
        kSecAttrAccessibleWhenUnlockedThisDeviceOnly, kSecAccessControlPrivateKeyUsage, &error);
    if (!access) { if (error) CFRelease(error); return NULL; }
    NSDictionary *attributes = @{(__bridge id)kSecAttrKeyType: (__bridge id)kSecAttrKeyTypeECSECPrimeRandom,
        (__bridge id)kSecAttrKeySizeInBits: @256, (__bridge id)kSecAttrTokenID: (__bridge id)kSecAttrTokenIDSecureEnclave,
        (__bridge id)kSecPrivateKeyAttrs: @{(__bridge id)kSecAttrIsPermanent: @YES,
            (__bridge id)kSecAttrApplicationTag: tag, (__bridge id)kSecAttrAccessControl: (__bridge id)access}};
    key = SecKeyCreateRandomKey((__bridge CFDictionaryRef)attributes, &error);
    CFRelease(access); if (error) CFRelease(error);
    return key;
}

extern "C" char *SowIdentity(const char *scopeBytes, const char *operationBytes, const char *valueBytes) {
    @autoreleasepool {
        if (!scopeBytes || !operationBytes) return NULL;
        NSString *scope = [NSString stringWithUTF8String:scopeBytes], *operation = [NSString stringWithUTF8String:operationBytes];
        if (scope.length != 78 || ![scope hasPrefix:@"sow-device-v1-"]) return NULL;
        NSString *result = nil;
        if ([operation isEqualToString:@"public"] || [operation isEqualToString:@"sign"]) {
            SecKeyRef key = DeviceKey(scope, [operation isEqualToString:@"public"]);
            if (!key) return NULL;
            CFErrorRef error = NULL; CFDataRef data = NULL;
            if ([operation isEqualToString:@"public"]) {
                SecKeyRef publicKey = SecKeyCopyPublicKey(key);
                if (publicKey) { data = SecKeyCopyExternalRepresentation(publicKey, &error); CFRelease(publicKey); }
                if (data && CFDataGetLength(data) == 65 && CFDataGetBytePtr(data)[0] == 4) {
                    const uint8_t prefix[] = {0x30,0x59,0x30,0x13,0x06,0x07,0x2a,0x86,0x48,0xce,0x3d,0x02,0x01,0x06,0x08,0x2a,0x86,0x48,0xce,0x3d,0x03,0x01,0x07,0x03,0x42,0x00};
                    NSMutableData *spki = [NSMutableData dataWithBytes:prefix length:sizeof(prefix)];
                    [spki appendData:(__bridge NSData *)data]; result = [spki base64EncodedStringWithOptions:0];
                }
            } else if (valueBytes) {
                NSData *message = [[NSData alloc] initWithBase64EncodedString:[NSString stringWithUTF8String:valueBytes] options:0];
                if (message) data = SecKeyCreateSignature(key, kSecKeyAlgorithmECDSASignatureMessageX962SHA256, (__bridge CFDataRef)message, &error);
                if (data) result = [(__bridge NSData *)data base64EncodedStringWithOptions:0];
            }
            if (data) CFRelease(data); if (error) CFRelease(error); CFRelease(key);
        } else if ([operation isEqualToString:@"load"]) {
            NSMutableDictionary *query = SessionQuery(scope);
            query[(__bridge id)kSecReturnData] = @YES;
            CFTypeRef data = NULL; OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &data);
            if (status == errSecItemNotFound) result = @"";
            else if (status == errSecSuccess) result = [[NSString alloc] initWithData:(__bridge NSData *)data encoding:NSUTF8StringEncoding];
            if (data) CFRelease(data);
        } else if ([operation isEqualToString:@"save"] && valueBytes && strlen(valueBytes) <= 8192) {
            NSMutableDictionary *query = SessionQuery(scope);
            NSData *value = [[NSString stringWithUTF8String:valueBytes] dataUsingEncoding:NSUTF8StringEncoding];
            NSDictionary *update = @{(__bridge id)kSecValueData: value};
            OSStatus status = SecItemUpdate((__bridge CFDictionaryRef)query, (__bridge CFDictionaryRef)update);
            if (status == errSecItemNotFound) {
                query[(__bridge id)kSecValueData] = value;
                query[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleWhenUnlockedThisDeviceOnly;
                status = SecItemAdd((__bridge CFDictionaryRef)query, NULL);
            }
            if (status == errSecSuccess) result = @"ok";
        }
        return result ? strdup(result.UTF8String) : NULL;
    }
}
extern "C" void SowIdentityFree(char *value) {
    if (value) { volatile char *p = value; size_t count = strlen(value); while (count--) *p++ = 0; free(value); }
}
