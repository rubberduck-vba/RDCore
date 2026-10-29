grammar VBAConditionalCompilation;

options {
    tokenVocab = VBALexer;
    superClass = VBABaseParser;
    contextSuperClass = VBABaseParserRuleContext;
 }

// a #Else, #ElseIf or #End If that belongs to no block is a line of the module's own, between its blocks: SLL prediction, which does not know the module's
// ccBlock from a block's, ends the ccBlock there. One that is not well formed is no directive, and is a physicalLine wherever it is.
compilationUnit : ccBlock (strayDirective ccBlock)* EOF;
strayDirective : ccElseIf | ccElse | ccEndIf;

ccBlock :
    // We use the non-greedy operator so that we stop consuming EOFs as soon as possible.
    // EOFs can be used in other places than the start rule since they're "emitted as long as someone takes them".
    (ccConst | ccIfBlock | physicalLine)*?
;

ccConst : whiteSpace* hashConst whiteSpace+ ccVarLhs whiteSpace* EQ whiteSpace* ccExpression ccEol;
ccVarLhs : name;

ccIfBlock : ccIf ccBlock ccElseIfBlock* ccElseBlock? ccEndIf;
// MS-VBAL 3.4.2 (cc-if): no whitespace is required around the condition, which a parenthesis or the lexer's word boundaries separate: #If(VBA7) Then,
// #If (Win64)Then.
ccIf : whiteSpace* hashIf whiteSpace* ccExpression whiteSpace* THEN ccEol;
ccElseIfBlock : ccElseIf ccBlock;
ccElseIf : whiteSpace* hashElseIf whiteSpace* ccExpression whiteSpace* THEN ccEol;
ccElseBlock : ccElse ccElseBody;
// what follows a block's #Else is ended by its #End If alone: a #Else or #ElseIf after it is a line of it, which the directive scanner reports. A rule of its
// own, for the #End If to be all that SLL prediction finds can follow it.
ccElseBody : (ccConst | ccIfBlock | physicalLine)*?;
ccElse : whiteSpace* hashElse ccEol;
ccEndIf : whiteSpace* hashEndIf ccEol;
ccEol : whiteSpace* comment? (NEWLINE | EOF);
// We use parser rules instead of tokens (such as HASHCONST) because
// marked file numbers have a similar format and cause conflicts.
// MS-VBA has the '#' and the keyword apart too, with spaces or a line-continuation between them (oracle\cc-continued C13, D01-D06).
hashConst : HASH whiteSpace* CONST;
hashIf : HASH whiteSpace* IF;
hashElseIf : HASH whiteSpace* ELSEIF;
hashElse : HASH whiteSpace* ELSE;
hashEndIf : HASH whiteSpace* END_IF;

physicalLine : ~(NEWLINE | EOF)* (NEWLINE | EOF);

ccExpression :
    LPAREN whiteSpace* ccExpression whiteSpace* RPAREN                                                     # parenthesizedExpr
    | ccExpression whiteSpace* POW whiteSpace* ccExpression                                                # powOp
    | MINUS whiteSpace* ccExpression                                                                       # unaryMinusOp
    | ccExpression whiteSpace* (MULT | DIV) whiteSpace* ccExpression                                       # multOp
    | ccExpression whiteSpace* INTDIV whiteSpace* ccExpression                                             # intDivOp
    | ccExpression whiteSpace* MOD whiteSpace* ccExpression                                                # modOp
    | ccExpression whiteSpace* (PLUS | MINUS) whiteSpace* ccExpression                                     # addOp
    | ccExpression whiteSpace* AMPERSAND whiteSpace* ccExpression                                          # concatOp
    | ccExpression whiteSpace* (EQ | NEQ | LT | GT | LEQ | GEQ | LIKE | IS) whiteSpace* ccExpression       # relationalOp
    | NOT whiteSpace* ccExpression                                                                         # logicalNotOp
    | ccExpression whiteSpace* AND whiteSpace* ccExpression                                                # logicalAndOp
    | ccExpression whiteSpace* OR whiteSpace* ccExpression                                                 # logicalOrOp
    | ccExpression whiteSpace* XOR whiteSpace* ccExpression                                                # logicalXorOp
    | ccExpression whiteSpace* EQV whiteSpace* ccExpression                                                # logicalEqvOp
    | ccExpression whiteSpace* IMP whiteSpace* ccExpression                                                # logicalImpOp
    | intrinsicFunction                                                                                    # intrinsicCallExpr
    | literal                                                                                              # literalExpr
    | name                                                                                                 # nameExpr
;

intrinsicFunction : intrinsicFunctionName LPAREN whiteSpace* ccExpression whiteSpace* RPAREN;

