namespace sim6502.Backend;

public class ExecutionResult
{
    public bool ExitedCleanly { get; set; } = true;
    public StopReason Reason { get; set; } = StopReason.Rts;
    public long CyclesElapsed { get; set; }
    public int ProgramCounter { get; set; }
}

public enum StopReason
{
    Rts,
    Brk,
    StopAddress,
    Timeout
}
