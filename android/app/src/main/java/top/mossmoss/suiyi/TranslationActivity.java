package top.mossmoss.suiyi;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.os.Bundle;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.Gravity;
import android.view.View;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.LinearLayout;
import android.widget.Space;
import android.widget.TextView;

public final class TranslationActivity extends Activity {
    private final UiRequest request = new UiRequest();
    private TranslationSettings settings;
    private EditText source;
    private EditText result;
    private LinearLayout output;
    private Button translate;
    private Button paste;
    private Button sample;
    private ImageButton clear;
    private ImageButton copy;
    private TextView pair;
    private TextView status;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        settings = new TranslationSettings(this);
        NativeUi.Page page = NativeUi.page(this);
        pair = NativeUi.text(this, 16, false);
        pair.setTypeface(android.graphics.Typeface.create("sans-serif-medium", android.graphics.Typeface.NORMAL));
        NativeUi.fill(page.header(), pair);
        ImageButton settingsButton = NativeUi.iconButton(this, R.drawable.ic_settings, R.string.settings,
                view -> openSettings());
        page.header().addView(settingsButton);

        source = NativeUi.editor(this, R.string.source_hint, 2);
        source.setTextSize(22);
        page.body().addView(source);

        LinearLayout inputActions = NativeUi.row(this);
        paste = NativeUi.button(this, R.string.paste, false, view -> paste());
        inputActions.addView(paste);
        sample = NativeUi.button(this, R.string.demo_sample, false, view -> {
            source.setText(R.string.demo_source);
            translate(true);
        });
        inputActions.addView(sample);
        NativeUi.fill(inputActions, new Space(this));
        clear = NativeUi.iconButton(this, R.drawable.ic_close, R.string.clear, view -> source.setText(""));
        inputActions.addView(clear);
        translate = NativeUi.button(this, R.string.translate, false, view -> {
            if (request.running()) {
                request.cancel();
                setStatus(R.string.cancelled);
                refreshActions();
            } else {
                translate(false);
            }
        });
        inputActions.addView(translate);
        page.body().addView(inputActions);

        status = NativeUi.text(this, 14, true);
        status.setPaddingRelative(0, NativeUi.dp(this, 8), 0, NativeUi.dp(this, 4));
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        status.setVisibility(View.GONE);
        page.body().addView(status);

        output = NativeUi.column(this);
        output.setVisibility(View.GONE);
        View divider = new View(this);
        divider.setBackgroundColor(getColor(R.color.outline));
        LinearLayout.LayoutParams dividerLayout = new LinearLayout.LayoutParams(-1, 1);
        dividerLayout.setMargins(0, NativeUi.dp(this, 14), 0, NativeUi.dp(this, 12));
        output.addView(divider, dividerLayout);
        LinearLayout resultRow = NativeUi.row(this);
        resultRow.setGravity(Gravity.TOP);
        result = NativeUi.editor(this, R.string.result, 1);
        result.setTextSize(22);
        NativeUi.fill(resultRow, result);
        copy = NativeUi.iconButton(this, R.drawable.ic_copy, R.string.copy,
                view -> NativeUi.copy(this, result.getText().toString()));
        resultRow.addView(copy);
        output.addView(resultRow);
        page.body().addView(output);

