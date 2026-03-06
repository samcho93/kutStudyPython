using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using StudyPython.Models;
using StudyPython.ViewModels;

namespace StudyPython;

/// <summary>JavaScript ↔ WPF 통신 브릿지 (스크롤 시 예제 자동 선택)</summary>
[ComVisible(true)]
public class ScriptingBridge
{
    private readonly MainViewModel _vm;
    public ScriptingBridge(MainViewModel vm) => _vm = vm;

    public void NotifyExampleVisible(int index)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _vm.SelectExampleByIndex(index);
        });
    }
}

public partial class MainWindow : Window
{
    private IHighlightingDefinition? _pythonHighlighting;
    private bool _isUpdatingEditor;
    private EditorTabItem? _previousTab;

    public MainWindow()
    {
        InitializeComponent();

        LoadPythonSyntaxHighlighting();
        SetupEditor();

        var vm = (MainViewModel)DataContext;

        // JavaScript ↔ WPF 통신 브릿지 설정
        ContentBrowser.ObjectForScripting = new ScriptingBridge(vm);

        vm.NavigateHtmlAction = html =>
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // HTML을 임시 파일로 저장 후 Navigate → 로컬 file:// 이미지 표시 가능
                    var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                    Directory.CreateDirectory(tempDir);
                    var tempHtmlPath = Path.Combine(tempDir, "content.html");
                    File.WriteAllText(tempHtmlPath, html, System.Text.Encoding.UTF8);
                    ContentBrowser.Navigate(new Uri(tempHtmlPath));
                }
                catch { }
            });
        };
        vm.ScrollReplAction = () =>
        {
            Dispatcher.Invoke(() => ReplOutputBox.ScrollToEnd());
        };
        vm.ScrollEditorResultAction = () =>
        {
            Dispatcher.Invoke(() => EditorResultBox.ScrollToEnd());
        };

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void LoadPythonSyntaxHighlighting()
    {
        // 1차: WPF Resource (pack URI)
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/Python.xshd");
            var info = Application.GetResourceStream(uri);
            if (info != null)
            {
                using var reader = new XmlTextReader(info.Stream);
                _pythonHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
        }
        catch { }

        // 2차: 실행파일 옆 Resources 폴더
        if (_pythonHighlighting == null)
        {
            try
            {
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var xshdPath = Path.Combine(exeDir, "Resources", "Python.xshd");
                if (File.Exists(xshdPath))
                {
                    using var stream = File.OpenRead(xshdPath);
                    using var reader = new XmlTextReader(stream);
                    _pythonHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }
            catch { }
        }

        // 3차: 프로젝트 소스 폴더에서 직접 로드
        if (_pythonHighlighting == null)
        {
            try
            {
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                // bin\Debug\net8.0-windows 에서 3단계 위로 → 프로젝트 루트
                var projectDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
                var xshdPath = Path.Combine(projectDir, "Resources", "Python.xshd");
                if (File.Exists(xshdPath))
                {
                    using var stream = File.OpenRead(xshdPath);
                    using var reader = new XmlTextReader(stream);
                    _pythonHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }
            catch { }
        }
    }

    private void SetupEditor()
    {
        if (_pythonHighlighting != null)
            PythonEditor.SyntaxHighlighting = _pythonHighlighting;

        PythonEditor.Options.ConvertTabsToSpaces = true;
        PythonEditor.Options.IndentationSize = 4;
        PythonEditor.Options.ShowSpaces = false;
        PythonEditor.Options.ShowTabs = false;

        PythonEditor.TextChanged += PythonEditor_TextChanged;

        // 초기 탭의 코드 로드
        var vm = (MainViewModel)DataContext;
        if (vm.SelectedTab != null)
        {
            _previousTab = vm.SelectedTab;
            PythonEditor.Text = vm.SelectedTab.Code;
        }
    }

    private void PythonEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingEditor) return;
        var vm = (MainViewModel)DataContext;
        if (vm.SelectedTab != null)
        {
            vm.SelectedTab.Code = PythonEditor.Text;
        }
    }

    private void EditorTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PythonEditor == null) return;

        var vm = (MainViewModel)DataContext;
        if (vm.SelectedTab == null) return;
        if (vm.SelectedTab == _previousTab) return;

        _isUpdatingEditor = true;
        try
        {
            PythonEditor.Text = vm.SelectedTab.Code;
            _previousTab = vm.SelectedTab;

            // 하이라이트가 유실된 경우 재적용
            if (PythonEditor.SyntaxHighlighting == null && _pythonHighlighting != null)
                PythonEditor.SyntaxHighlighting = _pythonHighlighting;
        }
        finally
        {
            _isUpdatingEditor = false;
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        await vm.InitializeAsync();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.SaveProfile();
        vm.Cleanup();
    }

    private void CourseListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CourseListBox.SelectedItem is TreeNodeItem node)
        {
            var vm = (MainViewModel)DataContext;
            vm.OnCourseSelected(node);
        }
    }

    private void ReplInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var vm = (MainViewModel)DataContext;
            vm.ExecuteReplCommand();
            e.Handled = true;
        }
    }
}
