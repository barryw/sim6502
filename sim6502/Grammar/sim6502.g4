/*
Copyright (c) 2020 Barry Walker. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation
and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

grammar sim6502;

@header {
namespace sim6502.Grammar.Generated;
}

suites
    : Suites LBrace (suite)* RBrace
    ;

suite
    : Suite LParen suiteName RParen LBrace
        (systemDeclaration | processorDeclaration)?
        (testFunction | symbolsFunction | loadFunction | romDeclaration | setupBlock)+
      RBrace
    ;
    
suiteName
    : StringLiteral
    ;

processorDeclaration
    : Processor LParen processorTypeValue RParen
    ;

processorTypeValue
    : ProcessorType6502
    | ProcessorType6510
    | ProcessorType65C02
    ;

// System declaration (takes precedence over processor)
systemDeclaration
    : System LParen systemTypeValue RParen
    ;

systemTypeValue
    : SystemC64
    | SystemGeneric6502
    | SystemGeneric6510
    | SystemGeneric65C02
    ;

romDeclaration
    : Rom LParen romName Comma romFilename RParen
    ;

romName
    : StringLiteral
    ;

romFilename
    : StringLiteral
    ;

// Assignment rule - order matters: most specific first, general expression last
assignment
    : Register Assign expression        # registerAssignment
    | ProcessorFlag Assign expression   # flagAssignment
    | symbolRef Assign Register         # symbolRegisterAssignment
    | symbolRef Assign expression       # symbolAssignment
    | address Assign Register           # addressRegisterAssignment
    | address Assign expression         # addressAssignment
    | expression Assign expression      # expressionAssignment
    ;
    
address
    : number        # numberAddress
    | symbolRef     # symbolAddress
    ;
    
number
    : Hex       # hexNumber
    | Int       # intNumber
    | Binary    # binaryNumber
    ;
    
boolean
    : Boolean
    ;
    
assertFunction
    : Assert LParen comparison Comma assertDescription RParen
    ;
    
assertDescription
    : StringLiteral
    ;
    
comparison
    : compareLHS CompareOperator expression     # compareExpression
    | memoryChkFunction                         # memoryChk
    | memoryCmpFunction                         # memoryCmp
    ;
    
compareLHS
    : Register                  # registerCompare
    | ProcessorFlag             # flagCompare
    | address                   # addressCompare
    | Cycles                    # cyclesCompare
    | expression (byteWord)?    # expressionCompare
    ;

jsrFunction
    : JSR LParen address stopOn failOnBreak RParen
    ;
    
stopOn
    : Comma StopOnAddress Assign address 
    | Comma StopOnRTS Assign boolean
    ;
    
failOnBreak
    : Comma FailOnBRK Assign boolean
    ;
        
symbolsFunction
    : Symbols LParen symbolsFilename RParen
    ;
    
symbolsFilename
    : StringLiteral
    ;
    
loadFunction
    : Load LParen loadFilename (loadAddress)? (stripHeader)? RParen
    ;
    
loadFilename
    : StringLiteral
    ;
    
loadAddress
    : Comma Address Assign address
    ;
    
stripHeader
    : Comma StripHeader Assign boolean
    ;
    
testFunction
    : Test LParen testName Comma testDescription (Comma testOptions)? RParen LBrace testContents+ RBrace
    ;

testName
    : StringLiteral
    ;

testDescription
    : StringLiteral
    ;

testOptions
    : testOption (Comma testOption)*
    ;

testOption
    : Skip Assign boolean
    | Trace Assign boolean
    | Timeout Assign number
    | Tags Assign StringLiteral
    ;

setupBlock
    : Setup LBrace setupContents+ RBrace
    ;
    
// Test contents - allow any single statement, testFunction uses + to allow multiple
testContents
    : assertFunction
    | assignment
    | jsrFunction
    | memFillFunction
    | memDumpFunction
    ;

// Setup contents - similar to test contents but without assertions
setupContents
    : assignment
    | jsrFunction
    | memFillFunction
    | memDumpFunction
    ;

peekByteFunction
    : PeekByte LParen expression RParen
    ;
    
peekWordFunction
    : PeekWord LParen expression RParen
    ;
    
memoryCmpFunction
    : MemoryCmp LParen sourceAddress Comma targetAddress Comma memorySize RParen
    ;
    
memoryChkFunction
    : MemoryChk LParen sourceAddress Comma memorySize Comma memoryValue RParen
    ;

memFillFunction
    : MemFill LParen expression Comma expression Comma expression RParen
    ;

memDumpFunction
    : MemDump LParen expression Comma expression RParen
    ;

sourceAddress
    : expression
    ;
    
targetAddress
    : expression
    ;
    
memorySize
    : expression
    ;
    
memoryValue
    : expression
    ;
    
// Expression rule - operator precedence (bottom = highest in ANTLR4)
// Precedence order: * / > + - > & > ^ > |
expression
    : address (lbhb)?                           # addressValue
    | number (lbhb)?                            # intValue
    | boolean                                   # boolValue
    | intFunction                               # intFunctionValue
    | boolFunction                              # boolFunctionValue
    | LParen expression RParen                  # subExpressionValue
    | expression BitOr expression               # bitOrExpressionValue
    | expression BitXor expression              # bitXorExpressionValue
    | expression BitAnd expression              # bitAndExpressionValue
    | expression Add expression                 # addValue
    | expression Sub expression                 # subValue
    | expression Mul expression                 # multiplyValue
    | expression Div expression                 # divisionValue
    ;
    
lbhb
    : LoByte  # loByte
    | HiByte  # hiByte
    ;
    
byteWord
    : Byte      # byteValue
    | Word      # wordValue
    ;
    
intFunction
    : peekByteFunction  # peekByteFunctionValue
    | peekWordFunction  # peekWordFunctionValue
    ;
   
boolFunction 
    : memoryChkFunction # memoryChkFunctionValue
    | memoryCmpFunction # memoryCmpFunctionValue
    ;
    
symbolRef
    : LBracket symbol RBracket
    ;
    
symbol
    : Identifier
    | Identifier '.' Identifier
    ;
     
Boolean
    : BoolTrue
    | BoolFalse
    ;
    
ProcessorFlag
    : FlagC
    | FlagN
    | FlagZ
    | FlagD
    | FlagV
    ;
    
Register
    : RegA
    | RegX
    | RegY
    ;

// Processor type tokens must come BEFORE Int to avoid 6502/6510 being matched as integers
ProcessorType6502:  '6502';
ProcessorType6510:  '6510';
ProcessorType65C02: '65c02' | '65C02';

Int
    : [0-9]+
    ;

Hex
    : Dollar HexDigit+
    | '0' [xX] HexDigit+
    ;
    
fragment
HexDigit
    : [0-9a-fA-F]
    ;
    
Binary
    : Percent [01]+
    ;

CompareOperator
    : Equal
    | GT
    | LT
    | GTE
    | LTE
    | NotEqual
    ;
    
Assign:     '=' ;
Equal:      '==' ;
GT:         '>' ;
LT:         '<' ;
GTE:        '>=' ;
LTE:        '<=' ;
NotEqual
    : '<>'
    | '!='
    ;
Add:        '+' ;
Sub:        '-' ;
Mul:        '*' ;
Div:        '/' ;
BitAnd:     '&' ;
BitOr:      '|' ;
BitXor:     '^' ;
Dollar:     '$' ;
Percent:    '%' ;
Comma:      ',' ;

LParen:     '(' ;
RParen:     ')' ;
LBrace:     '{' ;
RBrace:     '}' ;
LBracket:   '[' ;
RBracket:   ']' ;

fragment
Quote:      '"' ;

RegA: [aA] ;
RegX: [xX] ;
RegY: [yY] ;

FlagC: [cC] ;
FlagN: [nN] ;
FlagV: [vV] ;
FlagD: [dD] ;
FlagZ: [zZ] ;

BoolTrue:   'true' ;
BoolFalse:  'false' ;

Suites:         'suites' ;
Suite:          'suite' ;
Test:           'test' ;
Setup:          'setup' ;
Load:           'load' ;
Symbols:        'symbols' ;
Assert:         'assert' ;
JSR:            'jsr' ;
PeekByte:       'peekbyte' ;
PeekWord:       'peekword' ;
MemoryCmp:      'memcmp';
MemoryChk:      'memchk';
MemFill:        'memfill';
MemDump:        'memdump';
Cycles:         'cycles' ;
Address:        'address';
StripHeader:    'strip_header';
StopOnAddress:  'stop_on_address';
StopOnRTS:      'stop_on_rts';
FailOnBRK:      'fail_on_brk';
Skip:           'skip';
Trace:          'trace';
Timeout:        'timeout';
Tags:           'tags';
Processor:      'processor';
System:             'system';
Rom:                'rom';
SystemC64:          'c64' | 'C64';
SystemGeneric6502:  'generic_6502';
SystemGeneric6510:  'generic_6510';
SystemGeneric65C02: 'generic_65c02' | 'generic_65C02';

LoByte: '.l' | '.L' ;
HiByte: '.h' | '.H' ;

Byte: '.b' | '.B' ;
Word: '.w' | '.W' ;

// Identifier must start with letter or underscore, can contain letters, digits, underscores
Identifier
    : [a-zA-Z_][a-zA-Z0-9_]*
    ;
    
StringLiteral
    : Quote String Quote
    ;
	
fragment
String
    : ~ ["\n\r]+
    ;
	
Comment
    : ';' ~ [\r\n]* -> skip
    ;

// Whitespace includes newlines - no separate NewLine rule needed
WS
    : [ \t\r\n\u000C]+ -> skip
    ;