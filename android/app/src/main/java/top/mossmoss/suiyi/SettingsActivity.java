package top.mossmoss.suiyi;

import android.app.Activity;
import android.app.AlertDialog;
import android.os.Build;
import android.os.Bundle;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.view.View;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.security.GeneralSecurityException;
import java.security.ProviderException;

public final class SettingsActivity extends Activity {
    private final UiRequest request = new UiRequest();
    private EditText address;
    private EditText model;
    private EditText key;
    private Switch demo;
    private Switch quick;
    private CheckBox clearKey;
    private Button load;
    private Button test;
    private Button primaryButton;
    private Button otherButton;
    private TextView status;
    private LinearLayout apiFields;
    private TextView demoNote;
    private TranslationSettings saved;
    private Language primary;
    private Language other;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        saved = new TranslationSettings(this);
        primary = Language.fromCode(state == null ? "" : state.getString("primary"), saved.primary);
        other = Language.fromCode(state == null ? "" : state.getString("other"), saved.other);
        NativeUi.Page page = NativeUi.page(this);
        page.header().addView(NativeUi.button(this, R.string.back, false, view -> finish()));
        NativeUi.fill(page.header(), NativeUi.title(this, R.string.settings, 24));
        page.header().addView(NativeUi.button(this, R.string.save, true, view -> save()));

        LinearLayout preferences = NativeUi.card(page.body());
        primaryButton = languageRow(preferences, R.string.primary_language, true);
        otherButton = languageRow(preferences, R.string.other_language, false);
        addNote(preferences, R.string.language_rule);
        quick = new Switch(this);
        quick.setText(R.string.quick_mode);
        quick.setMinHeight(NativeUi.dp(this, 48));
        quick.setChecked(state == null ? saved.quick : state.getBoolean("quick", saved.quick));
        preferences.addView(quick);
        addNote(preferences, R.string.quick_note);

        LinearLayout service = NativeUi.card(page.body());
        service.addView(NativeUi.title(this, R.string.service, 20));
        demo = new Switch(this);
        demo.setText(R.string.demo_enabled);
        demo.setMinHeight(NativeUi.dp(this, 48));
        demo.setChecked(state == null ? saved.demo : state.getBoolean("demo", saved.demo));
        service.addView(demo);
        demoNote = addNote(service, R.string.demo_instructions);
        apiFields = NativeUi.column(this);
        service.addView(apiFields);
        address = addInput(apiFields, R.string.api_address, InputType.TYPE_TEXT_VARIATION_URI);
        address.setText(state == null ? saved.address : state.getString("address", saved.address));
        model = addInput(apiFields, R.string.api_model, InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        model.setText(state == null ? saved.model : state.getString("model", saved.model));
        key = addInput(apiFields, R.string.api_key, InputType.TYPE_TEXT_VARIATION_PASSWORD);
        if (Build.VERSION.SDK_INT >= 26) {
            key.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO);
        }
        clearKey = new CheckBox(this);
        clearKey.setText(R.string.clear_key);
        apiFields.addView(clearKey);
        addNote(apiFields, R.string.api_note);
        LinearLayout actions = NativeUi.row(this);
        load = NativeUi.button(this, R.string.load_models, false, view -> inspect(false));
        test = NativeUi.button(this, R.string.test_connection, false, view -> inspect(true));
        NativeUi.fill(actions, load);
        NativeUi.fill(actions, test);
        apiFields.addView(actions);
        status = NativeUi.text(this, 14, true);
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        service.addView(status);

        TextView instructions = addNote(page.body(), R.string.instructions);
        instructions.setPadding(0, 0, 0, NativeUi.dp(this, 12));
        TextView version = NativeUi.text(this, 13, true);
        try {
            version.setText(getString(R.string.about_version,
                    getPackageManager().getPackageInfo(getPackageName(), 0).versionName));
        } catch (android.content.pm.PackageManager.NameNotFoundException ignored) {
            version.setText(R.string.app_name);
        }
        page.body().addView(version);

