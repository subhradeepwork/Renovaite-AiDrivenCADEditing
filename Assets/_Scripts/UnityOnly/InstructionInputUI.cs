/*
 * Script Summary:
 * ----------------
 * Connects chat input UI with AIClient. Sends instructions to the AI pipeline and
 * feeds responses back into ChatManager for display.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: InstructionInputUI – MonoBehaviour for input handling.
 * - Key Methods:
 *     • OnSendButtonClicked() – Collects input, adds to chat, sends to AIClient, handles callback.
 * - Key Fields:
 *     • instructionInputField – TMP input field for instruction text.
 *     • sendButton – Button triggering AI send.
 *     • aiClient – Reference to AIClient integration.
 *     • chatManager – Chat UI handler.
 * - Dependencies/Interactions:
 *     • AIClient.SendInstructionToAI – Invoked with callback when tool-call args are returned.
 *     • ChatManager – Updates UI with user + AI messages.
 * - Special Considerations:
 *     • Disables sendButton while awaiting AI response to avoid duplicate sends.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * instructionUI.instructionInputField.text = "move wall left by 1m";
 * instructionUI.OnSendButtonClicked();
 * ```
 */

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InstructionInputUI : MonoBehaviour
{
    public TMP_InputField instructionInputField;
    public Button sendButton;
    public AIClient aiClient;
    public ChatManager chatManager;

    void Start()
    {
        sendButton.onClick.AddListener(OnSendButtonClicked);
    }

    void OnSendButtonClicked()
    {
        string instruction = instructionInputField.text;
        if (string.IsNullOrWhiteSpace(instruction))
            return;

        chatManager.OnSendButton(instruction);
        sendButton.interactable = false; // optional: prevent double-send

        aiClient.SendInstructionToAI(instruction, modelArgsJson =>
        {
            // This is the tool-call args (normalized) returned by AIClient after execution.
            Debug.Log("[UI] Tool-call args returned: " + modelArgsJson);
            chatManager.OnResponseReceived(modelArgsJson);

            sendButton.interactable = true;
        });
    }

}
