using System.ComponentModel;
using System.Runtime.CompilerServices;
using DriveFlip.Models;

namespace DriveFlip.ViewModels;

public class DriveItemViewModel : INotifyPropertyChanged
{
    public PhysicalDrive Drive { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Drive.IsSystemDrive) { _isSelected = false; OnPropertyChanged(); return; }
            _isSelected = value; OnPropertyChanged();
        }
    }

    /// <summary>True when the checkbox should be enabled (not a system drive).</summary>
    public bool IsSelectable => !Drive.IsSystemDrive;

    private bool _isInfoSelected;
    public bool IsInfoSelected
    {
        get => _isInfoSelected;
        set { _isInfoSelected = value; OnPropertyChanged(); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _speedText = "";
    public string SpeedText
    {
        get => _speedText;
        set { _speedText = value; OnPropertyChanged(); }
    }

    private string _timeRemainingText = "";
    public string TimeRemainingText
    {
        get => _timeRemainingText;
        set { _timeRemainingText = value; OnPropertyChanged(); }
    }

    private long _errorCount;
    public long ErrorCount
    {
        get => _errorCount;
        set { _errorCount = value; OnPropertyChanged(); }
    }

    private double _dataPresencePercent;
    public double DataPresencePercent
    {
        get => _dataPresencePercent;
        set { _dataPresencePercent = value; OnPropertyChanged(); }
    }

    private bool _isComplete;
    public bool IsComplete
    {
        get => _isComplete;
        set { _isComplete = value; OnPropertyChanged(); }
    }

    private bool _passed;
    public bool Passed
    {
        get => _passed;
        set { _passed = value; OnPropertyChanged(); }
    }

    private string _reportText = "";
    public string ReportText
    {
        get => _reportText;
        set { _reportText = value; OnPropertyChanged(); }
    }

    public DriveItemViewModel(PhysicalDrive drive)
    {
        Drive = drive;
    }

    public void Reset()
    {
        IsRunning = false;
        Progress = 0;
        StatusText = "";
        SpeedText = "";
        TimeRemainingText = "";
        ErrorCount = 0;
        DataPresencePercent = 0;
        IsComplete = false;
        Passed = false;
        ReportText = "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
