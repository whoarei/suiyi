namespace TranslationFloat.TestTarget;
public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
        InputBox.TextChanged += (_, _) => Count.Text = $"当前字符数：{InputBox.Text.Length}";
        Loaded += (_, _) => InputBox.Focus();
    }
}
