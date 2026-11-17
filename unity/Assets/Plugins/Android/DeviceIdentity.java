package com.sowsiege.auth;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import android.util.Base64;
import java.io.File;
import java.io.FileOutputStream;
import java.io.FileNotFoundException;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.security.KeyPairGenerator;
import java.security.KeyStore;
import java.security.Signature;
import java.security.spec.ECGenParameterSpec;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Private keys never leave AndroidKeyStore. Only authenticated ciphertext is written to the no-backup directory. */
public final class DeviceIdentity {
    private DeviceIdentity() { }
    public static synchronized String call(Context context, String scope, String operation, String value) throws Exception {
        if (!scope.matches("sow-device-v1-[a-f0-9]{64}")) throw new SecurityException("Invalid identity scope");
        KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null);
        String signer = scope + ".sign", encryption = scope + ".session";
        if (operation.equals("public") || operation.equals("sign")) {
            if (!store.containsAlias(signer)) {
                if (operation.equals("sign")) throw new SecurityException("Missing device key");
                KeyPairGenerator generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore");
                generator.initialize(new KeyGenParameterSpec.Builder(signer, KeyProperties.PURPOSE_SIGN)
                    .setAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
                    .setDigests(KeyProperties.DIGEST_SHA256).build());
                generator.generateKeyPair();
            }
            if (operation.equals("public")) return Base64.encodeToString(store.getCertificate(signer).getPublicKey().getEncoded(), Base64.NO_WRAP);
            Signature signature = Signature.getInstance("SHA256withECDSA");
            signature.initSign((java.security.PrivateKey)store.getKey(signer, null));
            signature.update(Base64.decode(value, Base64.NO_WRAP));
            return Base64.encodeToString(signature.sign(), Base64.NO_WRAP);
        }
        AtomicFile file = new AtomicFile(new File(context.getNoBackupFilesDir(), scope + ".bin"));
        byte[] stored = null;
        if (operation.equals("load")) {
            try { stored = file.readFully(); } catch (FileNotFoundException missing) { return ""; }
        }
        if (!store.containsAlias(encryption)) {
            if (operation.equals("load")) throw new SecurityException("Missing storage key");
            KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(encryption, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256).build());
            generator.generateKey();
        }
        SecretKey key = (SecretKey)store.getKey(encryption, null);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        byte[] aad = scope.getBytes(StandardCharsets.UTF_8);
        if (operation.equals("load")) {
            byte[] bytes = stored;
            if (bytes.length < 29 || bytes.length > 16384 || bytes[0] != 1) throw new SecurityException("Invalid encrypted session");
            cipher.init(Cipher.DECRYPT_MODE, key, new GCMParameterSpec(128, Arrays.copyOfRange(bytes, 1, 13)));
            cipher.updateAAD(aad);
            byte[] clear = cipher.doFinal(bytes, 13, bytes.length - 13);
            try { return new String(clear, StandardCharsets.UTF_8); } finally { Arrays.fill(clear, (byte)0); }
        }
        if (!operation.equals("save") || value == null || value.length() > 8192) throw new SecurityException("Invalid storage request");
        cipher.init(Cipher.ENCRYPT_MODE, key); cipher.updateAAD(aad);
        byte[] clear = value.getBytes(StandardCharsets.UTF_8), encrypted;
        try { encrypted = cipher.doFinal(clear); } finally { Arrays.fill(clear, (byte)0); }
        byte[] bytes = ByteBuffer.allocate(1 + cipher.getIV().length + encrypted.length).put((byte)1).put(cipher.getIV()).put(encrypted).array();
        FileOutputStream output = null;
        try { output = file.startWrite(); output.write(bytes); file.finishWrite(output); }
        catch (Exception failure) { if (output != null) file.failWrite(output); throw failure; }
        return "ok";
    }
}
