namespace sim6502.Backend;

/// <summary>
/// A backend that can carry Ultimate Command Interface traffic, whether the UCI
/// is simulated in-process or reached over the wire on real hardware.
///
/// This is deliberately narrow: it is exactly what the DSL's uci(), uci_status()
/// and uci_data() need, and nothing more. Widening it would couple the grammar to
/// backend internals that only one implementation has.
/// </summary>
public interface IUltimateBackend
{
    /// <summary>
    /// Run one UCI command to completion, walking every continuation part, and
    /// return the reply payload and status string.
    /// </summary>
    (string Status, byte[] Data) IssueUciCommand(byte[] command);
}
