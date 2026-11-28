package vn.hiep.gtadash;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.DownloadManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.Settings;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * Cap nhat ngay trong app: tim release "dash-N" moi hon tren GitHub, tai APK bang DownloadManager
 * va mo trinh cai dat cua Android. Android chi cho cai de khi hai APK ky cung mot khoa.
 */
final class Updater {
    private static final String RELEASES = "https://api.github.com/repos/Dshiepbv/Gta-viet-life/releases?per_page=50";
    private static final String ASSET_PREFIX = "https://github.com/Dshiepbv/Gta-viet-life/releases/download/";

    private final Activity activity;
    private long downloadId = -1;
    private BroadcastReceiver receiver;

    Updater(Activity activity) { this.activity = activity; }

    private long installedVersion() throws Exception {
        android.content.pm.PackageInfo info = activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0);
        return Build.VERSION.SDK_INT >= 28 ? info.getLongVersionCode() : info.versionCode;
    }

    /** announce = true khi nguoi dung bam nut: bao ca khi khong co ban moi hoac khong doc duoc. */
    void check(final boolean announce) {
        new Thread(() -> {
            try {
                long current = installedVersion();
                long best = current;
                String url = null;
                java.net.HttpURLConnection c = (java.net.HttpURLConnection) new java.net.URL(RELEASES).openConnection();
                c.setConnectTimeout(15000);
                c.setReadTimeout(25000);
                c.setRequestProperty("User-Agent", "GtaDashboard");
                if (c.getResponseCode() != 200) throw new java.io.IOException("GitHub tra ve ma " + c.getResponseCode());
                java.io.ByteArrayOutputStream body = new java.io.ByteArrayOutputStream();
                try (java.io.InputStream in = c.getInputStream()) {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = in.read(buffer)) > 0) body.write(buffer, 0, read);
                }
                JSONArray releases = new JSONArray(body.toString("UTF-8"));
                for (int i = 0; i < releases.length(); i++) {
                    JSONObject release = releases.getJSONObject(i);
                    if (release.optBoolean("draft") || release.optBoolean("prerelease")) continue;
                    String tag = release.optString("tag_name");
                    if (!tag.matches("dash-\\d{1,9}")) continue;
                    long number = Long.parseLong(tag.substring(5));
                    if (number <= best) continue;
                    JSONArray assets = release.optJSONArray("assets");
                    for (int j = 0; assets != null && j < assets.length(); j++) {
                        JSONObject asset = assets.getJSONObject(j);
                        String link = asset.optString("browser_download_url");
                        if (asset.optString("name").equals("GtaDashboard-" + number + ".apk") && link.startsWith(ASSET_PREFIX)) {
                            best = number;
                            url = link;
                        }
                    }
                }
                final long newest = best;
                final String link = url;
                activity.runOnUiThread(() -> {
                    if (activity.isFinishing()) return;
                    if (link == null) {
                        if (announce) toast("Dang dung ban moi nhat (" + newest + ").");
                        return;
                    }
                    new AlertDialog.Builder(activity).setTitle("Co ban moi")
                        .setMessage("Cap nhat len ban " + newest + " (dang dung " + current + ")?")
                        .setPositiveButton("Cap nhat", (d, w) -> download(link, newest))
                        .setNegativeButton("De sau", null).show();
                });
            } catch (Exception e) {
                if (announce) activity.runOnUiThread(() -> toast("Khong kiem tra duoc ban moi: " + e.getMessage()));
            }
        }).start();
    }

    private void download(String url, long number) {
        // Android 8 tro len: nguoi dung phai cho phep app nay cai ung dung.
        if (Build.VERSION.SDK_INT >= 26 && !activity.getPackageManager().canRequestPackageInstalls()) {
            toast("Bat 'Cho phep tu nguon nay' roi bam cap nhat lai.");
            activity.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                Uri.parse("package:" + activity.getPackageName())));
            return;
        }
        DownloadManager manager = (DownloadManager) activity.getSystemService(Context.DOWNLOAD_SERVICE);
        DownloadManager.Request request = new DownloadManager.Request(Uri.parse(url))
            .setTitle("GTA Dashboard " + number)
            .setMimeType("application/vnd.android.package-archive")
            .setDestinationInExternalFilesDir(activity, Environment.DIRECTORY_DOWNLOADS,
                "GtaDashboard-" + number + "-" + System.currentTimeMillis() + ".apk")
            .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
        close();
        receiver = new BroadcastReceiver() {
            @Override public void onReceive(Context context, Intent intent) {
                if (intent.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -1) == downloadId) install(manager);
            }
        };
        IntentFilter done = new IntentFilter(DownloadManager.ACTION_DOWNLOAD_COMPLETE);
        if (Build.VERSION.SDK_INT >= 33) activity.registerReceiver(receiver, done, Context.RECEIVER_EXPORTED);
        else activity.registerReceiver(receiver, done);
        downloadId = manager.enqueue(request);
        toast("Dang tai ban " + number + "...");
    }

    private void install(DownloadManager manager) {
        try (Cursor cursor = manager.query(new DownloadManager.Query().setFilterById(downloadId))) {
            if (cursor == null || !cursor.moveToFirst() || cursor.getInt(cursor.getColumnIndexOrThrow(
                    DownloadManager.COLUMN_STATUS)) != DownloadManager.STATUS_SUCCESSFUL) {
                toast("Tai ban cap nhat that bai.");
                return;
            }
        }
        Intent intent = new Intent(Intent.ACTION_VIEW)
            .setDataAndType(manager.getUriForDownloadedFile(downloadId), "application/vnd.android.package-archive")
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        try { activity.startActivity(intent); }
        catch (android.content.ActivityNotFoundException e) { toast("May khong mo duoc trinh cai dat."); }
    }

    void close() {
        if (receiver != null) {
            activity.unregisterReceiver(receiver);
            receiver = null;
        }
    }

    private void toast(String message) { Toast.makeText(activity, message, Toast.LENGTH_LONG).show(); }
}
