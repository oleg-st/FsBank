namespace FsBank.Scanner.Scanning;

internal static class ScanConstants
{
    public const int ActivationTimeoutMs = 1800;
    public const int ActivationPollMs = 30;
    public const int ActivationSettleMs = 180;
    public const int DetailsKeyTimeoutMs = 250;
    public const int DetailsKeyPollMs = 10;
    public const int CursorTolerancePx = 5;
    public const int MouseIdleBeforeRetryMs = 300;
    public const int SaveQueueCapacity = 8;
    public const int InitialParkMs = 140;
    public const int TooltipClearTimeoutMs = 1200;
    public const int InitialHoverDelayMs = 10;
    public const int TooltipTimeoutMs = 1600;
    public const int FirstFullSearchMs = 400;
    public const int FullSearchIntervalMs = 350;
    public const int FailedTooltipParkMs = 120;
    public const int TabHoverSettleMs = 150;
    public const int TabClickHoldMs = 100;
    public const int TabReleaseSettleMs = 200;
    public const int TabRetryAfterMs = 1000;
    public const int TabClickAttempts = 3;
    public const int TabSwitchTimeoutMs = 4000;
    public const int TabSettleMs = 350;
    public const int TabStabilityPollMs = 70;
    public const int TabStableFrames = 3;
}
