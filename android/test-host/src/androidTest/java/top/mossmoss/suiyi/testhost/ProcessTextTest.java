package top.mossmoss.suiyi.testhost;

import android.app.Instrumentation;
import android.app.UiAutomation;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.Resources;
import android.os.Bundle;
import android.os.SystemClock;
import android.provider.Settings;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.ViewConfiguration;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;
import android.widget.TextView;

import androidx.test.platform.app.InstrumentationRegistry;

import org.json.JSONArray;
import org.junit.After;
import org.junit.Assume;
import org.junit.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

public final class ProcessTextTest {
    private static final String APP = "top.mossmoss.suiyi";
    private static final String SOURCE = "开头 | Hello | 结尾";
    private static final String TRANSLATED = "开头 | 你好 | 结尾";
    private final Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
    private final UiAutomation ui = instrumentation.getUiAutomation();
    private HostActivity host;

    @After
    public void closeHost() {
        if (host != null) {
            instrumentation.runOnMainSync(host::finish);
        }
    }

    @Test
    public void nativePreviewCancelAndReplace() {
        startHost(false);
        selectNativeWord(host.editor, SOURCE.indexOf("Hello") + 2);
        chooseSuiyi();
        assertNotNull(waitFor("你好"));
        click(label("cancel"));
        assertEquals(SOURCE, read(host.editor));

        selectNativeWord(host.editor, SOURCE.indexOf("Hello") + 2);
        chooseSuiyi();
        AccessibilityNodeInfo output = waitFor("你好");
        assertNotNull(output);
        click(label("show_source"));
        click(label("hide_source"));
        setText(output, "您好");
        click(label("replace"));
        assertEquals("开头 | 您好 | 结尾", read(host.editor));
    }

    @Test
    public void nativeQuickReplace() {
        startHost(true);
        selectNativeWord(host.editor, SOURCE.indexOf("Hello") + 2);
        chooseSuiyi();
        assertNotNull(waitFor(TRANSLATED));
        assertEquals(TRANSLATED, read(host.editor));
        configureDemo(false);
    }

    @Test
    public void readonlyCopiesEvenInQuickMode() {
        startHost(true);
        selectNativeWord(host.readonly, 2);
        chooseSuiyi();
        assertNotNull(waitFor("你好"));
        click(label("copy"));
        assertEquals("Hello", read(host.readonly));
        AtomicReference<CharSequence> copied = new AtomicReference<>();
        instrumentation.runOnMainSync(() -> {
            ClipboardManager clipboard = host.getSystemService(ClipboardManager.class);
            copied.set(clipboard.getPrimaryClip().getItemAt(0).getText());
        });
        assertEquals("你好", copied.get().toString());
        configureDemo(false);
    }

    @Test
    public void webViewReplacesOnlySelectedWord() throws Exception {
        startHost(false);
        String coordinates = javascript("(() => { const e = document.querySelector('#editor'); "
                + "const r = e.getBoundingClientRect(); const c = document.createElement('canvas'); "
                + "const ctx = c.getContext('2d'); ctx.font = '20px monospace'; "
                + "return [(r.left + 4 + ctx.measureText('START | Hel').width) * devicePixelRatio, "
                + "(r.top + r.height / 2) * devicePixelRatio]; })()");
        JSONArray point = new JSONArray(coordinates);
        int[] location = new int[2];
        instrumentation.runOnMainSync(() -> host.web.getLocationOnScreen(location));
        longPress(location[0] + (float) point.getDouble(0), location[1] + (float) point.getDouble(1));
        chooseSuiyi();
        click(label("replace"));
        read(host.editor); // Wait for the host to regain focus after the Activity result.
        assertEquals("\"START | 你好 | END\"", javascript("document.querySelector('#editor').value"));
    }

    @Test
    public void sharedTextStaysInDraftUntilTranslateIsTapped() {
        configureDemo(false);
        Intent intent = new Intent(Intent.ACTION_SEND).setType("text/plain")
                .putExtra(Intent.EXTRA_TEXT, "Hello");
        openMain(intent);
        assertNotNull(waitFor("Hello"));
        assertNull(find("你好"));
        assertNull(find(label("copy")));
        click(label("translate"));
        assertNotNull(waitFor("你好"));
        assertNotNull(waitFor(label("copy")));
    }

