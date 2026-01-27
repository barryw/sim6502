# DSL Enhancements for sim6502 Test Framework

> **For Claude:** Use superpowers:writing-plans to create implementation tasks from this design.

## Overview

Enhance the sim6502 test DSL based on real-world usage from the C64 chess project. Focus on reducing test boilerplate and improving debugging workflows.

## Feature Summary

| Feature | Priority | Description |
|---------|----------|-------------|
| `memfill(addr, count, value)` | High | Fill memory region with value |
| `setup {}` block | High | Suite-level setup runs before each test |
| `--filter`, `--test` | High | Run subset of tests by name pattern |
| `memdump(addr, count)` | Medium | Hex dump for debugging |
| `trace = true` | Medium | Execution trace on failure |
| `skip = true` | Medium | Skip individual tests |
| `tags = "a,b"` | Nice | Categorize tests |
| `timeout = N` | Nice | Cycle limit per test |

---

## Grammar Changes

### New Tokens
```antlr
SETUP: 'setup';
DEFAULT_TIMEOUT: 'default_timeout';
SKIP: 'skip';
TRACE: 'trace';
TIMEOUT: 'timeout';
TAGS: 'tags';
MEMFILL: 'memfill';
MEMDUMP: 'memdump';
```

### Suite-Level Elements
```antlr
suite_content
    : symbols_statement
    | load_statement
    | setup_block
    | default_timeout
    | test_function
    ;

setup_block
    : SETUP LBRACE statement* RBRACE
    ;

default_timeout
    : DEFAULT_TIMEOUT EQUALS number
    ;
```

### Extended Test Function
```antlr
test_function
    : TEST LPAREN STRING COMMA STRING (COMMA test_options)? RPAREN LBRACE statement* RBRACE
    ;

test_options
    : test_option (COMMA test_option)*
    ;

test_option
    : SKIP EQUALS bool_literal
    | TRACE EQUALS bool_literal
    | TIMEOUT EQUALS number
    | TAGS EQUALS STRING
    ;
```

### New Built-in Functions
```antlr
builtin_function
    : MEMFILL LPAREN expression COMMA expression COMMA expression RPAREN
    | MEMDUMP LPAREN expression COMMA expression RPAREN
    | /* existing... */
    ;
```

---

## CLI Changes

### New Options
```
--filter <pattern>      Glob pattern for test names (e.g., "castle*")
--test <name>           Run single test by exact name
--filter-tag <tags>     Comma-separated tags, OR logic
--exclude-tag <tags>    Exclude tests with these tags
--list                  List matching tests without running
```

### Filter Precedence
1. `--test` (exact match) takes priority
2. `--filter` (glob) narrows the set
3. `--filter-tag` further narrows to matching tags
4. `--exclude-tag` removes from final set

### Examples
```bash
sim6502 -s chess.6502 --filter "castle*"
sim6502 -s chess.6502 --test "attack-knight-center"
sim6502 -s chess.6502 --filter-tag "regression,smoke"
sim6502 -s chess.6502 --exclude-tag "slow"
sim6502 -s chess.6502 --filter "castle*" --list
```

---

## Behavior Specifications

### memfill(address, count, value)
Fills `count` bytes starting at `address` with `value`.
```
memfill([Board88], 128, $30)    ; Clear board with empty squares
memfill($0400, 1000, $20)       ; Fill screen with spaces
```

### memdump(address, count)
Always prints immediately when reached. Output format:
```
[memdump] $0E5A (Board88), 16 bytes:
0E5A: 30 B2 B3 B4 B5 B3 B2 B4  |  0....2..|
0E62: B1 B1 B1 B1 B1 B1 B1 B1  |  ........|
```
- Shows symbol name if address resolves to known symbol
- 8 bytes per line with ASCII sidebar
- Non-printable chars shown as `.`

### setup {} Block
Executes before each test in the suite:
```
suite("Chess Tests") {
  symbols("main.sym")
  load("main.prg", strip_header = true)

  setup {
    [whitekingsq] = $74
    [blackkingsq] = $04
    [currentplayer] = $01
  }

  test("my-test", "...") {
    ; Setup already ran, board is initialized
  }
}
```

### timeout (Cycles)
Suite-level default with per-test override:
```
suite("Chess Tests") {
  default_timeout = 1000000    ; 1M cycles default

  test("quick", "...") { }                      ; uses 1M
  test("slow", "...", timeout = 5000000) { }    ; 5M override
  test("debug", "...", timeout = 0) { }         ; disabled
}
```
- Unit is CPU cycles (deterministic across machines)
- `0` disables timeout
- Default if unspecified: no timeout (backward compatible)

### trace = true (Failure-Only)
Buffers execution trace, dumps only if test fails:
```
test("buggy", "Debug this", trace = true) {
  jsr([GenerateMove])
  assert(c == true, "Should set carry")
}
```

Output on failure:
```
FAILED: buggy - Should set carry
Expected: c == true, Got: c == false

Execution trace (247 instructions):
$1832: LDA $2157      A=$B6 X=$74 Y=$00 SP=$F7 NV-bdizc
$1835: AND #$7F       A=$36 X=$74 Y=$00 SP=$F7 nv-bdizc
$1837: CMP #$36       A=$36 X=$74 Y=$00 SP=$F7 nv-bdiZC
...
$18F2: CLC            A=$00 X=$02 Y=$03 SP=$F7 nv-bdizc  ; carry cleared
$18F3: RTS
```
- Flags uppercase when set, lowercase when clear
- Safe to leave enabled permanently (no output on pass)

### skip = true
```
test("not-ready", "WIP", skip = true) {
  ; Not executed, shown as skipped in results
}
```

### tags = "a,b"
Comma-separated string, OR filtering:
```
test("castle-ks", "...", tags = "castling,king") { }
test("castle-qs", "...", tags = "castling,queen") { }
```
```bash
--filter-tag "castling"        # runs both
--filter-tag "king"            # runs only castle-ks
--filter-tag "castling,queen"  # runs both (OR logic)
```

---

## Implementation Order

| Phase | Features | Rationale |
|-------|----------|-----------|
| 1 | `memfill`, `memdump` | Pure additions, no restructuring |
| 2 | `setup {}`, `default_timeout` | Suite-level changes |
| 3 | Test options (`skip`, `timeout`, `tags`, `trace`) | Extends test syntax |
| 4 | CLI filtering | Depends on tags being parsed |
| 5 | Trace buffering & output | Most complex, needs processor hook |

---

## Backward Compatibility

All changes are additive. Existing test files work unchanged.

---

## Testing Strategy

- Grammar tests for each new syntax element
- Listener tests for `memfill`/`memdump` behavior
- Integration tests for filtering (run subset, verify counts)
- Timeout test with intentional infinite loop
- Trace output test with forced failure
