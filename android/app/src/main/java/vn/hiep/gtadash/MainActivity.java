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
import android.webkit.JsResult;
import android.webkit.WebChromeClient;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.EditText;

/** Vo WebView cho dashboard.py tren PC: nho dia chi, luon sang man hinh, giu nguyen giao dien web. */
public class MainActivity extends Activity {
    WebView web;
    SharedPreferences prefs;

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
        if (addr.isEmpty()) askAddr(); else web.loadUrl(addr);
    }

    void askAddr() {
        final EditText et = new EditText(this);
        et.setInputType(InputType.TYPE_TEXT_VARIATION_URI);
        et.setText(prefs.getString("addr", "http://192.168.1.10:5588"));
        new AlertDialog.Builder(this).setTitle("Dia chi dashboard tren PC")
            .setMessage("Dang http://IP-may-tinh:5588 (IP in ra khi chay start_dashboard.bat)")
            .setView(et).setCancelable(!prefs.getString("addr", "").isEmpty())
            .setPositiveButton("Luu", (d, w) -> {
                String a = et.getText().toString().trim();
                if (!a.startsWith("http")) a = "http://" + a;
                prefs.edit().putString("addr", a).apply();
                web.loadUrl(a);
            }).show();
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
