/*
 * Script Summary:
 * ----------------
 * DTO (Data Transfer Object) classes for minimal OpenAI-style chat completions.
 * Used for serialization/deserialization of messages, requests, and responses.
 *
 * Developer Notes:
 * ----------------
 * - Classes:
 *     • ChatMessage – { role, content } for system/user/assistant messages.
 *     • ChatRequest – { model, messages[], temperature } request payload.
 *     • Choice – { message } wrapper for response.
 *     • ChatResponse – { choices[] } top-level response container.
 * - Dependencies/Interactions:
 *     • Can be used with JsonUtility, Newtonsoft, or UnityWebRequest when calling OpenAI endpoints.
 *     • Forms the basis of communication between RenovAite and the LLM.
 * - Special Considerations:
 *     • Minimal subset; may be extended to include tool_calls or usage fields if needed.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var req = new ChatRequest {
 *     model = "gpt-4.1-mini",
 *     messages = new [] {
 *         new ChatMessage { role = "system", content = "You are RenovAite." },
 *         new ChatMessage { role = "user", content = "Scale this wall by 2x." }
 *     }
 * };
 * string json = JsonUtility.ToJson(req);
 * ```
 */

using System;

[Serializable]
public class ChatMessage { public string role; public string content; }

[Serializable]
public class ChatRequest
{
    public string model;
    public ChatMessage[] messages;
    public float temperature = 0.2f;
}

[Serializable]
public class Choice { public ChatMessage message; }

[Serializable]
public class ChatResponse { public Choice[] choices; }
