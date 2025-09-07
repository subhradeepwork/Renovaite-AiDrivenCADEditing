/*
 * Script Summary:
 * ----------------
 * Manages the in-game chat panel, adding messages from the user and AI responses.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ChatManager – MonoBehaviour attached to chat UI root.
 * - Key Methods:
 *     • OnSendButton(message) – Adds user message and clears input field.
 *     • OnResponseReceived(msg) – Simulates receiving a bot response.
 *     • AddMessage(message, isUser) – Instantiates correct prefab (left/right).
 * - Key Fields:
 *     • leftMessagePrefab / rightMessagePrefab – Prefabs for AI vs user messages.
 *     • contentPanel – Parent transform for chat messages.
 *     • inputField – TMP input field for text entry.
 * - Special Considerations:
 *     • SimulateBotResponse coroutine delays by 1s before showing AI response.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * chatManager.OnSendButton("Scale wall by 2x");
 * chatManager.OnResponseReceived("{\"action\":\"scale\"...}");
 * ```
 */

using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatManager : MonoBehaviour
{
    public GameObject leftMessagePrefab;
    public GameObject rightMessagePrefab;
    public Transform contentPanel;
    public TMP_InputField inputField;

    public void OnSendButton(string message)
    {
        message = message.Trim();
        if (string.IsNullOrEmpty(message)) return;

        AddMessage(message, isUser: true); // Your message
        inputField.text = "";
        
    }

    public void OnResponseReceived(string msg)
    {
        // Simulate a response
        StartCoroutine(SimulateBotResponse(msg));
    }


    public void AddMessage(string message, bool isUser)
    {
        GameObject prefab = isUser ? rightMessagePrefab : leftMessagePrefab;
        GameObject messageGO = Instantiate(prefab, contentPanel);
        messageGO.GetComponentInChildren<TMP_Text>().text = message;
    }

    System.Collections.IEnumerator SimulateBotResponse(string response)
    {
        yield return new WaitForSeconds(1);
        AddMessage(response, isUser: false);
    }
}
