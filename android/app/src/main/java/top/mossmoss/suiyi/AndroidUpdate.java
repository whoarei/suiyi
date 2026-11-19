package top.mossmoss.suiyi;

import android.content.Context;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.pm.Signature;
import android.os.Build;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InterruptedIOException;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.DigestOutputStream;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

final class AndroidUpdate {
    private static final String REPOSITORY = "whoarei/suiyi";
    private static final String API = "https://api.github.com/repos/" + REPOSITORY + "/releases/latest";
    private static final String DOWNLOAD = "https://github.com/" + REPOSITORY + "/releases/download/";
    private static final String NUMBER = "(?:0|[1-9][0-9]{0,8})";
    private static final String VERSION = NUMBER + "\\." + NUMBER + "\\." + NUMBER;
    private static final Pattern APK_NAME = Pattern.compile("suiyi-(" + VERSION + ")-android\\.apk");
    private static final Pattern HASH = Pattern.compile("[a-f0-9]{64}");
    private static final long MAX_APK = 128L * 1024 * 1024;

    record Release(String version, URL url, long size, String sha256) {}

    static Release check(Context context) throws UiRequest.Failure {
        HttpURLConnection connection = null;
        try {
            String current = context.getPackageManager().getPackageInfo(context.getPackageName(), 0).versionName;
            connection = open(new URL(API), false);
            ByteArrayOutputStream body = new ByteArrayOutputStream();
            try (InputStream input = connection.getInputStream()) {
                copy(input, body, 1024 * 1024);
            }
            return findUpdate(new JSONObject(new String(body.toByteArray(), StandardCharsets.UTF_8)), current);
        } catch (IOException | PackageManager.NameNotFoundException exception) {
            throw new UiRequest.Failure(R.string.update_check_failed);
        } catch (JSONException exception) {
            throw new UiRequest.Failure(R.string.update_metadata_invalid);
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    private static Release findUpdate(JSONObject release, String current) throws JSONException, IOException,
            UiRequest.Failure {
        String tag = release.getString("tag_name");
        if (release.getBoolean("draft") || release.getBoolean("prerelease")
                || !tag.matches("v" + VERSION) || current == null || !current.matches(VERSION)) {
            throw new UiRequest.Failure(R.string.update_metadata_invalid);
        }
        JSONArray assets = release.getJSONArray("assets");
        Release candidate = null;
        boolean hasAndroid = false;
        for (int index = 0; index < assets.length(); index++) {
            JSONObject asset = assets.getJSONObject(index);
            String name = asset.optString("name");
            Matcher apk = APK_NAME.matcher(name);
            if (!apk.matches() || !"uploaded".equals(asset.optString("state"))) {
                continue;
            }
            hasAndroid = true;
            String version = apk.group(1);
            if (compareVersions(version, current) <= 0
                    || (candidate != null && compareVersions(version, candidate.version) <= 0)) {
                continue;
            }
            long size = asset.optLong("size");
            String digest = asset.optString("digest").toLowerCase(Locale.ROOT);
            String url = DOWNLOAD + tag + "/" + name;
            if (size <= 0 || size > MAX_APK || !digest.startsWith("sha256:")
                    || !HASH.matcher(digest.substring(7)).matches()
                    || !url.equals(asset.optString("browser_download_url"))) {
                throw new UiRequest.Failure(R.string.update_metadata_invalid);
            }
            candidate = new Release(version, new URL(url), size, digest.substring(7));
        }
        if (!hasAndroid) {
            throw new UiRequest.Failure(R.string.update_no_apk);
        }
        return candidate;
    }

    private static int compareVersions(String left, String right) {
        String[] a = left.split("\\.");
        String[] b = right.split("\\.");
        for (int index = 0; index < a.length; index++) {
            int difference = Integer.compare(Integer.parseInt(a[index]), Integer.parseInt(b[index]));
            if (difference != 0) {
                return difference;
            }
        }
        return 0;
    }

    static File download(Context context, Release update) throws UiRequest.Failure {
        File target = cachedApk(context, update.sha256 + ".apk");
        File part = null;
        HttpURLConnection connection = null;
        try {
            File directory = target.getParentFile();
            if (!directory.isDirectory() && !directory.mkdirs() && !directory.isDirectory()) {
                throw new IOException("Cannot create update cache directory");
            }
            part = File.createTempFile("download-", ".part.apk", directory);
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            connection = open(update.url, true);
            try (InputStream input = connection.getInputStream();
                 OutputStream output = new DigestOutputStream(new FileOutputStream(part), digest)) {
                if (copy(input, output, update.size) != update.size) {
                    throw new UiRequest.Failure(R.string.update_integrity_failed);
                }
            }
            if (!MessageDigest.isEqual(digest.digest(), decodeHash(update.sha256))) {
                throw new UiRequest.Failure(R.string.update_integrity_failed);
            }
            verifyApk(context, part, update.version);
            checkInterrupted();
            if (!part.renameTo(target)) {
                throw new IOException("Cannot store verified APK");
            }
            return target;
        } catch (IOException | NoSuchAlgorithmException | PackageManager.NameNotFoundException exception) {
            throw new UiRequest.Failure(R.string.update_download_failed);
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
            if (part != null) {
                part.delete();
            }
        }
    }

    static File cachedApk(Context context, String name) {
        if (name == null || !name.endsWith(".apk") || !HASH.matcher(name.substring(0, name.length() - 4)).matches()) {
            return null;
        }
        return new File(new File(context.getCacheDir(), "updates"), name);
    }

    @SuppressWarnings("deprecation")
    private static void verifyApk(Context context, File apk, String version)
            throws PackageManager.NameNotFoundException, UiRequest.Failure {
        PackageManager manager = context.getPackageManager();
        int flags = Build.VERSION.SDK_INT >= 28 ? PackageManager.GET_SIGNING_CERTIFICATES
                : PackageManager.GET_SIGNATURES;
        PackageInfo installed = manager.getPackageInfo(context.getPackageName(), flags);
        PackageInfo archive = manager.getPackageArchiveInfo(apk.getAbsolutePath(), flags);
        if (archive == null || !installed.packageName.equals(archive.packageName)
                || !version.equals(archive.versionName) || versionCode(archive) <= versionCode(installed)) {
            throw new UiRequest.Failure(R.string.update_integrity_failed);
        }
        Signature[] current = signers(installed);
        Signature[] offered = signers(archive);
        if (current == null || offered == null || current.length != 1 || offered.length != 1
                || !current[0].equals(offered[0])) {
            throw new UiRequest.Failure(R.string.update_signer_mismatch);
        }
    }

    @SuppressWarnings("deprecation")
    private static long versionCode(PackageInfo info) {
        return Build.VERSION.SDK_INT >= 28 ? info.getLongVersionCode() : info.versionCode;
    }

    @SuppressWarnings("deprecation")
    private static Signature[] signers(PackageInfo info) {
        if (Build.VERSION.SDK_INT >= 28) {
            return info.signingInfo == null ? null : info.signingInfo.getApkContentsSigners();
        }
        return info.signatures;
    }

    private static HttpURLConnection open(URL url, boolean download) throws IOException {
        for (int hop = 0; hop < 5; hop++) {
            checkInterrupted();
            HttpURLConnection connection = (HttpURLConnection) url.openConnection();
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(8000);
            connection.setReadTimeout(12000);
            connection.setRequestProperty("User-Agent", "Suiyi-Android");
            if (!download) {
                connection.setRequestProperty("Accept", "application/vnd.github+json");
            }
            boolean connected = false;
            try {
                int status = connection.getResponseCode();
                if (status == HttpURLConnection.HTTP_OK) {
                    connected = true;
                    return connection;
                }
                String location = connection.getHeaderField("Location");
                if (!download || status < 300 || status >= 400 || location == null) {
                    throw new IOException("Unexpected GitHub response");
                }
                url = new URL(url, location);
                String host = url.getHost();
                if (!"https".equals(url.getProtocol()) || (url.getPort() != -1 && url.getPort() != 443)
                        || url.getUserInfo() != null || url.getRef() != null
                        || !("release-assets.githubusercontent.com".equalsIgnoreCase(host)
                        || "objects.githubusercontent.com".equalsIgnoreCase(host))) {
                    throw new IOException("Unexpected GitHub download redirect");
                }
            } finally {
                if (!connected) {
                    connection.disconnect();
                }
            }
        }
        throw new IOException("Too many GitHub download redirects");
    }

    private static long copy(InputStream input, OutputStream output, long limit) throws IOException {
        byte[] buffer = new byte[8192];
        long size = 0;
        int count;
        while ((count = input.read(buffer)) != -1) {
            checkInterrupted();
            size += count;
            if (size > limit) {
                throw new IOException("GitHub response exceeds expected size");
            }
            output.write(buffer, 0, count);
        }
        return size;
    }

    private static byte[] decodeHash(String hex) {
        byte[] bytes = new byte[32];
        for (int index = 0; index < bytes.length; index++) {
            int offset = index * 2;
            bytes[index] = (byte) ((Character.digit(hex.charAt(offset), 16) << 4)
                    | Character.digit(hex.charAt(offset + 1), 16));
        }
        return bytes;
    }

    private static void checkInterrupted() throws InterruptedIOException {
        if (Thread.currentThread().isInterrupted()) {
            throw new InterruptedIOException();
        }
    }
}
