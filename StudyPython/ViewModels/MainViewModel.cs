using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using StudyPython.Models;
using StudyPython.Services;

namespace StudyPython.ViewModels;

public class MainViewModel : BaseViewModel
{
    private string _pythonPath = "python";
    private Process? _replProcess;

    // === 학생 정보 ===
    private string _department = string.Empty;
    private string _studentId = string.Empty;
    private string _grade = string.Empty;
    private string _name = string.Empty;
    private string _company = string.Empty;
    private string _score = string.Empty;
    private string _workingFolder = string.Empty;

    public string Department { get => _department; set { SetProperty(ref _department, value); SaveProfile(); } }
    public string StudentId { get => _studentId; set { SetProperty(ref _studentId, value); SaveProfile(); } }
    public string Grade { get => _grade; set { SetProperty(ref _grade, value); SaveProfile(); } }
    public string Name { get => _name; set { SetProperty(ref _name, value); SaveProfile(); } }
    public string Company { get => _company; set { SetProperty(ref _company, value); SaveProfile(); } }
    public string Score { get => _score; set => SetProperty(ref _score, value); }
    public string WorkingFolder { get => _workingFolder; set { SetProperty(ref _workingFolder, value); SaveProfile(); TutorialService.SetImageFolder(value); } }

    // === 강좌 ===
    private ObservableCollection<TreeNodeItem> _courseTree = new();
    private CourseContent? _currentContent;
    private ObservableCollection<CodeExample> _codeExamples = new();
    private CodeExample? _selectedExample;
    private string _selectedExampleCode = string.Empty;
    private string _exampleResult = string.Empty;

    public ObservableCollection<TreeNodeItem> CourseTree { get => _courseTree; set => SetProperty(ref _courseTree, value); }
    public ObservableCollection<CodeExample> CodeExamples { get => _codeExamples; set => SetProperty(ref _codeExamples, value); }
    public CodeExample? SelectedExample
    {
        get => _selectedExample;
        set
        {
            SetProperty(ref _selectedExample, value);
            SelectedExampleCode = value?.Code ?? string.Empty;
        }
    }
    public string SelectedExampleCode { get => _selectedExampleCode; set => SetProperty(ref _selectedExampleCode, value); }
    public string ExampleResult { get => _exampleResult; set => SetProperty(ref _exampleResult, value); }

    // === 에디터 ===
    private ObservableCollection<EditorTabItem> _editorTabs = new();
    private EditorTabItem? _selectedTab;
    private string _editorResult = string.Empty;
    private CancellationTokenSource? _runCts;

