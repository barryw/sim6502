// sim6502-lsp/Handlers/CompletionProvider.cs
namespace sim6502_lsp.Handlers;

public record CompletionItem(
    string Label,
    CompletionItemKind Kind,
    string? Detail = null,
    string? Documentation = null
);

public enum CompletionItemKind
{
    Keyword = 14,
    Variable = 6,
    Function = 3,
    Constant = 21,
    Enum = 13
}

public class CompletionProvider
{
    private static readonly List<CompletionItem> Keywords = new()
    {
        new("suites", CompletionItemKind.Keyword, "Top-level container for test suites"),
        new("suite", CompletionItemKind.Keyword, "Define a test suite", "suite(\"name\") { ... }"),
        new("test", CompletionItemKind.Keyword, "Define a test case", "test(\"name\", \"description\") { ... }"),
        new("setup", CompletionItemKind.Keyword, "Setup block run before each test"),
        new("assert", CompletionItemKind.Function, "Assert a condition", "assert(condition, \"message\")"),
        new("jsr", CompletionItemKind.Function, "Jump to subroutine", "jsr(address, stop_on_rts = true, fail_on_brk = false)"),
        new("load", CompletionItemKind.Function, "Load a binary file", "load(\"file.prg\")"),
        new("symbols", CompletionItemKind.Function, "Load symbol file", "symbols(\"file.sym\")"),
        new("system", CompletionItemKind.Keyword, "Set system type", "system(c64)"),
        new("processor", CompletionItemKind.Keyword, "Set processor type (deprecated)", "processor(6502)"),
        new("rom", CompletionItemKind.Function, "Load ROM file", "rom(\"name\", \"file.rom\")"),
    };

    private static readonly List<CompletionItem> Registers = new()
    {
        new("a", CompletionItemKind.Variable, "Accumulator register"),
        new("x", CompletionItemKind.Variable, "X index register"),
        new("y", CompletionItemKind.Variable, "Y index register"),
    };

    private static readonly List<CompletionItem> Flags = new()
    {
        new("c", CompletionItemKind.Variable, "Carry flag"),
        new("n", CompletionItemKind.Variable, "Negative flag"),
        new("z", CompletionItemKind.Variable, "Zero flag"),
        new("d", CompletionItemKind.Variable, "Decimal flag"),
        new("v", CompletionItemKind.Variable, "Overflow flag"),
    };

    private static readonly List<CompletionItem> SystemTypes = new()
    {
        new("c64", CompletionItemKind.Enum, "Commodore 64 with memory banking"),
        new("generic_6502", CompletionItemKind.Enum, "Generic 6502 with flat 64KB RAM"),
        new("generic_6510", CompletionItemKind.Enum, "Generic 6510 with $00/$01 I/O port"),
        new("generic_65c02", CompletionItemKind.Enum, "Generic 65C02 with extended opcodes"),
    };

    private static readonly List<CompletionItem> Functions = new()
    {
        new("peekbyte", CompletionItemKind.Function, "Read byte from memory", "peekbyte(address)"),
        new("peekword", CompletionItemKind.Function, "Read word from memory", "peekword(address)"),
        new("memcmp", CompletionItemKind.Function, "Compare memory regions", "memcmp(src, dest, size)"),
        new("memchk", CompletionItemKind.Function, "Check memory for value", "memchk(address, size, value)"),
        new("memfill", CompletionItemKind.Function, "Fill memory region", "memfill(address, size, value)"),
        new("memdump", CompletionItemKind.Function, "Dump memory region", "memdump(address, size)"),
    };

    private static readonly List<CompletionItem> TestOptions = new()
    {
        new("skip", CompletionItemKind.Keyword, "Skip this test"),
        new("trace", CompletionItemKind.Keyword, "Enable instruction tracing"),
        new("timeout", CompletionItemKind.Keyword, "Set timeout in cycles"),
        new("tags", CompletionItemKind.Keyword, "Add tags to test"),
    };

    private static readonly List<CompletionItem> JsrOptions = new()
    {
        new("stop_on_rts", CompletionItemKind.Keyword, "Stop when RTS encountered"),
        new("stop_on_address", CompletionItemKind.Keyword, "Stop at specific address"),
        new("fail_on_brk", CompletionItemKind.Keyword, "Fail if BRK encountered"),
    };

    public List<CompletionItem> GetCompletions(string content, int line, int character)
    {
        // For now, return all completions
        // TODO: Context-aware filtering based on cursor position
        var all = new List<CompletionItem>();
        all.AddRange(Keywords);
        all.AddRange(Registers);
        all.AddRange(Flags);
        all.AddRange(SystemTypes);
        all.AddRange(Functions);
        all.AddRange(TestOptions);
        all.AddRange(JsrOptions);
        return all;
    }
}