        TextWatcher watcher = new TextWatcher() {
            public void beforeTextChanged(CharSequence text, int start, int count, int after) {}
            public void onTextChanged(CharSequence text, int start, int before, int count) { draftChanged(); }
            public void afterTextChanged(Editable text) {}
        };
        address.addTextChangedListener(new TextWatcher() {
            public void beforeTextChanged(CharSequence text, int start, int count, int after) {}
            public void onTextChanged(CharSequence text, int start, int before, int count) {
                if (key.length() != 0) {
                    key.setText("");
                }
                draftChanged();
            }
            public void afterTextChanged(Editable text) {}
        });
        model.addTextChangedListener(watcher);
        key.addTextChangedListener(watcher);
        clearKey.setOnCheckedChangeListener((button, checked) -> draftChanged());
        demo.setOnCheckedChangeListener((button, checked) -> draftChanged());
        draftChanged();
    }

    @Override
    protected void onSaveInstanceState(Bundle out) {
        super.onSaveInstanceState(out);
        out.putString("address", address.getText().toString());
        out.putString("model", model.getText().toString());
        out.putString("primary", primary.code);
        out.putString("other", other.code);
        out.putBoolean("demo", demo.isChecked());
        out.putBoolean("quick", quick.isChecked());
    }

    @Override
    protected void onStop() {
        if (request.running()) {
            draftChanged();
        }
        super.onStop();
    }

    private void inspect(boolean connectionTest) {
        Draft draft = readDraft(connectionTest);
        if (draft == null) {
            return;
        }
        load.setEnabled(false);
        test.setEnabled(false);
        status.setText(connectionTest ? R.string.testing_connection : R.string.loading_models);
        if (connectionTest) {
            request.start(() -> {
                TranslationClient.Profile profile = profile(draft);
                TranslationClient.translate(profile, profile.primary().sample);
                TranslationClient.translate(profile, profile.other().sample + "\n\n" + profile.other().sample);
                return true;
            }, ok -> {
                setIdle();
                status.setText(R.string.connection_ok);
            }, this::showFailure, 30000);
        } else {
            request.start(() -> TranslationClient.models(profile(draft)), models -> {
                setIdle();
                if (models.isEmpty()) {
                    status.setText(R.string.empty_models);
                } else {
                    status.setText("");
                    String[] items = models.toArray(new String[0]);
                    new AlertDialog.Builder(this).setTitle(R.string.api_model)
                            .setItems(items, (dialog, index) -> model.setText(items[index]))
                            .setNegativeButton(R.string.cancel, null).show();
                }
            }, this::showFailure, 15000);
        }
    }

    private TranslationClient.Profile profile(Draft draft) throws TranslationClient.TranslationFailure {
        return TranslationClient.resolveProfile(this, draft.previous, draft.address, draft.model,
                draft.key, draft.clearKey, draft.primary, draft.other);
    }

    private void save() {
        Draft draft = demo.isChecked()
                ? new Draft(saved, saved.address, saved.model, "", false, primary, other)
                : readDraft(true);
        if (draft == null) {
            return;
        }
        request.cancel();
        setIdle();
        try {
            if (saved.save(demo.isChecked(), quick.isChecked(), draft.address, draft.model,
                    primary, other, draft.key, draft.clearKey)) {
                saved = new TranslationSettings(this);
                key.setText("");
                clearKey.setChecked(false);
                draftChanged();
                Toast.makeText(this, R.string.saved, Toast.LENGTH_SHORT).show();
                return;
            }
        } catch (GeneralSecurityException | ProviderException ignored) {
            // The credentials are never written unencrypted.
        }
        status.setText(R.string.save_failed);
    }

    private Draft readDraft(boolean requireModel) {
        String endpoint;
        try {
            endpoint = TranslationSettings.normalizeAddress(this, address.getText().toString().trim());
        } catch (IllegalArgumentException exception) {
            address.setError(getString(R.string.invalid_address));
            address.requestFocus();
            return null;
        }
        String modelName = model.getText().toString().trim();
        if (requireModel && !TranslationSettings.validModel(modelName)) {
            model.setError(getString(R.string.invalid_model));
            model.requestFocus();
            return null;
        }
        String newKey = clearKey.isChecked() ? "" : key.getText().toString().trim();
        if (!TranslationSettings.validKey(newKey)) {
            key.setError(getString(R.string.invalid_key));
            key.requestFocus();
            return null;
        }
        return new Draft(saved, endpoint, modelName, newKey, clearKey.isChecked(), primary, other);
    }

    private void draftChanged() {
        request.cancel();
        status.setText("");
        setIdle();
        boolean useDemo = demo.isChecked();
        apiFields.setVisibility(useDemo ? View.GONE : View.VISIBLE);
        demoNote.setVisibility(useDemo ? View.VISIBLE : View.GONE);
        boolean hasMatchingKey = false;
        try {
            hasMatchingKey = saved.hasKey && saved.address.equals(
                    TranslationSettings.normalizeAddress(this, address.getText().toString().trim()));
        } catch (IllegalArgumentException ignored) {
            // An incomplete address cannot reference a stored credential.
        }
        clearKey.setVisibility(hasMatchingKey ? View.VISIBLE : View.GONE);
        if (!hasMatchingKey && clearKey.isChecked()) {
            clearKey.setChecked(false);
        }
        key.setEnabled(!clearKey.isChecked());
        key.setHint(hasMatchingKey ? getText(R.string.key_saved) : null);
        primaryButton.setText(primary.label);
        otherButton.setText(other.label);
    }

    private void setIdle() {
        load.setEnabled(true);
        test.setEnabled(true);
    }

    private void showFailure(TranslationClient.TranslationFailure failure) {
        setIdle();
        status.setText(failure.message(this));
        Toast.makeText(this, failure.message(this), Toast.LENGTH_LONG).show();
    }

    private Button languageRow(LinearLayout parent, int title, boolean first) {
        LinearLayout row = NativeUi.row(this);
        NativeUi.fill(row, NativeUi.title(this, title, 16));
        Button button = NativeUi.button(this, (first ? primary : other).label, false,
                view -> chooseLanguage(first));
        row.addView(button);
        parent.addView(row);
        return button;
    }

    private void chooseLanguage(boolean first) {
        Language[] options = Language.values();
        String[] names = new String[options.length];
        for (int index = 0; index < options.length; index++) {
            names[index] = getString(options[index].label);
        }
        new AlertDialog.Builder(this).setTitle(first ? R.string.primary_language : R.string.other_language)
                .setSingleChoiceItems(names, (first ? primary : other).ordinal(), (dialog, index) -> {
                    Language choice = options[index];
                    if (first) {
                        if (choice == other) {
                            other = primary;
                        }
                        primary = choice;
                    } else {
                        if (choice == primary) {
                            primary = other;
                        }
                        other = choice;
                    }
                    draftChanged();
                    dialog.dismiss();
                }).setNegativeButton(R.string.cancel, null).show();
    }

    private TextView addNote(LinearLayout parent, int label) {
        TextView note = NativeUi.text(this, 14, true);
        note.setText(label);
        parent.addView(note);
        return note;
    }

    private EditText addInput(LinearLayout parent, int label, int inputVariation) {
        EditText field = new EditText(this);
        field.setId(View.generateViewId());
        field.setSingleLine();
        field.setInputType(InputType.TYPE_CLASS_TEXT | inputVariation);
        field.setSaveEnabled(false);
        addNote(parent, label).setLabelFor(field.getId());
        parent.addView(field);
        return field;
    }

    private record Draft(TranslationSettings previous, String address, String model, String key,
                         boolean clearKey, Language primary, Language other) {}
}
