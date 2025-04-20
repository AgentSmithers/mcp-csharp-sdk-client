using Newtonsoft.Json;
using System.Diagnostics;
using System.Text;
using System.Net.Http; // Ensure HttpClient is available
using System.Threading.Tasks; // Ensure Task is available
using System.Collections.Generic; // For List<>
using System.Linq; // For FirstOrDefault()

// Optional: Define a namespace consistent with your project structure
// namespace YourProjectNamespace
// {

/// <summary>
/// A client class to interact with the Anthropic Claude API.
/// Manages conversation history per instance, similar to the GeminiAI client.
/// </summary>
internal class AnthropicAI
{
    // --- Constants ---
    private const string ApiEndpoint = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01"; // Anthropic API version

    // --- Instance Fields ---
    private readonly string _apiKey;
    private readonly string _modelName;
    public readonly List<Content> chatHistory; // Stores conversation history for this instance
    private bool isSending = false; // Concurrency flag for chat operations

    // --- Static Fields ---
    // Re-use HttpClient for performance, similar to GeminiAI example
    private static readonly HttpClient httpClient = new HttpClient();

    #region Claude API Data Classes
    // Nested or separate, define the API contract

    // Request structure for Claude
    private class ClaudeRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; } = string.Empty;

        [JsonProperty("max_tokens")]
        public int MaxTokens { get; set; } // Consider making this configurable

        [JsonProperty("messages")]
        public Content[] Messages { get; set; } = Array.Empty<Content>();

        // Add other parameters like temperature, system prompt if needed
        // [JsonProperty("system", NullValueHandling = NullValueHandling.Ignore)]
        // public string? SystemPrompt { get; set; }
    }

    // Content structure for messages (used in request and history)
    // Note: Claude uses 'content' (string) directly within the message object,
    // unlike Gemini's 'parts' array.
    public class Content // Renamed for consistency, was also used in response
    {
        [JsonProperty("role")]
        public string Role { get; set; } = string.Empty; // "user" or "assistant"

        [JsonProperty("content")]
        public string Text { get; set; } = string.Empty; // Changed from 'Content' to 'Text' for internal consistency if preferred, but maps to Claude's 'content'
    }

    // Response structure from Claude
    public class MessageResponse // Keep public if used outside the class
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("type")] // "message"
        public string Type { get; set; } = string.Empty;

        [JsonProperty("role")] // "assistant"
        public string Role { get; set; } = string.Empty;

        [JsonProperty("model")]
        public string Model { get; set; } = string.Empty;

        [JsonProperty("content")] // This is an array in the response
        public ResponseContentItem[] Content { get; set; } = Array.Empty<ResponseContentItem>();

        [JsonProperty("stop_reason")]
        public string? StopReason { get; set; } // e.g., "end_turn", "max_tokens"

        [JsonProperty("stop_sequence")]
        public string? StopSequence { get; set; }

        [JsonProperty("usage")]
        public Usage Usage { get; set; } = new Usage();
    }

    // Structure for content items within the response
    public class ResponseContentItem // Renamed to avoid conflict with request 'Content'
    {
        [JsonProperty("type")] // e.g., "text"
        public string Type { get; set; } = string.Empty;

        [JsonProperty("text")]
        public string Text { get; set; } = string.Empty;
    }

    // Usage information from the response
    public class Usage // Keep public if used outside
    {
        [JsonProperty("input_tokens")]
        public int InputTokens { get; set; }

        [JsonProperty("output_tokens")]
        public int OutputTokens { get; set; }
    }

    #endregion // Claude API Data Classes

    /// <summary>
    /// Creates a new instance of the Claude Chat Client.
    /// </summary>
    /// <param name="apiKey">Your Anthropic API Key.</param>
    /// <param name="modelNameInput">The specific Claude model to use (e.g., "claude-3-sonnet-20240229").</param>
    /// <exception cref="ArgumentNullException">Thrown if apiKey is null or empty.</exception>
    public AnthropicAI(string? apiKey, string modelNameInput = "claude-3-7-sonnet-20250219") // Updated default model
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey), "API Key cannot be null or empty.");
        }

        this._apiKey = apiKey;

        if (string.IsNullOrEmpty(modelNameInput))
        {
            this._modelName = "claude-3-sonnet-20240229"; // Default model
            Debug.WriteLine($"Warning: Model name was empty, defaulting to {this._modelName}");
        }
        else
        {
            this._modelName = modelNameInput;
        }

        this.chatHistory = new List<Content>();

        // Configure shared HttpClient once (if not already done elsewhere)
        // Static HttpClient setup might be better placed in a static constructor
        // or application startup logic. For simplicity, we ensure headers are added per call if needed.
        // httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", this._apiKey); // This is tricky with shared client if multiple keys are used
        // httpClient.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", ApiVersion);
    }

    /// <summary>
    /// Sends a message as part of the ongoing conversation, maintaining history.
    /// </summary>
    /// <param name="userMessage">The user's message.</param>
    /// <returns>The model's response text as a Task<string?>, or null if an error occurred.</returns>
    public async Task<string?> SendChatMessageAsync(string userMessage)
    {
        if (isSending)
        {
            Debug.WriteLine("Error: SendChatMessageAsync called while another request is in progress.");
            return null; // Or throw an exception
        }
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            Debug.WriteLine("Error: User message cannot be empty.");
            return null;
        }

        isSending = true;
        var userMessageContent = new Content { Role = "user", Text = userMessage }; // Use 'Text' internally

        try
        {
            // Add user message to history *before* sending
            chatHistory.Add(userMessageContent);

            var request = new ClaudeRequest
            {
                Model = _modelName,
                MaxTokens = 2048, // Increased max tokens, make configurable if needed
                Messages = chatHistory.ToArray() // Send the entire history
                // SystemPrompt = "Your optional system prompt here"
            };

            Debug.WriteLine($"Sending chat request to {_modelName} with {chatHistory.Count} history items...");

            // Call the refactored API call method
            MessageResponse? claudeResponse = await CallClaudeApiInternalAsync(request);

            if (claudeResponse != null)
            {
                // Extract the primary text response
                string? responseText = claudeResponse.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

                if (!string.IsNullOrEmpty(responseText))
                {
                    // Add model response to history
                    chatHistory.Add(new Content { Role = "assistant", Text = responseText });
                    Debug.WriteLine($"Received response, history count: {chatHistory.Count}");
                    return responseText.Trim();
                }
                else
                {
                    // Handle cases where response is valid but content is empty/missing
                    Debug.WriteLine($"Warning: Claude returned a valid response but no text content found. Stop Reason: {claudeResponse.StopReason}");
                    // Potentially remove the user message if the response was essentially empty/error? Debatable.
                    // For now, we keep the user message but return empty string.
                    return string.Empty;
                }
            }
            else
            {
                // CallClaudeApiInternalAsync already logged the error. Remove the user message added optimistically.
                if (chatHistory.LastOrDefault() == userMessageContent)
                {
                    chatHistory.RemoveAt(chatHistory.Count - 1);
                    Debug.WriteLine("Removed last user message from history due to API call failure.");
                }
                return null; // Error occurred
            }
        }
        catch (Exception ex) // Catch broader exceptions from logic above API call
        {
            Debug.WriteLine($"Unexpected Error in SendChatMessageAsync: {ex.ToString()}");
            // Ensure history consistency if an unexpected error occurs after adding user message
            if (chatHistory.LastOrDefault() == userMessageContent)
            {
                chatHistory.RemoveAt(chatHistory.Count - 1);
                Debug.WriteLine("Removed last user message from history due to unexpected error.");
            }
            return null;
        }
        finally
        {
            isSending = false;
        }
    }

    /// <summary>
    /// Sends a single message for inference without using or modifying the conversation history.
    /// </summary>
    /// <param name="userMessage">The user's message/prompt.</param>
    /// <returns>The model's response text as a Task<string?>, or null if an error occurred.</returns>
    public async Task<string?> InferAsync(string userMessage) // Return nullable string
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            Debug.WriteLine("Error: User message for inference cannot be empty.");
            return null;
        }

        // Do not use or modify this.chatHistory here
        var request = new ClaudeRequest
        {
            Model = _modelName,
            MaxTokens = 2048, // Make configurable if needed
            Messages = new[] // Create a new history just for this call
            {
                new Content { Role = "user", Text = userMessage } // Use 'Text' internally
            }
            // SystemPrompt = "Your optional system prompt here"
        };

        Debug.WriteLine($"Sending inference request to {_modelName}...");

        try
        {
            // Call the refactored API call method
            MessageResponse? claudeResponse = await CallClaudeApiInternalAsync(request);

            if (claudeResponse != null)
            {
                string? responseText = claudeResponse.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

                if (!string.IsNullOrEmpty(responseText))
                {
                    return responseText.Trim();
                }
                else
                {
                    Debug.WriteLine($"Warning: Claude returned a valid inference response but no text content found. Stop Reason: {claudeResponse.StopReason}");
                    return string.Empty; // Return empty for valid response with no text
                }
            }
            else
            {
                // Error handled and logged by CallClaudeApiInternalAsync
                return null; // Error occurred
            }
        }
        catch (Exception ex) // Catch broader exceptions
        {
            Debug.WriteLine($"Unexpected Error in InferAsync: {ex.ToString()}");
            return null;
        }
        // No finally block needed here as isSending is not used for InferAsync
    }


    /// <summary>
    /// Internal method to handle the actual HTTP call to the Claude API.
    /// </summary>
    /// <param name="messageRequest">The request payload.</param>
    /// <returns>The deserialized MessageResponse or null if an error occurred.</returns>
    private async Task<MessageResponse?> CallClaudeApiInternalAsync(ClaudeRequest messageRequest)
    {
        try
        {
            string jsonPayload = JsonConvert.SerializeObject(messageRequest, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            using var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Use a separate HttpRequestMessage to set headers per request,
            // which is safer with a shared HttpClient if API keys could differ per instance.
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
            httpRequest.Headers.Add("x-api-key", _apiKey);
            httpRequest.Headers.Add("anthropic-version", ApiVersion);
            httpRequest.Content = httpContent;

            HttpResponseMessage response = await httpClient.SendAsync(httpRequest).ConfigureAwait(false); // Use shared client
            string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                // Deserialize and handle potential null
                MessageResponse? deserializedResponse = JsonConvert.DeserializeObject<MessageResponse>(jsonResponse);
                if (deserializedResponse == null)
                {
                    Debug.WriteLine($"JSON Error: Failed to deserialize Claude response. Raw JSON:\n{jsonResponse}");
                    return null;
                }
                return deserializedResponse;
            }
            else
            {
                // Log detailed error information
                Debug.WriteLine($"API Error: {(int)response.StatusCode} - {response.ReasonPhrase}\nResponse: {jsonResponse}");
                // Consider parsing the error response body for more details if Claude provides structured errors
                return null; // Indicate failure
            }
        }
        catch (HttpRequestException httpEx)
        {
            Debug.WriteLine($"Network Error calling Claude API: {httpEx.ToString()}");
            return null;
        }
        catch (JsonException jsonEx)
        {
            Debug.WriteLine($"JSON Error processing Claude request/response: {jsonEx.ToString()}");
            return null;
        }
        catch (Exception ex) // Catch unexpected errors during the API call process
        {
            Debug.WriteLine($"Unexpected error calling Claude API: {ex.ToString()}");
            return null;
        }
    }


    /// <summary>
    /// Clears the internal conversation history for this client instance.
    /// </summary>
    public void ClearHistory()
    {
        this.chatHistory.Clear();
        Debug.WriteLine("Claude chat history cleared.");
    }

    /// <summary>
    /// Gets a read-only view of the current conversation history.
    /// </summary>
    /// <returns>Read-only list of Content messages.</returns>
    public IReadOnlyList<Content> GetHistory() => chatHistory.AsReadOnly();

} // End of ClaudeAI class

// } // End of namespace