    public ObservableCollection<EditorTabItem> EditorTabs { get => _editorTabs; set => SetProperty(ref _editorTabs, value); }
    public EditorTabItem? SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }
    public string EditorResult { get => _editorResult; set => SetProperty(ref _editorResult, value); }

    // === REPL ===
    private string _replInput = string.Empty;
    private string _replOutput = string.Empty;

    public string ReplInput { get => _replInput; set => SetProperty(ref _replInput, value); }
    public string ReplOutput { get => _replOutput; set => SetProperty(ref _replOutput, value); }

    // === 상태 ===
    private string _statusText = "준비";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    // === Actions (View에서 설정) ===
    public Action<string>? NavigateHtmlAction { get; set; }
    public Action? ScrollReplAction { get; set; }
    public Action? ScrollEditorResultAction { get; set; }

    // === Commands ===
    public RelayCommand SelectWorkFolderCommand { get; }
    public AsyncRelayCommand RunExampleCommand { get; }
    public RelayCommand NewFileCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand SaveFileCommand { get; }
    public AsyncRelayCommand RunCodeCommand { get; }
    public RelayCommand StopCodeCommand { get; }
    public AsyncRelayCommand ExportPdfCommand { get; }
    public AsyncRelayCommand SubmitCommand { get; }
    public RelayCommand CloseTabCommand { get; }
    public RelayCommand ClearResultCommand { get; }

    public MainViewModel()
    {
        SelectWorkFolderCommand = new RelayCommand(SelectWorkFolder);
        RunExampleCommand = new AsyncRelayCommand(RunExample);
        NewFileCommand = new RelayCommand(NewFile);
        OpenFileCommand = new RelayCommand(OpenFile);
        SaveFileCommand = new RelayCommand(SaveFile);
        RunCodeCommand = new AsyncRelayCommand(RunCode);
        StopCodeCommand = new RelayCommand(StopCode);
        ExportPdfCommand = new AsyncRelayCommand(ExportPdf);
        SubmitCommand = new AsyncRelayCommand(Submit);
        CloseTabCommand = new RelayCommand(p => CloseTab(p as EditorTabItem));
        ClearResultCommand = new RelayCommand(() => EditorResult = string.Empty);

        // 기본 탭 추가
        var defaultTab = new EditorTabItem { Title = "새 파일", IsModified = false };
        EditorTabs.Add(defaultTab);
        SelectedTab = defaultTab;
    }

    public async Task InitializeAsync()
    {
        // 사용자 프로필 로드
        LoadProfile();

        // Python 경로 감지
        _pythonPath = PythonExecutionService.DetectPythonPath();

        // 강좌 목록 로드
        StatusText = "강좌 목록 로딩 중...";
        try
        {
            CourseTree = await TutorialService.GetCourseListAsync();
            StatusText = "준비";
        }
        catch (Exception ex)
        {
            StatusText = $"강좌 로딩 오류: {ex.Message}";
        }

        // REPL 시작
        StartRepl();
    }

    // === 프로필 관리 ===
    private void LoadProfile()
    {
        var profile = UserProfileService.Load();
        _department = profile.Department;
        _studentId = profile.StudentId;
        _grade = profile.Grade;
        _name = profile.Name;
        _company = profile.Company;
        _score = profile.Score;
        _workingFolder = profile.WorkingFolder;

        OnPropertyChanged(nameof(Department));
        OnPropertyChanged(nameof(StudentId));
        OnPropertyChanged(nameof(Grade));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Company));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(WorkingFolder));
        TutorialService.SetImageFolder(_workingFolder);
    }

    public void SaveProfile()
    {
        UserProfileService.Save(new UserProfile
        {
            Department = _department,
            StudentId = _studentId,
            Grade = _grade,
            Name = _name,
            Company = _company,
            Score = _score,
            WorkingFolder = _workingFolder
        });
    }

    // === 강좌 선택 ===
    public async void OnCourseSelected(TreeNodeItem node)
    {
        if (node.Tag is CourseItem course)
        {
            StatusText = $"'{course.Title}' 로딩 중...";
            try
            {
                _currentContent = await TutorialService.GetCourseContentAsync(course);

                NavigateHtmlAction?.Invoke(_currentContent.HtmlContent);

                CodeExamples = new ObservableCollection<CodeExample>(_currentContent.CodeExamples);
                if (CodeExamples.Count > 0)
                    SelectedExample = CodeExamples[0];
                else
                    SelectedExampleCode = string.Empty;

                ExampleResult = string.Empty;
                StatusText = "준비";
            }
            catch (Exception ex)
            {
                StatusText = $"로딩 오류: {ex.Message}";
            }
        }
    }

    /// <summary>스크롤 시 예제 콤보박스 자동 선택 (JavaScript에서 호출)</summary>
    public void SelectExampleByIndex(int index)
    {
        if (index >= 0 && index < CodeExamples.Count)
            SelectedExample = CodeExamples[index];
    }

    // === 예제 실행 ===
    private async Task RunExample()
    {
        if (SelectedExample == null || string.IsNullOrWhiteSpace(SelectedExample.Code))
        {
            ExampleResult = "실행할 예제가 없습니다.";
            return;
        }

        StatusText = "예제 실행 중...";
        ExampleResult = "실행 중...";

        var (output, error, exitCode) = await PythonExecutionService.RunCodeAsync(
            SelectedExample.Code, _pythonPath, CancellationToken.None, _workingFolder);

        ExampleResult = exitCode == 0
            ? output
            : $"[오류]\n{error}\n\n{output}";

        StatusText = "준비";
    }

    // === 파일 관리 ===
    private void NewFile()
    {
        var tab = new EditorTabItem { Title = "새 파일", IsModified = false };
        EditorTabs.Add(tab);
        SelectedTab = tab;
    }

    private void OpenFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Python 파일 (*.py)|*.py|모든 파일 (*.*)|*.*",
            InitialDirectory = string.IsNullOrEmpty(_workingFolder) ? "" : _workingFolder,
            Multiselect = true
        };

        if (dlg.ShowDialog() == true)
        {
            foreach (var filePath in dlg.FileNames)
            {
                try
                {
                    var code = File.ReadAllText(filePath);
                    var tab = new EditorTabItem
                    {
                        Title = Path.GetFileName(filePath),
                        FilePath = filePath,
                    };
                    tab.SetCodeDirect(code);
                    tab.IsModified = false;
                    EditorTabs.Add(tab);
                    SelectedTab = tab;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"파일 열기 오류: {ex.Message}\n{filePath}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void SaveFile()
    {
        if (SelectedTab == null) return;

        if (string.IsNullOrEmpty(SelectedTab.FilePath))
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Python 파일 (*.py)|*.py|모든 파일 (*.*)|*.*",
                InitialDirectory = string.IsNullOrEmpty(_workingFolder) ? "" : _workingFolder,
                FileName = SelectedTab.Title.Replace(" *", "")
            };

            if (dlg.ShowDialog() == true)
            {
                SelectedTab.FilePath = dlg.FileName;
                SelectedTab.Title = Path.GetFileName(dlg.FileName);
            }
            else return;
        }

        try
        {
            File.WriteAllText(SelectedTab.FilePath, SelectedTab.Code);
            SelectedTab.IsModified = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseTab(EditorTabItem? tab)
    {
        if (tab == null) return;

        if (tab.IsModified)
        {
            var result = MessageBox.Show("변경사항을 저장하시겠습니까?", "확인",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes)
            {
                SelectedTab = tab;
                SaveFile();
            }
        }

        EditorTabs.Remove(tab);
        if (EditorTabs.Count == 0)
            NewFile();
    }

    // === 코드 실행 ===
    private readonly object _outputLock = new();
    private System.Text.StringBuilder _outputBuffer = new();
    private bool _flushScheduled;

    private void AppendOutputBuffered(string text)
    {
        lock (_outputLock)
        {
            _outputBuffer.AppendLine(text);
            if (!_flushScheduled)
            {
                _flushScheduled = true;
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    () => FlushOutput());
            }
        }
    }

    private void FlushOutput()
    {
        string pending;
        lock (_outputLock)
        {
            pending = _outputBuffer.ToString();
            _outputBuffer.Clear();
            _flushScheduled = false;
        }
        if (!string.IsNullOrEmpty(pending))
        {
            EditorResult += pending;
            ScrollEditorResultAction?.Invoke();
        }
    }

    private async Task RunCode()
    {
        if (SelectedTab == null || string.IsNullOrWhiteSpace(SelectedTab.Code))
        {
            EditorResult = "실행할 코드가 없습니다.";
            return;
        }

        StatusText = "코드 실행 중...";
        var tabTitle = SelectedTab.Title.Replace(" *", "");
        if (EditorTabs.Count > 1)
            EditorResult += $"--- [{tabTitle}] ---\n";

        _runCts = new CancellationTokenSource();

        try
        {
            var exitCode = await PythonExecutionService.RunCodeStreamingAsync(
                SelectedTab.Code, _pythonPath,
                output => AppendOutputBuffered(output),
                error => AppendOutputBuffered($"[오류] {error}"),
                _runCts.Token, _workingFolder);

            // 남은 버퍼 플러시
            Application.Current.Dispatcher.Invoke(() => FlushOutput());

            if (exitCode != 0)
                EditorResult += "\n";
        }
        catch (OperationCanceledException)
        {
            Application.Current.Dispatcher.Invoke(() => FlushOutput());
            EditorResult += "[실행 취소됨]\n";
        }

        EditorResult += "\n";
        ScrollEditorResultAction?.Invoke();
        StatusText = "준비";
    }

    private void StopCode()
    {
        _runCts?.Cancel();
        PythonExecutionService.StopRunningProcess();
        EditorResult += "[정지됨]\n";
        StatusText = "준비";
    }

    // === 작업폴더 ===
    private void SelectWorkFolder()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "작업폴더를 선택하세요",
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrEmpty(_workingFolder) && Directory.Exists(_workingFolder))
            dlg.SelectedPath = _workingFolder;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            WorkingFolder = dlg.SelectedPath;
        }
    }

    // === 프로필 생성 헬퍼 ===
    private UserProfile CreateProfile() => new UserProfile
    {
        Department = _department,
        StudentId = _studentId,
        Grade = _grade,
        Name = _name,
        Company = _company,
        Score = _score,
        WorkingFolder = _workingFolder
    };

    /// <summary>모든 탭의 코드와 타이틀을 수집 (코드가 있는 탭만)</summary>
    private List<(string Title, string Code)> CollectAllTabCodes()
    {
        var items = new List<(string Title, string Code)>();
        foreach (var tab in EditorTabs)
        {
            if (!string.IsNullOrWhiteSpace(tab.Code))
            {
                var title = tab.Title.Replace(" *", "");
                items.Add((title, tab.Code));
            }
        }
        return items;
    }

    // === PDF 출력 ===
    private async Task ExportPdf()
    {
        var tabCodes = CollectAllTabCodes();
        if (tabCodes.Count == 0)
        {
            MessageBox.Show("출력할 코드가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusText = "PDF 출력 중...";

        try
        {
            var profile = CreateProfile();
            var outputFolder = string.IsNullOrEmpty(_workingFolder) ? null : _workingFolder;
            var pdfPath = await Task.Run(() =>
                PdfExportService.Export(profile, tabCodes, _editorResult, outputFolder));

            MessageBox.Show($"PDF 출력 완료!\n{pdfPath}", "출력 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText = "준비";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF 출력 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "준비";
        }
    }

    // === Git 제출 ===
    private async Task Submit()
    {
        var tabCodes = CollectAllTabCodes();
        if (tabCodes.Count == 0)
        {
            MessageBox.Show("제출할 코드가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusText = "제출 준비 중...";

        try
        {
            var profile = CreateProfile();
            var outputFolder = string.IsNullOrEmpty(_workingFolder) ? null : _workingFolder;
            var pdfPath = await Task.Run(() =>
                PdfExportService.Export(profile, tabCodes, _editorResult, outputFolder));

            // Git Push
            var result = await GitSubmitService.SubmitAsync(pdfPath, progress =>
            {
                Application.Current.Dispatcher.Invoke(() => StatusText = progress);
            });

            MessageBox.Show(result, "제출 결과", MessageBoxButton.OK,
                result.Contains("성공") ? MessageBoxImage.Information : MessageBoxImage.Warning);

            StatusText = "준비";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"제출 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "준비";
        }
    }

    // === REPL ===
    private void StartRepl()
    {
        ReplOutput = "Python REPL 시작 중...\n";

        _replProcess = PythonExecutionService.StartReplProcess(_pythonPath, output =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ReplOutput += output + "\n";
                ScrollReplAction?.Invoke();
            });
        });

        if (_replProcess == null)
        {
            ReplOutput += "[오류] Python 프로세스를 시작할 수 없습니다.\n";
        }
    }

    public void ExecuteReplCommand()
    {
        if (string.IsNullOrEmpty(ReplInput)) return;

        ReplOutput += $">>> {ReplInput}\n";
        PythonExecutionService.SendReplInput(ReplInput);
        ReplInput = string.Empty;
        ScrollReplAction?.Invoke();
    }

    public void Cleanup()
    {
        PythonExecutionService.StopRunningProcess();
        PythonExecutionService.StopReplProcess();
    }
}
