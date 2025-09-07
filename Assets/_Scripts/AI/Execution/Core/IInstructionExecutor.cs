using RenovAite.AI.Instructions.Models;

namespace RenovAite.AI.Execution
{
    public interface IInstructionExecutor
    {
        void Execute(AIInstruction instruction);
    }
}
