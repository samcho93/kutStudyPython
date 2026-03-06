using System.Collections.ObjectModel;

namespace StudyPython.Models;

public class CourseItem
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int LessonNumber { get; set; }
}

public class CourseContent
{
    public string HtmlContent { get; set; } = string.Empty;
    public List<CodeExample> CodeExamples { get; set; } = new();
}

public class CodeExample
{
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class TreeNodeItem
{
    public string Name { get; set; } = string.Empty;
    public object? Tag { get; set; }
    public ObservableCollection<TreeNodeItem> Children { get; set; } = new();
    public bool IsExpanded { get; set; }
}

public class EditorTabItem : System.ComponentModel.INotifyPropertyChanged
{
    private string _title = "새 파일";
    private string _code = string.Empty;
    private string _filePath = string.Empty;
    private bool _isModified;

    public string Title
    {
        get => _isModified ? _title + " *" : _title;
        set { _title = value; OnPropertyChanged(nameof(Title)); }
    }

    public string Code
    {
        get => _code;
        set { _code = value; _isModified = true; OnPropertyChanged(nameof(Code)); OnPropertyChanged(nameof(Title)); }
    }

    /// <summary>초기 로딩 시 Modified 플래그 없이 코드 설정</summary>
    public void SetCodeDirect(string code)
    {
        _code = code;
        OnPropertyChanged(nameof(Code));
    }

    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; OnPropertyChanged(nameof(FilePath)); }
    }

    public bool IsModified
    {
        get => _isModified;
        set { _isModified = value; OnPropertyChanged(nameof(IsModified)); OnPropertyChanged(nameof(Title)); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
