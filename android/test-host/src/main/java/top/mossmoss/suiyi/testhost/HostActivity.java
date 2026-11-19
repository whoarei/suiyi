package top.mossmoss.suiyi.testhost;

import android.app.Activity;
import android.os.Bundle;
import android.view.View;
import android.webkit.WebView;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;

public final class HostActivity extends Activity {
    static final String PREFIX = "开头 | ";
    static final String SAMPLE = "你好，世界。";
    static final String SUFFIX = " | 结尾";
    EditText editor;
    TextView readonly;
    WebView web;

    @Override
    public void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setFocusableInTouchMode(true);
        int margin = Math.round(20 * getResources().getDisplayMetrics().density);
        layout.setOnApplyWindowInsetsListener((view, insets) -> {
            view.setPadding(margin, margin + insets.getSystemWindowInsetTop(), margin,
                    margin + insets.getSystemWindowInsetBottom());
            return insets;
        });
        TextView title = new TextView(this);
        title.setText("长按输入框或只读文字，选中问候语。");
        layout.addView(title);
        editor = new EditText(this);
        editor.setId(View.generateViewId());
        editor.setText(PREFIX + SAMPLE + SUFFIX);
        editor.setTextSize(22);
        editor.setShowSoftInputOnFocus(false);
        layout.addView(editor);
        readonly = new TextView(this);
        readonly.setId(View.generateViewId());
        readonly.setText(SAMPLE);
        readonly.setTextSize(22);
        readonly.setTextIsSelectable(true);
        readonly.setPadding(0, margin, 0, margin);
        layout.addView(readonly);
        web = new WebView(this);
        web.getSettings().setJavaScriptEnabled(true);
        web.loadDataWithBaseURL(null, "<meta charset=utf-8>"
                + "<meta name=viewport content='width=device-width, initial-scale=1'>"
                + "<label for=editor>网页输入框</label>"
                + "<input id=editor style='width:95%;font:20px monospace' "
                + "value='START | Hello | END'>", "text/html", "UTF-8", null);
        layout.addView(web, new LinearLayout.LayoutParams(-1, 0, 1));
        setContentView(layout);
        layout.requestFocus();
    }
}
