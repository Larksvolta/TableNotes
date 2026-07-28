using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TableNotes.Models;

public class TableNote : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    private DateTime _createdAt = DateTime.Now;
    public DateTime CreatedAt
    {
        get => _createdAt;
        set
        {
            if (_createdAt != value)
            {
                _createdAt = value;
                OnPropertyChanged();
            }
        }
    }

    private DateTime _modifiedAt = DateTime.Now;
    public DateTime ModifiedAt
    {
        get => _modifiedAt;
        set
        {
            if (_modifiedAt != value)
            {
                _modifiedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public string FileName { get; set; } = string.Empty;

    private string _category = "General";
    public string Category
    {
        get => _category;
        set
        {
            if (_category != value)
            {
                _category = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlaceholder { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