    @Test
    public void apiSuccessFailureAndLateResponse() {
        String endpoint = InstrumentationRegistry.getArguments().getString("fixtureEndpoint");
        Assume.assumeTrue("Set fixtureEndpoint to use the local translation fixture", endpoint != null);
        try {
            openSettings();
            setSwitch(label("demo_enabled"), false);
            setSwitch(label("quick_mode"), true);
            setPrimaryJapanese();
            List<AccessibilityNodeInfo> fields = new ArrayList<>();
            collectTextFields(ui.getRootInActiveWindow(), fields);
            assertEquals(3, fields.size());
            setText(fields.get(0), endpoint);
            setText(fields.get(2), "fixture-test-key");
            clickWithScroll(label("load_models"));
            click("mock");
            clickWithScroll(label("test_connection"));
            assertNotNull(waitFor(label("connection_ok")));
            click(label("save"));
            closeSettings();
            launchHost();

            selectFixtureWord("你好");
            assertNotNull(waitFor("开头 | こんにちは、世界。 | 结尾"));

            selectFixtureWord("失败");
            assertNotNull(waitFor(label("api_rate")));
            click(label("close"));
            assertEquals("开头 | 失败 | 结尾", read(host.editor));

            selectFixtureWord("慢");
            assertNotNull(waitFor(label("translating")));
            instrumentation.getTargetContext().startActivity(new Intent(Settings.ACTION_SETTINGS)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            SystemClock.sleep(2300);
            pressBack();
            assertEquals("开头 | 慢 | 结尾", read(host.editor));
        } finally {
            configureDemo(false);
        }
    }

    private void selectFixtureWord(String word) {
        String input = "开头 | " + word + " | 结尾";
        instrumentation.runOnMainSync(() -> host.editor.setText(input));
        assertNotNull(waitFor(input));
        selectNativeWord(host.editor, input.indexOf(word) + (word.length() / 2));
        chooseSuiyi();
    }

    private void startHost(boolean quick) {
        configureDemo(quick);
        launchHost();
    }

    private void launchHost() {
        Intent intent = new Intent(instrumentation.getTargetContext(), HostActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        host = (HostActivity) instrumentation.startActivitySync(intent);
        instrumentation.runOnMainSync(() -> {
            host.editor.setText(SOURCE);
            host.readonly.setText("Hello");
        });
        assertNotNull(waitFor(SOURCE));
    }

    private void configureDemo(boolean quick) {
        openSettings();
        setSwitch(label("demo_enabled"), true);
        setSwitch(label("quick_mode"), quick);
        click(label("save"));
        closeSettings();
    }

    private void openSettings() {
        openMain(new Intent(Intent.ACTION_MAIN));
        click(label("settings"));
    }

    private void openMain(Intent intent) {
        intent.setClassName(APP, APP + ".TranslationActivity")
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        instrumentation.getTargetContext().startActivity(intent);
    }

    private void closeSettings() {
        click(label("back"));
        assertNotNull(waitFor(label("source_hint")));
        pressBack();
    }

    private String label(String name) {
        try {
            Resources resources = instrumentation.getTargetContext().getPackageManager()
                    .getResourcesForApplication(APP);
            return resources.getString(resources.getIdentifier(name, "string", APP));
        } catch (PackageManager.NameNotFoundException exception) {
            throw new AssertionError("Install Suiyi before running the host tests", exception);
        }
    }

    private void pressBack() {
        instrumentation.sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);
    }

    private static void collectTextFields(AccessibilityNodeInfo node,
                                          List<AccessibilityNodeInfo> fields) {
        if (node == null) {
            return;
        }
        if ("android.widget.EditText".contentEquals(node.getClassName())) {
            fields.add(node);
        }
        for (int index = 0; index < node.getChildCount(); index++) {
            collectTextFields(node.getChild(index), fields);
        }
    }

    private void setText(AccessibilityNodeInfo field, String text) {
        Bundle args = new Bundle();
        args.putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, text);
        assertTrue(field.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args));
    }

    private void setSwitch(String label, boolean checked) {
        AccessibilityNodeInfo node = waitFor(label);
        assertNotNull(label, node);
        if (node.isChecked() != checked) {
            assertTrue(node.performAction(AccessibilityNodeInfo.ACTION_CLICK));
        }
    }

    private void setPrimaryJapanese() {
        AccessibilityNodeInfo primary = waitFor(label("primary_language"));
        assertNotNull(primary);
        AccessibilityNodeInfo row = primary.getParent();
        for (int index = 0; index < row.getChildCount(); index++) {
            AccessibilityNodeInfo button = row.getChild(index);
            if (button != null && "android.widget.Button".contentEquals(button.getClassName())) {
                String japanese = label("language_ja");
                if (!japanese.contentEquals(button.getText())) {
                    assertTrue(button.performAction(AccessibilityNodeInfo.ACTION_CLICK));
                    click(japanese);
                }
                return;
            }
        }
        throw new AssertionError("Primary language selector missing");
    }

    private String javascript(String code) throws InterruptedException {
        ArrayBlockingQueue<String> value = new ArrayBlockingQueue<>(1);
        instrumentation.runOnMainSync(() -> host.web.evaluateJavascript(code, value::offer));
        String result = value.poll(5, TimeUnit.SECONDS);
        assertNotNull("WebView result", result);
        return result;
    }

    private String read(TextView view) {
        AtomicReference<String> value = new AtomicReference<>();
        long deadline = SystemClock.uptimeMillis() + 5000;
        do {
            instrumentation.runOnMainSync(() -> {
                if (host.hasWindowFocus()) {
                    value.set(view.getText().toString());
                }
            });
            if (value.get() != null) {
                return value.get();
            }
            SystemClock.sleep(50);
        } while (SystemClock.uptimeMillis() < deadline);
        throw new AssertionError("Test host did not regain focus");
    }

    private void selectNativeWord(TextView view, int index) {
        int[] location = new int[2];
        float[] point = new float[2];
        instrumentation.runOnMainSync(() -> {
            view.getLocationOnScreen(location);
            point[0] = view.getTotalPaddingLeft() + view.getLayout().getPrimaryHorizontal(index);
            point[1] = view.getTotalPaddingTop() + view.getLayout().getLineBottom(0) / 2f;
        });
        longPress(location[0] + point[0], location[1] + point[1]);
    }

    private void longPress(float x, float y) {
        long down = SystemClock.uptimeMillis();
        injectTouch(down, MotionEvent.ACTION_DOWN, x, y);
        SystemClock.sleep(ViewConfiguration.getLongPressTimeout() + 150);
        injectTouch(down, MotionEvent.ACTION_UP, x, y);
    }

    private void injectTouch(long down, int action, float x, float y) {
        MotionEvent event = MotionEvent.obtain(down, SystemClock.uptimeMillis(), action, x, y, 0);
        event.setSource(InputDevice.SOURCE_TOUCHSCREEN);
        try {
            assertTrue(ui.injectInputEvent(event, true));
        } finally {
            event.recycle();
        }
    }

    private void chooseSuiyi() {
        String entry = label("app_name");
        long deadline = SystemClock.uptimeMillis() + 5000;
        do {
            AccessibilityNodeInfo action = find(entry);
            if (action != null) {
                assertTrue(action.performAction(AccessibilityNodeInfo.ACTION_CLICK));
                return;
            }
            AccessibilityNodeInfo more = find("More options");
            if (more == null) {
                more = find("更多选项");
            }
            if (more != null && more.performAction(AccessibilityNodeInfo.ACTION_CLICK)) {
                click(entry);
                return;
            }
            SystemClock.sleep(50);
        } while (SystemClock.uptimeMillis() < deadline);
        throw new AssertionError("随译 selection action did not appear");
    }

    private void click(String label) {
        AccessibilityNodeInfo node = waitFor(label);
        assertNotNull(label, node);
        assertTrue(label, node.performAction(AccessibilityNodeInfo.ACTION_CLICK));
    }

    private void clickWithScroll(String label) {
        if (find(label) == null) {
            AccessibilityNodeInfo scroll = scrollView(ui.getRootInActiveWindow());
            assertNotNull("Expected a scrollable settings page", scroll);
            assertTrue(scroll.performAction(AccessibilityNodeInfo.ACTION_SCROLL_FORWARD));
        }
        click(label);
    }

    private AccessibilityNodeInfo scrollView(AccessibilityNodeInfo node) {
        if (node == null) {
            return null;
        }
        if (node.isScrollable()) {
            return node;
        }
        for (int index = 0; index < node.getChildCount(); index++) {
            AccessibilityNodeInfo match = scrollView(node.getChild(index));
            if (match != null) {
                return match;
            }
        }
        return null;
    }

    private AccessibilityNodeInfo waitFor(String label) {
        long deadline = SystemClock.uptimeMillis() + 5000;
        do {
            AccessibilityNodeInfo node = find(label);
            if (node != null) {
                return node;
            }
            SystemClock.sleep(50);
        } while (SystemClock.uptimeMillis() < deadline);
        return null;
    }

    private AccessibilityNodeInfo find(String label) {
        android.accessibilityservice.AccessibilityServiceInfo info = ui.getServiceInfo();
        int flags = android.accessibilityservice.AccessibilityServiceInfo.FLAG_RETRIEVE_INTERACTIVE_WINDOWS;
        if ((info.flags & flags) == 0) {
            info.flags |= flags;
            ui.setServiceInfo(info);
        }
        for (AccessibilityWindowInfo window : ui.getWindows()) {
            AccessibilityNodeInfo node = find(window.getRoot(), label);
            if (node != null) {
                return node;
            }
        }
        return find(ui.getRootInActiveWindow(), label);
    }

    private AccessibilityNodeInfo find(AccessibilityNodeInfo node, String label) {
        if (node == null) {
            return null;
        }
        if (node.isVisibleToUser() && (label.contentEquals(String.valueOf(node.getText()))
                || label.contentEquals(String.valueOf(node.getContentDescription())))) {
            return node;
        }
        for (int index = 0; index < node.getChildCount(); index++) {
            AccessibilityNodeInfo child = find(node.getChild(index), label);
            if (child != null) {
                return child;
            }
        }
        return null;
    }
}
