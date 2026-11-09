using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

// the breakpoint table as the interpreter asks it between two instructions: the breakpoints are on lines of the source, and the interpreter runs instructions.
public interface IInstructionBreakpointTable : IBreakpointTable
{
    // whether the instruction at the offset of the body is where a breakpoint is; the condition is the breakpoint's, which the interpreter evaluates before it
    // waits, or null when it has none.
    bool IsAt(string module, InstructionList body, int offset, out ExpressionNode? condition);
}
