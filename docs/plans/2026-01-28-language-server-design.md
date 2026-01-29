# sim6502 Language Server Design

> **Status:** Approved design, ready for implementation

## Overview

A Language Server Protocol (LSP) implementation for the sim6502 testing DSL, providing IDE features for `.6502` files in any LSP-compatible editor.

## Goals

- Real-time diagnostics (syntax and semantic errors)
- Code completion for keywords, registers, symbols
- Hover information for symbols and keywords
- Go-to-definition for symbols (into `.sym` files and assembly source)
- Find references and document symbols
- VS Code extension as first client, but standard LSP for any editor

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     VS Code Extension                    │
│                   (sim6502-vscode/)                      │
│            Thin launcher - spawns LSP process            │
└─────────────────────┬───────────────────────────────────┘
                      │ stdin/stdout (JSON-RPC)
┌─────────────────────▼───────────────────────────────────┐
│                    LSP Server                            │
│                  (sim6502-lsp/)                          │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐  │
│  │ Document    │  │ Symbol       │  │ Diagnostics    │  │
│  │ Manager     │  │ Index        │  │ Provider       │  │
│  └──────┬──────┘  └──────┬───────┘  └───────┬────────┘  │
│         │                │                   │           │
│  ┌──────▼────────────────▼───────────────────▼────────┐ │
│  │              ANTLR Parser (from sim6502)           │ │
│  └────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

**Key decisions:**
- LSP server is a separate .NET process (not embedded)
- Reuses `sim6502Parser` and `sim6502Lexer` via project reference
- Document state kept in memory for fast incremental updates

## LSP Features

### Diagnostics
- Syntax errors from ANTLR parser
- Semantic errors: undefined symbols, invalid registers, bad addresses
- Warnings: deprecated `processor()` usage, unused symbols

### Completion
- Keywords: `suites`, `suite`, `test`, `assert`, `jsr`, `system`, `load`, `symbols`
- Registers: `a`, `x`, `y` and flags `c`, `n`, `z`, `d`, `v`
- System types: `c64`, `generic_6502`, `generic_6510`, `generic_65c02`
- Symbols from loaded `.sym` files

### Hover
- Keywords: brief description
- Symbols: address value and source location
- Addresses: hex/decimal display, ROM region info

### Go-to-Definition
- `[symbol]` → symbol definition in `.sym`, `.6502`, or `.asm` file
- `load("file.prg")` → opens file path

### Find References / Document Symbols
- Outline view: suites, tests, symbols
- Find all usages of a symbol

## Symbol Index

Three symbol sources:

**1. DSL-defined symbols** (`.6502` files):
```
[myvar] = $1000
```

**2. KickAssembler `.sym` files:**
```
.label screenRam=$0400
.label main=$0810
```

**3. Assembly source mapping:**
- Parse `.sym` for symbol → address
- Locate corresponding `.asm` file
- Scan for label definitions
- Cache file/line for go-to-definition

**Lifecycle:**
- Built incrementally as files open/edit
- Invalidated when `symbols()` paths change
- Workspace-wide visibility

## Project Structure

```
sim6502-lsp/
├── sim6502-lsp.csproj
├── Program.cs
├── Server/
│   ├── Sim6502LanguageServer.cs
│   ├── DocumentManager.cs
│   └── SymbolIndex.cs
├── Handlers/
│   ├── TextDocumentHandler.cs
│   ├── CompletionHandler.cs
│   ├── HoverHandler.cs
│   ├── DefinitionHandler.cs
│   └── DiagnosticsPublisher.cs
└── Parsing/
    ├── KickAssemblerSymbolParser.cs
    └── AssemblySourceLocator.cs

sim6502-vscode/
├── package.json
├── src/
│   └── extension.ts
├── syntaxes/
│   └── sim6502.tmLanguage.json
└── language-configuration.json
```

## Dependencies

**sim6502-lsp:**
- `OmniSharp.Extensions.LanguageServer`
- Project reference to `sim6502`

**sim6502-vscode:**
- `vscode-languageclient`

## Diagnostics Flow

```
Document Change
      │
      ▼
┌─────────────────┐
│ ANTLR Lexer     │ → Lexer errors
└────────┬────────┘
         ▼
┌─────────────────┐
│ ANTLR Parser    │ → Syntax errors
└────────┬────────┘
         ▼
┌─────────────────┐
│ Semantic        │ → Undefined symbols, type errors
│ Analyzer        │
└────────┬────────┘
         ▼
   Push to Editor
```

- Debounce: 300ms after last keystroke
- Reuse existing `sim6502/Errors/` infrastructure

## Testing Strategy

**Unit tests** (`sim6502-lsp-tests/`):
- Symbol parser tests
- Completion handler tests
- Diagnostic detection tests
- Go-to-definition tests

**Integration tests:**
- JSON-RPC request/response verification
- Full workflow tests

**Manual testing:**
- VS Code Extension Host (`F5`)
- Real `.6502` files from test suite

## Future Enhancements

- Additional assembler symbol formats (cc65, ACME, 64tass)
- Code actions (quick fixes for common errors)
- Rename symbol refactoring
- Workspace-wide find references
- Semantic highlighting