intrinsicFunctionName :
    INT
    | FIX
    | ABS
    | SGN
    | LEN
    | LENB
    | CBOOL
    | CBYTE
    | CCUR
    | CDATE
    | CDBL
    | CINT
    | CLNG
    | CLNGLNG
    | CLNGPTR
    | CSNG
    | CSTR
    | CVAR
;

name : nameValue typeHint?;
nameValue : IDENTIFIER | keyword | foreignName | statementKeyword | markerKeyword;
foreignName : L_SQUARE_BRACKET foreignIdentifier* R_SQUARE_BRACKET;
foreignIdentifier : ~L_SQUARE_BRACKET | foreignName;

typeHint : PERCENT | AMPERSAND | POW | EXCLAMATIONPOINT | HASH | AT | DOLLAR;

literal : DATELITERAL | HEXLITERAL | OCTLITERAL | FLOATLITERAL | INTEGERLITERAL | STRINGLITERAL | TRUE | FALSE | NOTHING | NULL | EMPTY;

comment: SINGLEQUOTE (LINE_CONTINUATION | ~NEWLINE)*;

keyword : 
       ABS
     | ADDRESSOF
     | ALIAS
     | AND
     | ANY
     | ARRAY
     | ATTRIBUTE
     | BEGIN
     | BOOLEAN
     | BYREF
     | BYTE
     | BYVAL
     | CBOOL
     | CBYTE
     | CCUR
     | CDATE
     | CDBL
     | CDEC
     | CINT
     | CLASS
     | CLNG
     | CLNGLNG
     | CLNGPTR
     | CSNG
     | CSTR
     | CURRENCY
     | CVAR
     | CVERR
     | DATABASE
     | DATE
     | DEBUG
     | DOEVENTS
     | DOUBLE
     | END
     | EQV
     | FALSE
     | FIX
     | IMP
     | IN
     | INPUTB
     | INT
     | INTEGER
     | IS
     | LBOUND
     | LEN
     | LEN
     | LENB
     | LIB
     | LIKE
     | LOAD
     | LONG
     | LONGLONG
     | LONGPTR
     | ME
     | MID
     | MIDB
     | MIDBTYPESUFFIX
     | MIDTYPESUFFIX
     | MOD
     | NEW
     | NOT
     | NOTHING
     | NULL
     | OBJECT
     | OPTIONAL
     | OR
     | PARAMARRAY
     | PRESERVE
     | PSET
     | PTRSAFE
     | REM
     | SGN
     | SINGLE
     | SPC
     | STRING
     | TAB
     | TEXT
     | THEN
     | TO
     | TRUE
     | TYPEOF
     | UBOUND
     | UNTIL
     | VARIANT
     | VERSION
     | WITHEVENTS
     | XOR
     | STEP
     | ON_ERROR
     | RESUME_NEXT
     | ERROR
     | APPEND
     | BINARY
     | OUTPUT
     | RANDOM
     | ACCESS
     | READ
     | WRITE
     | READ_WRITE
     | SHARED
     | LOCK_READ
     | LOCK_WRITE
     | LOCK_READ_WRITE
     | LINE_INPUT    
     | RESET
     | WIDTH
     | PRINT
     | GET
     | PUT
     | CLOSE
     | INPUT
     | LOCK
     | OPEN
     | SEEK
     | UNLOCK
     | WRITE
	 | NAME
;

markerKeyword : AS;

statementKeyword :
    CALL
    | CASE
    | CONST
    | DECLARE
    | DEFBOOL
    | DEFBYTE
    | DEFCUR
    | DEFDATE
    | DEFDBL
    | DEFINT
    | DEFLNG
    | DEFLNGLNG
    | DEFLNGPTR
    | DEFOBJ
    | DEFSNG
    | DEFSTR
    | DEFVAR
    | DIM
    | DO
    | ELSE
    | ELSEIF
    | ENUM
    | ERASE
    | EVENT
    | EXIT
    | EXIT_DO 
    | EXIT_FOR 
    | EXIT_FUNCTION 
    | EXIT_PROPERTY 
    | EXIT_SUB
    | END_SELECT
    | END_WITH
    | FOR
    | FRIEND
    | FUNCTION
    | GLOBAL
    | GOSUB
    | GOTO
    | IF
    | IMPLEMENTS
    | LET
    | LOOP
    | LSET
    | NEXT
    | ON
    | OPTION
    | PRIVATE
    | PUBLIC
    | RAISEEVENT
    | REDIM
    | RESUME
    | RETURN
    | RSET
    | SELECT
    | SET
    | STATIC
    | STOP
    | SUB
    | TYPE
    | WEND
    | WHILE
    | WITH
;

whiteSpace : (WS | LINE_CONTINUATION)+;