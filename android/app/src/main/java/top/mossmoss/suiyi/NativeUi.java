package top.mossmoss.suiyi;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.res.ColorStateList;
import android.content.res.Configuration;
import android.graphics.Insets;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.os.Build;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

final class NativeUi {
    record Page(LinearLayout header, LinearLayout body) {}

    static Page page(Activity activity) {
        int nightMode = activity.getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK;
        if (Build.VERSION.SDK_INT >= 27 && nightMode != Configuration.UI_MODE_NIGHT_YES) {
            View decor = activity.getWindow().getDecorView();
            decor.setSystemUiVisibility(decor.getSystemUiVisibility() | View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        }
        LinearLayout root = column(activity);
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                Insets padding = insets.getInsets(WindowInsets.Type.systemBars()
                        | WindowInsets.Type.displayCutout() | WindowInsets.Type.ime());
                view.setPadding(padding.left, padding.top, padding.right, padding.bottom);
            } else {
                view.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(),
                        insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
            }
            return insets;
        });
        LinearLayout header = row(activity);
        header.setPadding(dp(activity, 20), dp(activity, 6), dp(activity, 20), dp(activity, 6));
        root.addView(header);
        ScrollView scroll = new ScrollView(activity);
        scroll.setFillViewport(true);
        root.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        LinearLayout body = column(activity);
        body.setFocusableInTouchMode(true);
        body.setPadding(dp(activity, 20), dp(activity, 8), dp(activity, 20), dp(activity, 22));
        scroll.addView(body);
        activity.setContentView(root);
        body.requestFocus();
        return new Page(header, body);
    }

    static LinearLayout column(Context context) {
        LinearLayout view = new LinearLayout(context);
        view.setOrientation(LinearLayout.VERTICAL);
        return view;
    }

    static LinearLayout row(Context context) {
        LinearLayout view = new LinearLayout(context);
        view.setGravity(Gravity.CENTER_VERTICAL);
        // Let wrapped button labels grow on tall font scales.
        view.setBaselineAligned(false);
        return view;
    }

    static void fill(LinearLayout row, View view) {
        row.addView(view, new LinearLayout.LayoutParams(0, -2, 1));
    }

    static LinearLayout card(LinearLayout parent) {
        Context context = parent.getContext();
        LinearLayout card = column(context);
        card.setBackground(shape(context, R.color.surface, true));
        int padding = dp(context, 16);
        card.setPadding(padding, padding, padding, padding);
        LinearLayout.LayoutParams layout = new LinearLayout.LayoutParams(-1, -2);
        layout.bottomMargin = dp(context, 12);
        parent.addView(card, layout);
        return card;
    }

    static TextView text(Context context, int size, boolean subdued) {
        TextView view = new TextView(context);
        view.setTextSize(size);
        view.setTextColor(context.getColor(subdued ? R.color.muted : R.color.foreground));
        return view;
    }

    static TextView title(Context context, int label, int size) {
        TextView view = text(context, size, false);
        view.setText(label);
        view.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        return view;
    }

    static Button button(Context context, int label, boolean primary, View.OnClickListener click) {
        Button button = new Button(context);
        button.setAllCaps(false);
        button.setText(label);
        button.setMinHeight(dp(context, 48));
        button.setOnClickListener(click);
        int space = dp(context, 12);
        button.setPadding(space, 0, space, 0);
        button.setStateListAnimator(null);
        button.setBackground(new RippleDrawable(ColorStateList.valueOf(context.getColor(R.color.ripple)),
                primary ? shape(context, R.color.accent, false) : null,
                primary ? null : shape(context, R.color.surface, false)));
        button.setTextColor(new ColorStateList(
                new int[][] {{-android.R.attr.state_enabled}, {}},
                new int[] {context.getColor(R.color.muted),
                        context.getColor(primary ? R.color.on_accent : R.color.accent)}));
        return button;
    }

    static ImageButton iconButton(Context context, int icon, int label, View.OnClickListener click) {
        ImageButton button = new ImageButton(context);
        int size = dp(context, 48);
        button.setLayoutParams(new LinearLayout.LayoutParams(size, size));
        button.setImageResource(icon);
        button.setImageTintList(ColorStateList.valueOf(context.getColor(R.color.muted)));
        button.setScaleType(android.widget.ImageView.ScaleType.CENTER_INSIDE);
        int padding = dp(context, 12);
        button.setPadding(padding, padding, padding, padding);
        button.setBackground(new RippleDrawable(ColorStateList.valueOf(context.getColor(R.color.ripple)),
                null, shape(context, R.color.surface, false)));
        setIconLabel(button, label);
        button.setOnClickListener(click);
        return button;
    }

    static void setIconLabel(ImageButton button, int label) {
        CharSequence name = button.getContext().getText(label);
        button.setContentDescription(name);
        if (Build.VERSION.SDK_INT >= 26) {
            button.setTooltipText(name);
        }
    }

    static EditText editor(Context context, int hint, int minLines) {
        EditText field = new EditText(context);
        field.setId(View.generateViewId());
        field.setGravity(Gravity.TOP | Gravity.START);
        field.setInputType(android.text.InputType.TYPE_CLASS_TEXT
                | android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE
                | android.text.InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
        if (Build.VERSION.SDK_INT >= 26) {
            field.setImeOptions(android.view.inputmethod.EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING);
        }
        field.setMinLines(minLines);
        field.setMaxLines(9);
        field.setHint(hint);
        field.setTextSize(18);
        field.setSaveEnabled(false);
        field.setBackground(null);
        field.setPadding(0, dp(context, 6), 0, dp(context, 6));
        return field;
    }

    static void copy(Context context, CharSequence text) {
        ClipboardManager clipboard = context.getSystemService(ClipboardManager.class);
        clipboard.setPrimaryClip(ClipData.newPlainText(context.getString(R.string.result), text));
        if (Build.VERSION.SDK_INT < 33) {
            Toast.makeText(context, R.string.copied, Toast.LENGTH_SHORT).show();
        }
    }

    static int dp(Context context, int value) {
        return Math.round(value * context.getResources().getDisplayMetrics().density);
    }

    private static GradientDrawable shape(Context context, int color, boolean outline) {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(context.getColor(color));
        shape.setCornerRadius(dp(context, 16));
        if (outline) {
            shape.setStroke(dp(context, 1), context.getColor(R.color.outline));
        }
        return shape;
    }
}