        source.addTextChangedListener(watch(() -> {
            request.cancel();
            clearResult();
            setStatus(source.length() > TranslationClient.MAX_SOURCE ? R.string.source_too_long : 0);
            refreshActions();
        }));
        result.addTextChangedListener(watch(this::refreshActions));
        Draft retained = (Draft) getLastNonConfigurationInstance();
        if (retained != null) {
            source.setText(retained.source);
            if (retained.revision == settings.revision && retained.resultVisible) {
                output.setVisibility(View.VISIBLE);
                result.setText(retained.result);
            }
        } else {
            receive(getIntent());
        }
        refreshActions();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        receive(intent);
    }

    @Override
    public Object onRetainNonConfigurationInstance() {
        return new Draft(source.getText().toString(), result.getText().toString(),
                output.getVisibility() == View.VISIBLE, settings.revision);
    }

    @Override
    protected void onResume() {
        super.onResume();
        TranslationSettings current = new TranslationSettings(this);
        if (current.revision != settings.revision) {
            request.cancel();
            clearResult();
            setStatus(source.length() > TranslationClient.MAX_SOURCE ? R.string.source_too_long : 0);
        }
        settings = current;
        pair.setText(current.demo ? getString(R.string.demo_pair)
                : getString(R.string.language_pair, getString(current.primary.label), getString(current.other.label)));
        refreshActions();
    }

    @Override
    protected void onStop() {
        if (request.running()) {
            request.cancel();
            setStatus(R.string.cancelled);
            refreshActions();
        }
        super.onStop();
    }

    private void translate(boolean demoSample) {
        String input = source.getText().toString();
        if (!TranslationClient.hasText(input) || input.length() > TranslationClient.MAX_SOURCE) {
            setStatus(R.string.invalid_source);
            return;
        }
        if (!demoSample && !settings.demo && !settings.configured()) {
            setStatus(R.string.setup_required);
            openSettings();
            return;
        }
        getSystemService(InputMethodManager.class).hideSoftInputFromWindow(source.getWindowToken(), 0);
        clearResult();
        setStatus(R.string.translating);
        TranslationSettings snapshot = settings;
        request.start(() -> demoSample ? TranslationClient.translateDemo(input)
                        : TranslationClient.translate(this, snapshot, input), translation -> {
                    result.setText(translation);
                    output.setVisibility(View.VISIBLE);
                    setStatus(0);
                    refreshActions();
                }, failure -> {
                    status.setText(failure.message(this));
                    status.setVisibility(View.VISIBLE);
                    refreshActions();
                }, 15000);
        refreshActions();
    }

    private void paste() {
        ClipboardManager clipboard = getSystemService(ClipboardManager.class);
        ClipData clip = clipboard.getPrimaryClip();
        CharSequence value = clip == null || clip.getItemCount() == 0 ? null : clip.getItemAt(0).getText();
        if (value == null || !TranslationClient.hasText(value.toString())) {
            setStatus(R.string.empty_clipboard);
        } else if (value.length() > TranslationClient.MAX_SOURCE) {
            setStatus(R.string.invalid_source);
        } else {
            source.setText(value.toString());
        }
    }

    private void receive(Intent intent) {
        if (!Intent.ACTION_SEND.equals(intent.getAction()) || !"text/plain".equals(intent.getType())) {
            return;
        }
        CharSequence value = intent.getCharSequenceExtra(Intent.EXTRA_TEXT);
        if (value == null || value.length() > TranslationClient.MAX_SOURCE
                || !TranslationClient.hasText(value.toString())) {
            setStatus(R.string.invalid_shared);
        } else {
            source.setText(value.toString());
        }
        // A share only pre-fills the draft. Translation starts after the user taps the button.
        setIntent(new Intent(Intent.ACTION_MAIN));
    }

    private void openSettings() {
        startActivity(new Intent(this, SettingsActivity.class));
    }

    private void refreshActions() {
        int length = source.length();
        boolean busy = request.running();
        paste.setVisibility(length == 0 ? View.VISIBLE : View.GONE);
        sample.setVisibility(length == 0 && settings.demo ? View.VISIBLE : View.GONE);
        clear.setVisibility(length > 0 ? View.VISIBLE : View.GONE);
        translate.setText(busy ? R.string.cancel : R.string.translate);
        boolean canTranslate = busy || length <= TranslationClient.MAX_SOURCE
                && TranslationClient.hasText(source.getText().toString());
        translate.setVisibility(canTranslate ? View.VISIBLE : View.GONE);
        boolean canCopy = result.length() <= TranslationClient.MAX_RESULT
                && TranslationClient.hasText(result.getText().toString());
        copy.setVisibility(canCopy ? View.VISIBLE : View.INVISIBLE);
    }

    private void clearResult() {
        output.setVisibility(View.GONE);
        result.setText("");
    }

    private void setStatus(int message) {
        if (message == 0) {
            status.setText("");
            status.setVisibility(View.GONE);
        } else {
            status.setText(message);
            status.setVisibility(View.VISIBLE);
        }
    }

    private static TextWatcher watch(Runnable action) {
        return new TextWatcher() {
            public void beforeTextChanged(CharSequence text, int start, int count, int after) {}
            public void onTextChanged(CharSequence text, int start, int before, int count) { action.run(); }
            public void afterTextChanged(Editable text) {}
        };
    }

    private record Draft(String source, String result, boolean resultVisible, int revision) {}
}
