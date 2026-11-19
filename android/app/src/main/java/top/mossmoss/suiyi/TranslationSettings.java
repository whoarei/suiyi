package top.mossmoss.suiyi;

import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.ApplicationInfo;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.KeyStore;
import java.security.ProviderException;
import java.util.Locale;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

final class TranslationSettings {
    private static final String KEY_ALIAS = "suiyi-translation-key";

    final boolean demo;
    final boolean quick;
    final String address;
    final String model;
    final Language primary;
    final Language other;
    final int revision;
    final boolean hasKey;
    private final SharedPreferences preferences;
    private final String encrypted;
    private final String encodedIv;

    TranslationSettings(Context context) {
        preferences = context.getSharedPreferences("translation", Context.MODE_PRIVATE);
        demo = preferences.getBoolean("demo", true);
        quick = preferences.getBoolean("quick", false);
        address = preferences.getString("address", "");
        model = preferences.getString("model", "");
        Locale locale = android.os.Build.VERSION.SDK_INT >= 24
                ? context.getResources().getConfiguration().getLocales().get(0)
                : context.getResources().getConfiguration().locale;
        primary = Language.fromCode(preferences.getString("primary", ""), Language.forLocale(locale));
        Language savedOther = Language.fromCode(preferences.getString("other", ""), primary.defaultOther());
        other = savedOther == primary ? primary.defaultOther() : savedOther;
        revision = preferences.getInt("revision", 0);
        encrypted = preferences.getString("key", "");
        encodedIv = preferences.getString("iv", "");
        hasKey = !encrypted.isEmpty();
    }

    boolean save(boolean demo, boolean quick, String address, String model,
                 Language primary, Language other, String newKey, boolean clearKey)
            throws GeneralSecurityException {
        SharedPreferences.Editor edit = preferences.edit()
                .putBoolean("demo", demo)
                .putBoolean("quick", quick)
                .putString("address", address)
                .putString("model", model)
                .putString("primary", primary.code)
                .putString("other", other.code)
                .putInt("revision", revision + 1);
        if (clearKey || (!this.address.equals(address) && newKey.isEmpty())) {
            edit.remove("key").remove("iv");
        } else if (!newKey.isEmpty()) {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, secretKey(true));
            cipher.updateAAD(address.getBytes(StandardCharsets.UTF_8));
            edit.putString("key", encode(cipher.doFinal(newKey.getBytes(StandardCharsets.UTF_8))));
            edit.putString("iv", encode(cipher.getIV()));
        }
        return edit.commit();
    }

    String readKey() throws GeneralSecurityException {
        if (!hasKey) {
            return "";
        }
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        try {
            byte[] iv = Base64.decode(encodedIv, Base64.NO_WRAP);
            cipher.init(Cipher.DECRYPT_MODE, secretKey(false), new GCMParameterSpec(128, iv));
            cipher.updateAAD(address.getBytes(StandardCharsets.UTF_8));
            return new String(cipher.doFinal(Base64.decode(encrypted, Base64.NO_WRAP)),
                    StandardCharsets.UTF_8);
        } catch (IllegalArgumentException | ProviderException exception) {
            throw new GeneralSecurityException(exception);
        }
    }

    boolean configured() {
        return !address.isEmpty() && validModel(model);
    }

    static boolean validModel(String value) {
        if (value.trim().isEmpty() || value.codePointCount(0, value.length()) > 256) {
            return false;
        }
        for (int index = 0; index < value.length(); index++) {
            if (Character.isISOControl(value.charAt(index))) {
                return false;
            }
        }
        return true;
    }

    static boolean validKey(String value) {
        return value.matches("[!-~]{0,4096}");
    }

    static String normalizeAddress(Context context, String value) {
        if (value.length() > 2048) {
            throw new IllegalArgumentException();
        }
        URI uri = URI.create(value);
        String host = uri.getHost();
        boolean debug = (context.getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) != 0;
        boolean secure = "https".equalsIgnoreCase(uri.getScheme());
        boolean debugLoopback = debug && "http".equalsIgnoreCase(uri.getScheme())
                && "127.0.0.1".equals(host);
        if ((!secure && !debugLoopback) || host == null || uri.getRawUserInfo() != null
                || uri.getRawQuery() != null || uri.getRawFragment() != null
                || uri.getPort() > 65535 || uri.getPort() == 0) {
            throw new IllegalArgumentException();
        }
        String path = uri.normalize().getRawPath().replaceFirst("/+$", "");
        if (path.toLowerCase(Locale.ROOT).endsWith("/chat/completions")) {
            path = path.substring(0, path.length() - "/chat/completions".length());
        }
        String port = uri.getPort() == -1 ? "" : ":" + uri.getPort();
        return uri.getScheme().toLowerCase(Locale.ROOT) + "://"
                + host.toLowerCase(Locale.ROOT) + port + path;
    }

    private static String encode(byte[] bytes) {
        return Base64.encodeToString(bytes, Base64.NO_WRAP);
    }

    private static SecretKey secretKey(boolean create) throws GeneralSecurityException {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        try {
            store.load(null);
        } catch (java.io.IOException exception) {
            throw new GeneralSecurityException(exception);
        }
        SecretKey existing = (SecretKey) store.getKey(KEY_ALIAS, null);
        if (existing != null) {
            return existing;
        }
        if (!create) {
            throw new GeneralSecurityException("Missing Android Keystore key");
        }
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,
                "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return generator.generateKey();
    }
}
