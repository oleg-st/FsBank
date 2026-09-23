using FsBank.Scanner.Game;
using FsBank.Scanner.Ocr;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Scanning;

internal enum ScanMode { Auto, Manual }
internal enum ScanPhase { Ready, Waiting, Scanning, Finishing, Completed, Cancelled, Failed }
internal enum AreaPhase { Queued, Waiting, Scanning, Recognizing, Completed, NeedsReview, Cancelled }

internal sealed record ScanOptions(ScanMode Mode, bool Equipped, bool Inventory, bool Bank, bool AllBankTabs)
{
    public ScanTarget[] Targets => Mode == ScanMode.Manual ? [ScanTarget.Equipped] :
        new[] { ScanTarget.Equipped, ScanTarget.Inventory, ScanTarget.Bank }.Where(target => target switch
        { ScanTarget.Equipped => Equipped, ScanTarget.Inventory => Inventory, _ => Bank }).ToArray();
}

internal sealed record ScanIssue(ScanTarget Area, int? Tab, int Row, int Column, string Reason);
internal sealed record AreaProgress(ScanTarget Area, AreaPhase Phase, int Items, int Pending, int Issues, int? Tab, bool AllTabs);
internal sealed record ScanSnapshot(long Revision, ScanMode Mode, ScanPhase Phase, AreaProgress[] Areas,
    ScanTarget? Current, string Message, ScanIssue[] Issues, Rectangle? GameBounds)
{
    public int Items => Areas.Sum(area => area.Items);
    public int Pending => Areas.Sum(area => area.Pending);
}

// Both UI surfaces consume the same immutable snapshot. OCR callbacks may arrive out of order.
internal sealed class ScanProgressTracker(ScanOptions options, Action<ScanSnapshot> changed)
{
    private sealed class Area(ScanTarget target)
    {
        public ScanTarget Target { get; } = target;
        public AreaPhase Phase;
        public int Items, Pending, Issues;
        public int? Tab;
        public bool CaptureComplete;
    }
    private readonly object gate = new();
    private readonly Area[] areas = options.Targets.Select(target => new Area(target)).ToArray();
    private readonly Dictionary<string, (ScanTarget Target, int? Tab)> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> completed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ScanIssue> issues = [];
    private ScanPhase phase = ScanPhase.Ready;
    private ScanTarget? current;
    private string message = "Ready to scan.";
    private Rectangle? bounds;
    private long revision;

    public ScanSnapshot Snapshot { get { lock (gate) return CreateSnapshot(); } }
    private ScanSnapshot CreateSnapshot() => new(revision, options.Mode, phase,
        areas.Select(area => new AreaProgress(area.Target, area.Phase, area.Items, area.Pending, area.Issues,
            area.Tab, area.Target == ScanTarget.Bank && options.AllBankTabs)).ToArray(), current, message, issues.ToArray(), bounds);
    private void Change(Action update)
    {
        ScanSnapshot snapshot;
        lock (gate) { update(); revision++; snapshot = CreateSnapshot(); }
        changed(snapshot);
    }
    private Area Find(ScanTarget target) => areas.Single(area => area.Target == target);
    public void SetBounds(Rectangle client) => Change(() => bounds = client);
    public void Waiting(ScanTarget target, string instruction) => SetPhase(target, ScanPhase.Waiting, AreaPhase.Waiting, instruction);
    public void Scanning(ScanTarget target, string instruction = "") => SetPhase(target, ScanPhase.Scanning, AreaPhase.Scanning, instruction);
    private void SetPhase(ScanTarget target, ScanPhase next, AreaPhase areaPhase, string instruction)
    {
        lock (gate)
            if (phase == next && current == target && message == instruction && Find(target).Phase == areaPhase) return;
        Change(() => { phase = next; current = target; message = instruction; Find(target).Phase = areaPhase; });
    }
    public void RegisterSession(string session, ScanTarget target, int? tab) => Change(() =>
    {
        sessions[Path.GetFullPath(session)] = (target, tab);
        if (target == current) Find(target).Tab = tab;
    });
    public void Queued(string file) => Change(() =>
    {
        if (queued.Add(Path.GetFullPath(file)) && sessions.TryGetValue(Path.GetDirectoryName(Path.GetFullPath(file))!, out var source))
            Find(source.Target).Pending++;
    });
    public void Recognized(string file, ItemRecognition.Result? result, Exception? error) => Change(() =>
    {
        file = Path.GetFullPath(file);
        if (!completed.Add(file) || !sessions.TryGetValue(Path.GetDirectoryName(file)!, out var source)) return;
        var area = Find(source.Target);
        if (queued.Contains(file)) area.Pending = Math.Max(0, area.Pending - 1);
        string? problem = error?.Message ?? result?.Issue;
        if (error is null && result is not null && problem is null) area.Items++;
        else
        {
            var cell = Regex.Match(Path.GetFileName(file), @"^r(\d+)_c(\d+)\.png$");
            AddIssue(area, source.Tab, cell.Success ? int.Parse(cell.Groups[1].Value) : 0,
                cell.Success ? int.Parse(cell.Groups[2].Value) : 0, problem ?? "Recognition produced no result.");
        }
        FinishArea(area);
    });
    public void CaptureFailed(ScanTarget target, int? tab, int row, int column, string reason) =>
        Change(() => AddIssue(Find(target), tab, row, column, reason));
    private void AddIssue(Area area, int? tab, int row, int column, string reason)
    {
        issues.Add(new(area.Target, tab, row, column, reason)); area.Issues++;
    }
    public void CaptureComplete(ScanTarget target) => Change(() =>
    {
        var area = Find(target); area.CaptureComplete = true;
        area.Phase = AreaPhase.Recognizing; FinishArea(area);
    });
    private static void FinishArea(Area area)
    {
        if (area.CaptureComplete && area.Pending == 0)
            area.Phase = area.Issues == 0 ? AreaPhase.Completed : AreaPhase.NeedsReview;
    }
    public void Finishing(string instruction = "Finishing recognition and saving results. You can use the mouse.") =>
        Change(() => { phase = ScanPhase.Finishing; message = instruction; });
    public void Finish(ScanPhase outcome, string description) => Change(() =>
    {
        phase = outcome; message = description;
        foreach (var area in areas)
        {
            FinishArea(area);
            if (!area.CaptureComplete && area.Phase != AreaPhase.Queued) area.Phase = AreaPhase.Cancelled;
        }
        current = null;
    });
}
