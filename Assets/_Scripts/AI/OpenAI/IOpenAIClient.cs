using System.Threading;
using System.Threading.Tasks;

namespace RenovAite.AI.OpenAI
{
    public interface IOpenAIClient
    {
        Task<string> CreateChatCompletionAsync(string system, string user, string toolsJson, CancellationToken ct);
    }
}
