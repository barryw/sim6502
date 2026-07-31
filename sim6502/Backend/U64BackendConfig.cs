namespace sim6502.Backend;

/// <summary>Configuration for the real Ultimate 64 hardware backend.</summary>
public class U64BackendConfig
{
    /// <summary>Hostname or IP of the Ultimate. Required.</summary>
    public string Host { get; set; } = "";

    /// <summary>HTTP port of the firmware's REST API.</summary>
    public int Port { get; set; } = 80;

    /// <summary>Timeout for a single HTTP round-trip.</summary>
    public int HttpTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// How long one UCI command may remain BUSY before the transaction gives up
    /// and runs its recovery sequence. Deliberately separate from
    /// <see cref="HttpTimeoutMs"/>: a command can legitimately stay busy for far
    /// longer than a single round-trip, and treating that as a wall-clock race is
    /// exactly what wedges the interface.
    ///
    /// Applies PER CONTINUATION PART, not per transaction: the wait timer
    /// restarts inside the wait for every "data more" part of a multi-part
    /// reply. A reply that uses the maximum number of continuation parts
    /// (U64Backend bounds this at 4096, mirroring UciRegisters) can therefore
    /// legitimately take up to CommandBudgetMs * 4096 before that guard gives
    /// up -- this value is per-part headroom, not a transaction-wide deadline.
    /// </summary>
    public int CommandBudgetMs { get; set; } = 30000;
}
