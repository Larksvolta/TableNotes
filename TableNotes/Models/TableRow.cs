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
    private string _col13 = string.Empty;
    private string _col14 = string.Empty;
    private string _col15 = string.Empty;
    private string _col16 = string.Empty;
    private string _col17 = string.Empty;
    private string _col18 = string.Empty;
    private string _col19 = string.Empty;
    private string _col20 = string.Empty;

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
    public string Col13 { get => _col13; set { if (_col13 != value) { _col13 = value; OnPropertyChanged(); } } }
    public string Col14 { get => _col14; set { if (_col14 != value) { _col14 = value; OnPropertyChanged(); } } }
    public string Col15 { get => _col15; set { if (_col15 != value) { _col15 = value; OnPropertyChanged(); } } }
    public string Col16 { get => _col16; set { if (_col16 != value) { _col16 = value; OnPropertyChanged(); } } }
    public string Col17 { get => _col17; set { if (_col17 != value) { _col17 = value; OnPropertyChanged(); } } }
    public string Col18 { get => _col18; set { if (_col18 != value) { _col18 = value; OnPropertyChanged(); } } }
    public string Col19 { get => _col19; set { if (_col19 != value) { _col19 = value; OnPropertyChanged(); } } }
    public string Col20 { get => _col20; set { if (_col20 != value) { _col20 = value; OnPropertyChanged(); } } }

    public event PropertyChangedEventHandler? PropertyChanged;    private void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
