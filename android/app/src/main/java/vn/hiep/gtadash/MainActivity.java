package vn.hiep.gtadash;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.os.Bundle;
import android.text.InputType;
import android.view.Menu;
import android.view.View;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.JsResult;
import android.webkit.WebChromeClient;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.EditText;

/** Vo WebView cho dashboard.py tren PC: nho dia chi, luon sang man hinh, giu nguyen giao dien web. */
public class MainActivity extends Activity {
    /** Cau noi HTTP cho trang (tranh loi CORS khi trang nam trong asset): JS goi Native.request, Java tra ket qua ve window.__cb. */
    class Bridge {
        @JavascriptInterface public void request(final int id, final String method, final String url, final String body) {
            new Thread(() -> {
                String res;
                try {
                    java.net.HttpURLConnection c = (java.net.HttpURLConnection) new java.net.URL(url).openConnection();
                    c.setInstanceFollowRedirects(true);
                    c.setConnectTimeout(15000); c.setReadTimeout(25000);
                    c.setRequestMethod(method);
                    if ("POST".equals(method)) {
                        c.setDoOutput(true);
                        c.setRequestProperty("Content-Type", "text/plain");
                        c.getOutputStream().write(body.getBytes("UTF-8"));
                    }
                    java.io.InputStream in = c.getResponseCode() >= 400 ? c.getErrorStream() : c.getInputStream();
                    java.io.ByteArrayOutputStream bo = new java.io.ByteArrayOutputStream();
                    byte[] buf = new byte[4096]; int n;
                    while ((n = in.read(buf)) > 0) bo.write(buf, 0, n);
                    res = bo.toString("UTF-8");
                } catch (Exception e) {
                    res = "{\"error\":\"Khong ket noi duoc: " + String.valueOf(e.getMessage()).replace("\"", "'") + "\"}";
                }
                final String js = "window.__cb(" + id + "," + org.json.JSONObject.quote(res) + ")";
                runOnUiThread(() -> web.evaluateJavascript(js, null));
            }).start();
        }
    }

    WebView web;
    SharedPreferences prefs;
    Updater updater;

    @Override protected void onCreate(Bundle b) {
        super.onCreate(b);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        getWindow().setStatusBarColor(Color.parseColor("#0b0f14"));
        getWindow().setNavigationBarColor(Color.parseColor("#0b0f14"));
        prefs = getSharedPreferences("dash", MODE_PRIVATE);
        web = new WebView(this);
        web.setBackgroundColor(Color.parseColor("#0b0f14"));
        web.getSettings().setJavaScriptEnabled(true);
        web.getSettings().setDomStorageEnabled(true);
        web.getSettings().setAllowFileAccess(true);
        web.addJavascriptInterface(new Bridge(), "Native");
        web.setWebViewClient(new WebViewClient() {
            @Override public void onReceivedError(WebView v, int code, String d, String url) {
                v.loadData("<body style='background:#0b0f14;color:#e8eef5;font:16px sans-serif;padding:24px'>"
                    + "<h3>Khong ket noi duoc may tinh</h3><p>Kiem tra: PC dang chay start_dashboard.bat, "
                    + "dien thoai cung Wi-Fi, dung IP.</p><p>Bam nut menu (hoac vuot) de doi dia chi.</p></body>",
                    "text/html; charset=utf-8", "UTF-8");
            }
        });
        web.setWebChromeClient(new WebChromeClient() {
            @Override public boolean onJsAlert(WebView v, String url, String msg, JsResult r) {
                new AlertDialog.Builder(MainActivity.this).setMessage(msg)
                    .setPositiveButton("OK", (d, w) -> r.confirm()).setOnCancelListener(d -> r.cancel()).show();
                return true;
            }
        });
        setContentView(web);
        String addr = prefs.getString("addr", "");
        if (addr.isEmpty()) askAddr(); else open(addr);
        updater = new Updater(this);
        updater.check(false);
    }

    @Override protected void onDestroy() {
        if (updater != null) updater.close();
        super.onDestroy();
    }

    // dia chi Google Apps Script (https://script.google.com/...) -> che do dam may: trang nam trong APK; con lai -> tai thang tu PC
    void open(String a) {
        if (a.contains("script.google.com")) web.loadUrl("file:///android_asset/dashboard.html?api=" + android.net.Uri.encode(a));
        else web.loadUrl(a);
    }

    void askAddr() {
        final EditText et = new EditText(this);
        et.setInputType(InputType.TYPE_TEXT_VARIATION_URI);
        et.setText(prefs.getString("addr", "http://192.168.1.10:5588"));
        new AlertDialog.Builder(this).setTitle("Dia chi dashboard")
            .setMessage("Dan link Google (https://script.google.com/.../exec) de xem 24/24 ca khi PC tat, hoac http://IP-may-tinh:5588 khi o nha.")
            .setView(et).setCancelable(!prefs.getString("addr", "").isEmpty())
            .setPositiveButton("Luu", (d, w) -> {
                String a = et.getText().toString().trim();
                if (!a.startsWith("http")) a = "http://" + a;
                prefs.edit().putString("addr", a).apply();
                open(a);
            })
            .setNeutralButton("Cap nhat app", (d, w) -> { if (updater != null) updater.check(true); })
            .show();
    }

    @Override public boolean onCreateOptionsMenu(Menu m) { m.add("Doi dia chi PC"); return true; }
    @Override public boolean onOptionsItemSelected(android.view.MenuItem i) { askAddr(); return true; }
    // menu cung (nut 3 cham) hoac nhan giu nut Back de doi dia chi
    @Override public boolean onKeyLongPress(int k, android.view.KeyEvent e) {
        if (k == android.view.KeyEvent.KEYCODE_BACK) { askAddr(); return true; }
        return super.onKeyLongPress(k, e);
    }
    @Override public boolean onKeyDown(int k, android.view.KeyEvent e) {
        if (k == android.view.KeyEvent.KEYCODE_BACK) { e.startTracking(); return true; }
        return super.onKeyDown(k, e);
    }
    @Override public boolean onKeyUp(int k, android.view.KeyEvent e) {
        if (k == android.view.KeyEvent.KEYCODE_BACK && !e.isCanceled()) {
            if (web.canGoBack()) web.goBack(); else finish();
            return true;
        }
        return super.onKeyUp(k, e);
    }
}
