package top.mossmoss.suiyi;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.view.Gravity;
import android.view.View;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Space;
import android.widget.TextView;

public final class ProcessTextActivity extends Activity {
    private final UiRequest request = new UiRequest();
    private TextView status;
    private TextView original;
    private EditText result;
    private LinearLayout actions;
    private Button originalToggle;
    private ImageButton replace;
    private ImageButton copy;
    private Button retry;
    private ImageButton cancel;
    private TranslationSettings settings;
    private boolean editable;
    private boolean quick;
    private boolean resumed;
    private boolean hadFocus;
    private String source;
    private String pendingQuick;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        Intent intent = getIntent();
        if (state != null || !Intent.ACTION_PROCESS_TEXT.equals(intent.getAction())
                || getCallingPackage() == null) {
            finish();
            return;
        }
        CharSequence selection = intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT);
        source = selection == null ? "" : selection.toString();
        settings = new TranslationSettings(this);
        editable = !intent.getBooleanExtra(Intent.EXTRA_PROCESS_TEXT_READONLY, true);
        quick = editable && settings.quick;
        boolean valid = source.length() <= TranslationClient.MAX_SOURCE && TranslationClient.hasText(source);
        buildDialog();
        if (valid) {
            translate();
        } else {
            status.setText(R.string.invalid_source);
            NativeUi.setIconLabel(cancel, R.string.close);
        }
    }

    private void buildDialog() {
        setTitle(R.string.app_name);
        ScrollView scroll = new ScrollView(this);
        LinearLayout layout = NativeUi.column(this);
        layout.setFocusableInTouchMode(true);
        layout.setPaddingRelative(NativeUi.dp(this, 20), NativeUi.dp(this, 8),
                NativeUi.dp(this, 8), NativeUi.dp(this, 8));
        scroll.addView(layout);
        setContentView(scroll);

        LinearLayout readingRow = NativeUi.row(this);
        readingRow.setGravity(Gravity.TOP);
        LinearLayout content = NativeUi.column(this);
        content.setPaddingRelative(0, NativeUi.dp(this, 9), 0, 0);
        NativeUi.fill(readingRow, content);
        cancel = NativeUi.iconButton(this, R.drawable.ic_close, R.string.cancel, view -> finish());
        readingRow.addView(cancel);
        layout.addView(readingRow);

        status = NativeUi.text(this, 16, true);
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        status.setPadding(0, 0, 0, NativeUi.dp(this, 4));
        content.addView(status);
        original = NativeUi.text(this, 15, true);
        original.setText(source);
        original.setPadding(0, 0, 0, NativeUi.dp(this, 8));
        original.setVisibility(View.GONE);
        content.addView(original);
        result = NativeUi.editor(this, R.string.result, 1);
        result.setTextSize(20);
        result.setPadding(0, 0, 0, 0);
        result.setVisibility(View.GONE);
        content.addView(result);

        actions = NativeUi.row(this);
        actions.setPaddingRelative(0, 0, NativeUi.dp(this, 8), 0);
        actions.setVisibility(View.GONE);
        originalToggle = NativeUi.button(this, R.string.source, false, view -> toggleOriginal());
        originalToggle.setContentDescription(getText(R.string.show_source));
        originalToggle.setTextColor(getColor(R.color.muted));
        originalToggle.setVisibility(View.GONE);
        actions.addView(originalToggle);
        NativeUi.fill(actions, new Space(this));
        retry = NativeUi.button(this, R.string.retry, false, view -> translate());
        retry.setVisibility(View.GONE);
        actions.addView(retry);
        copy = NativeUi.iconButton(this, R.drawable.ic_copy, R.string.copy, view -> copyResult());
        copy.setVisibility(View.GONE);
        actions.addView(copy);
        replace = NativeUi.iconButton(this, R.drawable.ic_replace, R.string.replace, view -> {
            String text = editedResult();
            if (text != null) {
                returnTranslation(text);
            }
        });
        replace.setVisibility(View.GONE);
        actions.addView(replace);
        layout.addView(actions);
        layout.requestFocus();
    }

    private void toggleOriginal() {
        boolean expand = original.getVisibility() != View.VISIBLE;
        original.setVisibility(expand ? View.VISIBLE : View.GONE);
        originalToggle.setText(expand ? R.string.collapse : R.string.source);
        originalToggle.setContentDescription(getText(expand ? R.string.hide_source : R.string.show_source));
    }

    private void copyResult() {
        String text = editedResult();
        if (text != null) {
            NativeUi.copy(this, text);
            if (!editable) {
                finish();
            }
        }
    }

    private void translate() {
        if (!settings.demo && !settings.configured()) {
            status.setText(R.string.setup_required);
            retry.setText(R.string.settings);
            retry.setOnClickListener(view -> {
                startActivity(new Intent(this, SettingsActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                finish();
            });
            retry.setVisibility(View.VISIBLE);
            actions.setVisibility(View.VISIBLE);
            NativeUi.setIconLabel(cancel, R.string.close);
            return;
        }
        actions.setVisibility(View.GONE);
        retry.setVisibility(View.GONE);
        NativeUi.setIconLabel(cancel, R.string.cancel);
        status.setText(R.string.translating);
        request.start(() -> TranslationClient.translate(this, settings, source), output -> {
            if (isFinishing()) {
                return;
            }
            if (quick) {
                pendingQuick = output;
                maybeReplace();
            } else {
                status.setVisibility(View.GONE);
                result.setText(output);
                result.setVisibility(View.VISIBLE);
                originalToggle.setVisibility(View.VISIBLE);
                copy.setVisibility(View.VISIBLE);
                replace.setVisibility(editable ? View.VISIBLE : View.GONE);
                actions.setVisibility(View.VISIBLE);
            }
        }, failure -> {
            if (!isFinishing()) {
                status.setText(failure.message(this));
                retry.setVisibility(View.VISIBLE);
                actions.setVisibility(View.VISIBLE);
                NativeUi.setIconLabel(cancel, R.string.close);
            }
        }, 15000);
    }

    private String editedResult() {
        String text = result.getText().toString();
        if (text.length() > TranslationClient.MAX_RESULT || !TranslationClient.hasText(text)) {
            result.setError(getString(R.string.invalid_result));
            return null;
        }
        return text;
    }

    private void returnTranslation(String text) {
        if (!resumed || isFinishing()) {
            return;
        }
        if (result.hasFocus()) {
            getSystemService(InputMethodManager.class).hideSoftInputFromWindow(result.getWindowToken(), 0);
        }
        Intent reply = new Intent().putExtra(Intent.EXTRA_PROCESS_TEXT, (CharSequence) text);
        setResult(RESULT_OK, reply);
        finish();
    }

    private void maybeReplace() {
        if (pendingQuick != null && hasWindowFocus()) {
            returnTranslation(pendingQuick);
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        resumed = true;
        maybeReplace();
    }

    @Override
    protected void onPause() {
        resumed = false;
        super.onPause();
    }

    @Override
    public void onWindowFocusChanged(boolean focused) {
        super.onWindowFocusChanged(focused);
        if (focused) {
            hadFocus = true;
            maybeReplace();
        } else if (quick && hadFocus && !isFinishing()) {
            request.cancel();
            finish();
        }
    }

    @Override
    protected void onStop() {
        request.cancel();
        if (!isFinishing()) {
            finish();
        }
        super.onStop();
    }
}
