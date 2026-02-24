using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DriveFlip.Localization;
using DriveFlip.Models;

namespace DriveFlip.ViewModels;

public enum PhaseStatus { Pending, Active, Done }

public class WipePhaseItem : INotifyPropertyChanged
{
    public string Name { get; }
    public string Keyword { get; }

    private PhaseStatus _status = PhaseStatus.Pending;
    public PhaseStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDone)); OnPropertyChanged(nameof(IsActive)); }
    }

    public bool IsDone => _status == PhaseStatus.Done;
    public bool IsActive => _status == PhaseStatus.Active;

    public WipePhaseItem(string name, string keyword) { Name = name; Keyword = keyword; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class DriveItemViewModel : INotifyPropertyChanged
{
    public PhysicalDrive Drive { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isProtected) { _isSelected = false; OnPropertyChanged(); return; }
            _isSelected = value; OnPropertyChanged();
        }
    }

    private bool _isProtected;
    public bool IsProtected
    {
        get => _isProtected;
        set { _isProtected = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSelectable)); }
    }

    public bool IsSelectable => !IsProtected;

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



    private TimeSpan _timeRemaining;
    public TimeSpan TimeRemaining
    {
        get => _timeRemaining;
        set { _timeRemaining = value; OnPropertyChanged(); }
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

    private bool _isQueryingHealth;
    public bool IsQueryingHealth
    {
        get => _isQueryingHealth;
        set { _isQueryingHealth = value; OnPropertyChanged(); }
    }

    private DriveRiskLevel _riskLevel = DriveRiskLevel.Unknown;
    public DriveRiskLevel RiskLevel
    {
        get => _riskLevel;
        set { _riskLevel = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRiskLevel)); }
    }

    public bool HasRiskLevel => _riskLevel != DriveRiskLevel.Unknown;

    // ── Operation Phases ──
    public ObservableCollection<WipePhaseItem> Phases { get; } = new();

    public void InitPhases(bool isSmartWipe, bool isSurfaceCheck, bool verifyAfterWipe, bool isCheckAndWipe = false)
    {
        Phases.Clear();

        if (isCheckAndWipe)
            Phases.Add(new WipePhaseItem(Loc.Get("PhaseSurfaceCheck"), "sampl"));

        if (isSurfaceCheck && !isCheckAndWipe)
        {
            Phases.Add(new WipePhaseItem(Loc.Get("PhaseSurfaceCheck"), "sampl"));
        }
        else if (!isSurfaceCheck || isCheckAndWipe)
        {
            if (isSmartWipe)
            {
                Phases.Add(new WipePhaseItem(Loc.Get("PhaseHeadWipe"), "head"));
                Phases.Add(new WipePhaseItem(Loc.Get("PhaseTailWipe"), "tail"));
                Phases.Add(new WipePhaseItem(Loc.Get("PhaseScatterWipe"), "scatter"));
            }
            else
            {
                Phases.Add(new WipePhaseItem(Loc.Get("PhaseFullWipe"), "wip"));
            }
            if (verifyAfterWipe)
                Phases.Add(new WipePhaseItem(Loc.Get("PhaseVerification"), "verif"));
        }

        if (Phases.Count > 0)
            Phases[0].Status = PhaseStatus.Active;
    }

    public void MarkAllPhasesDone()
    {
        foreach (var phase in Phases)
            phase.Status = PhaseStatus.Done;
    }

    public void MarkSurfaceCheckDone()
    {
        foreach (var phase in Phases)
        {
            if (phase.Keyword == "sampl")
            {
                phase.Status = PhaseStatus.Done;
                break;
            }
        }
    }

    public void UpdatePhases(string statusMessage)
    {
        var status = statusMessage.ToLowerInvariant();
        WipePhaseItem? matched = null;

        // Find which phase matches the current status
        for (int i = Phases.Count - 1; i >= 0; i--)
        {
            if (status.Contains(Phases[i].Keyword))
            {
                matched = Phases[i];
                break;
            }
        }

        if (matched == null) return;

        bool passedMatched = false;
        foreach (var phase in Phases)
        {
            if (phase == matched)
            {
                phase.Status = PhaseStatus.Active;
                passedMatched = true;
            }
            else if (!passedMatched)
            {
                phase.Status = PhaseStatus.Done;
            }
            else
            {
                phase.Status = PhaseStatus.Pending;
            }
        }
    }

    // ── Size Indicator ──
    private double _sizePercent;
    public double SizePercent
    {
        get => _sizePercent;
        set { _sizePercent = value; OnPropertyChanged(); }
    }

    // ── Wipe Visualization ──
    private const int VizSegments = 200;
    private static readonly byte[] UnscrubColor = [0x2E, 0x1A, 0x1A, 0xFF]; // #1A1A2E (BGRA)
    private static readonly byte[] ScrubColor = [0xC4, 0x8A, 0x4A, 0xFF];   // #4A8AC4 (BGRA)

    private bool[] _vizState = new bool[VizSegments];
    private WriteableBitmap? _vizBitmap;
    private int _headBoundary;
    private int _tailBoundary;
    private bool _isSmartWipe;
    private bool _isSurfaceCheck;
    private Random? _vizRandom;
    private double _lastVizProgress;

    private BitmapSource? _visualizationBitmap;
    public BitmapSource? VisualizationBitmap
    {
        get => _visualizationBitmap;
        private set { _visualizationBitmap = value; OnPropertyChanged(); }
    }

    public void InitVisualization(int headBoundary, int tailBoundary, bool isSmartWipe, bool isSurfaceCheck)
    {
        _vizState = new bool[VizSegments];
        _headBoundary = Math.Clamp(headBoundary, 1, VizSegments / 3);
        _tailBoundary = Math.Clamp(tailBoundary, 1, VizSegments / 3);
        _isSmartWipe = isSmartWipe;
        _isSurfaceCheck = isSurfaceCheck;
        _vizRandom = new Random(42); // seeded for consistent look
        _lastVizProgress = 0;

        _vizBitmap = new WriteableBitmap(VizSegments, 1, 96, 96, PixelFormats.Bgra32, null);
        RenderVisualization();
    }

    public void UpdateVisualization(double progress, string statusMessage)
    {
        if (_vizBitmap == null) return;

        UpdatePhases(statusMessage);
        var status = statusMessage.ToLowerInvariant();

        if (_isSurfaceCheck)
        {
            // Surface check: random segments across full bar
            int targetFilled = (int)(progress / 100.0 * VizSegments);
            int currentFilled = 0;
            for (int i = 0; i < VizSegments; i++)
                if (_vizState[i]) currentFilled++;

            while (currentFilled < targetFilled)
            {
                int idx = _vizRandom!.Next(VizSegments);
                if (!_vizState[idx])
                {
                    _vizState[idx] = true;
                    currentFilled++;
                }
            }
        }
        else if (!_isSmartWipe)
        {
            // Full wipe: linear left-to-right
            int targetSeg = (int)(progress / 100.0 * VizSegments);
            for (int i = 0; i < Math.Min(targetSeg, VizSegments); i++)
                _vizState[i] = true;
        }
        else
        {
            // Smart wipe: head → tail → scatter phases
            if (status.Contains("head"))
            {
                // Head phase: fills segments 0..headBoundary, progress ~0-25%
                double phaseProgress = Math.Min(progress / 25.0, 1.0);
                int targetSeg = (int)(phaseProgress * _headBoundary);
                for (int i = 0; i < Math.Min(targetSeg, _headBoundary); i++)
                    _vizState[i] = true;
            }
            else if (status.Contains("tail"))
            {
                // Fill head fully
                for (int i = 0; i < _headBoundary; i++)
                    _vizState[i] = true;

                // Tail phase: fills from right, progress ~25-50%
                double phaseProgress = Math.Clamp((progress - 25.0) / 25.0, 0, 1);
                int targetSeg = (int)(phaseProgress * _tailBoundary);
                int tailStart = VizSegments - _tailBoundary;
                for (int i = 0; i < Math.Min(targetSeg, _tailBoundary); i++)
                    _vizState[tailStart + (_tailBoundary - 1 - i)] = true;
            }
            else if (status.Contains("scatter") || status.Contains("random"))
            {
                // Fill head + tail fully
                for (int i = 0; i < _headBoundary; i++)
                    _vizState[i] = true;
                for (int i = VizSegments - _tailBoundary; i < VizSegments; i++)
                    _vizState[i] = true;

                // Scatter phase: random in middle, progress ~50-100%
                double phaseProgress = Math.Clamp((progress - 50.0) / 50.0, 0, 1);
                int middleStart = _headBoundary;
                int middleEnd = VizSegments - _tailBoundary;
                int middleLen = middleEnd - middleStart;
                if (middleLen > 0)
                {
                    int targetFilled = (int)(phaseProgress * middleLen);
                    int currentFilled = 0;
                    for (int i = middleStart; i < middleEnd; i++)
                        if (_vizState[i]) currentFilled++;

                    while (currentFilled < targetFilled)
                    {
                        int idx = middleStart + _vizRandom!.Next(middleLen);
                        if (!_vizState[idx])
                        {
                            _vizState[idx] = true;
                            currentFilled++;
                        }
                    }
                }
            }
            else if (status.Contains("verif"))
            {
                // During verification, fill everything
                for (int i = 0; i < VizSegments; i++)
                    _vizState[i] = true;
            }
            else
            {
                // Generic fallback — linear fill
                int targetSeg = (int)(progress / 100.0 * VizSegments);
                for (int i = 0; i < Math.Min(targetSeg, VizSegments); i++)
                    _vizState[i] = true;
            }
        }

        _lastVizProgress = progress;
        RenderVisualization();
    }

    private void RenderVisualization()
    {
        if (_vizBitmap == null) return;

        var pixels = new byte[VizSegments * 4];
        for (int i = 0; i < VizSegments; i++)
        {
            var color = _vizState[i] ? ScrubColor : UnscrubColor;
            int offset = i * 4;
            pixels[offset] = color[0];     // B
            pixels[offset + 1] = color[1]; // G
            pixels[offset + 2] = color[2]; // R
            pixels[offset + 3] = color[3]; // A
        }

        _vizBitmap.WritePixels(new Int32Rect(0, 0, VizSegments, 1), pixels, VizSegments * 4, 0);
        VisualizationBitmap = _vizBitmap;
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

        TimeRemaining = TimeSpan.Zero;
        ErrorCount = 0;
        DataPresencePercent = 0;
        IsComplete = false;
        Passed = false;
        ReportText = "";
        _vizState = new bool[VizSegments];
        _vizBitmap = null;
        VisualizationBitmap = null;
        Phases.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
