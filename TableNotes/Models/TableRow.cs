using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TableNotes.Models;

public class TableRow : INotifyPropertyChanged
{
    private string _col1 = string.Empty;
    private string _col2 = string.Empty;
    private string _col3 = string.Empty;
    private string _col4 = string.Empty;
    private string _col5 = string.Empty;
    private string _col6 = string.Empty;
    private string _col7 = string.Empty;
    private string _col8 = string.Empty;
    private string _col9 = string.Empty;
    private string _col10 = string.Empty;
    private string _col11 = string.Empty;
    private string _col12 = string.Empty;

    public string Col1 { get => _col1; set { if (_col1 != value) { _col1 = value; OnPropertyChanged(); } } }
    public string Col2 { get => _col2; set { if (_col2 != value) { _col2 = value; OnPropertyChanged(); } } }
    public string Col3 { get => _col3; set { if (_col3 != value) { _col3 = value; OnPropertyChanged(); } } }
    public string Col4 { get => _col4; set { if (_col4 != value) { _col4 = value; OnPropertyChanged(); } } }
    public string Col5 { get => _col5; set { if (_col5 != value) { _col5 = value; OnPropertyChanged(); } } }
    public string Col6 { get => _col6; set { if (_col6 != value) { _col6 = value; OnPropertyChanged(); } } }
    public string Col7 { get => _col7; set { if (_col7 != value) { _col7 = value; OnPropertyChanged(); } } }
    public string Col8 { get => _col8; set { if (_col8 != value) { _col8 = value; OnPropertyChanged(); } } }
    public string Col9 { get => _col9; set { if (_col9 != value) { _col9 = value; OnPropertyChanged(); } } }
    public string Col10 { get => _col10; set { if (_col10 != value) { _col10 = value; OnPropertyChanged(); } } }
    public string Col11 { get => _col11; set { if (_col11 != value) { _col11 = value; OnPropertyChanged(); } } }
    public string Col12 { get => _col12; set { if (_col12 != value) { _col12 = value; OnPropertyChanged(); } } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
