// Ported from GideonZ/1541ultimate (GPL-3.0):
//   fpga/io/command_interface/vhdl_source/command_if_pkg.vhd
//   software/io/command_interface/command_intf.h
//   software/io/command_interface/command_intf.cc  (status strings)
// Original author: Gideon Zweijtzer. See NOTICE.

namespace sim6502.Systems.Ultimate;

/// <summary>
/// Ultimate Command Interface constants: C64-visible register addresses, status
/// and control bit masks, Ultimate-side handshake values, and buffer geometry.
/// </summary>
public static class UciConstants
{
    // ── C64-visible registers ──
    public const int BusIdAddress    = 0xDF1B; // read: SoftwareIEC bus ID
    public const int ControlAddress  = 0xDF1C; // read: status byte, write: control
    public const int CommandAddress  = 0xDF1D; // read: Identifier, write: command data
    public const int ResponseAddress = 0xDF1E; // read: response data
    public const int StatusAddress   = 0xDF1F; // read: status data

    /// <summary>Reading $DF1D returns this when a UCI is present.</summary>
    public const byte Identifier = 0xC9;

    // ── Control byte, written by the C64 to $DF1C ──
    public const byte ControlPushCommand = 0x01;
    public const byte ControlDataAccept  = 0x02;
    public const byte ControlAbort       = 0x04;
    public const byte ControlClearError  = 0x08;
    public const byte ControlIrqEnable   = 0x20;
    public const byte ControlTrigger     = 0x40;
    public const byte ControlDma         = 0x80;

    // ── Status byte, read by the C64 from $DF1C ──
    public const byte StatusResponseAvailable = 0x80;
    public const byte StatusStatusAvailable   = 0x40;
    public const byte StatusStateMask         = 0x30;
    public const byte StatusError             = 0x08;
    public const byte StatusAbortSet          = 0x04;
    public const byte StatusDataAcceptedSet   = 0x02;
    public const byte StatusNewCommandSet     = 0x01;

    // ── Protocol states, already shifted into bits 5-4 ──
    public const byte StateIdle     = 0x00;
    public const byte StateBusy     = 0x10;
    public const byte StateDataLast = 0x20;
    public const byte StateDataMore = 0x30;

    // ── Ultimate-side handshake-out values ──
    public const byte HandshakeReset      = 0x87;
    public const byte HandshakeAcceptCommand  = 0x01;
    public const byte HandshakeAcceptNextData = 0x02;
    public const byte HandshakeAcceptAbort    = 0x04;
    public const byte HandshakeValidateLast   = 0x10;
    public const byte HandshakeValidateMore   = 0x30;

    // ── Buffer geometry (command_if_pkg.vhd lines 33-41) ──
    public const int CommandBufferStart  = 0;
    public const int CommandBufferSize   = 896;
    public const int CommandBufferEnd    = CommandBufferStart + CommandBufferSize - 1;

    public const int ResponseBufferStart = 896;
    public const int ResponseBufferSize  = 896;
    public const int ResponseBufferEnd   = ResponseBufferStart + ResponseBufferSize - 1;

    public const int StatusBufferStart   = 1792;
    public const int StatusBufferSize    = 256;
    public const int StatusBufferEnd     = StatusBufferStart + StatusBufferSize - 1;

    public const int BackingStoreSize    = 2048;

    /// <summary>Low nibble of command byte 0 selects the target.</summary>
    public const byte TargetMask  = 0x0F;
    /// <summary>Bit 7 of command byte 0 suppresses the reply.</summary>
    public const byte NoReplyFlag = 0x80;
    public const int  MaxTarget   = 0x0F;

    // ── Status strings shared across targets (command_intf.cc lines 223-226) ──
    public const string StatusOk             = "00,OK";
    public const string StatusUnknownCommand = "21,UNKNOWN COMMAND";
    public const string StatusNotImplemented = "99,FUNCTION NOT IMPLEMENTED";
    public const string MessageNoTarget      = "NO TARGET";
    public const string StatusEmpty          = "";
}
