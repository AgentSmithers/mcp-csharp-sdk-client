using System.Diagnostics;
using System.Text;
using Newtonsoft.Json; // Use Newtonsoft.Json consistently
using Newtonsoft.Json.Serialization; // For CamelCasePropertyNamesContractResolver if needed, not used here
// Remove System.Text.Json to avoid ambiguity
// using System.Text.Json;

namespace QuickstartClient // Ensure this namespace is correct
{
    /// <summary>
    /// A client class to interact with the Google Gemini API for chat conversations.
    /// Manages conversation history per instance.
    /// Remember to configure ServicePointManager.SecurityProtocol for TLS 1.2 at application startup.
    /// </summary>
    public class GeminiAI
    {
        // --- Constants ---
        // Base URL is constant
        private const string ApiUrlBase = "https://generativelanguage.googleapis.com/v1beta/models/";

        // --- Instance Fields ---
        // API Key and Model Name are specific to the instance, passed in constructor
        private readonly string apiKey;
        private readonly string modelName;

        // Conversation history is specific to the instance
        private readonly List<Content> chatHistory;

        // Concurrency flag specific to chat operations on this instance
        private bool isSending = false;

        // --- Static Fields ---
        // HttpClient can be static and shared for performance (ensure TLS is configured elsewhere)
        private static readonly HttpClient httpClient = new HttpClient();

        #region Gemini API Data Classes
        // Nested or separate, these classes define the API contract
        // Added initializers '= null!;' or Array.Empty to satisfy CS8618 without full nullable context

        public class GeminiRequest { [JsonProperty("contents")] public Content[] Contents { get; set; } = Array.Empty<Content>(); }
        public class Content { [JsonProperty("role")] public string Role { get; set; } = "user"; [JsonProperty("parts")] public Part[] Parts { get; set; } = Array.Empty<Part>(); }
        public class Part { [JsonProperty("text")] public string Text { get; set; } = null!; } // Or string.Empty
        public class GeminiResponse { [JsonProperty("candidates")] public Candidate[] Candidates { get; set; } = Array.Empty<Candidate>(); [JsonProperty("promptFeedback")] public PromptFeedback PromptFeedback { get; set; } = null!; }
        public class Candidate { [JsonProperty("content")] public Content Content { get; set; } = null!; [JsonProperty("finishReason")] public string FinishReason { get; set; } = null!; [JsonProperty("index")] public int Index { get; set; } [JsonProperty("safetyRatings")] public SafetyRating[] SafetyRatings { get; set; } = Array.Empty<SafetyRating>(); }
        public class SafetyRating { [JsonProperty("category")] public string Category { get; set; } = null!; [JsonProperty("probability")] public string Probability { get; set; } = null!; }
        public class PromptFeedback { [JsonProperty("safetyRatings")] public SafetyRating[] SafetyRatings { get; set; } = Array.Empty<SafetyRating>(); }

        // Response container for the list of models
        public class ModelListResponse
        {
            [JsonProperty("models")]
            public ModelInfo[] Models { get; set; } = Array.Empty<ModelInfo>();
        }

        // Detailed information about a specific model
        public class ModelInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty; // e.g., "models/gemini-1.5-pro"

            [JsonProperty("displayName")]
            public string DisplayName { get; set; } = string.Empty;

            [JsonProperty("description")]
            public string Description { get; set; } = string.Empty;

            [JsonProperty("version")]
            public string Version { get; set; } = string.Empty;

            [JsonProperty("supportedGenerationMethods")]
            public string[] SupportedGenerationMethods { get; set; } = Array.Empty<string>();
        }
        #endregion // Gemini API Data Classes

        /// <summary>
        /// Creates a new instance of the Gemini Chat Client.
        /// </summary>
        /// <param name="apiKey">Your Google API Key.</param>
        /// <param name="modelNameInput">The specific Gemini model to use (e.g., "gemini-1.5-pro-latest"). Uses default if null/empty.</param>
        /// <exception cref="ArgumentNullException">Thrown if apiKey is null or empty.</exception>
        public GeminiAI(string? apiKey, string modelNameInput = "gemini-3-pro-preview") // Default to a stable model //gemini-1.5-pro-latest
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentNullException(nameof(apiKey), "API Key cannot be null or empty.");
            }

            this.apiKey = apiKey; // Assign constructor arg to instance field

            // Use the input model name, or the default if input is invalid
            if (string.IsNullOrEmpty(modelNameInput))
            {
                this.modelName = "gemini-3-pro-preview"; // Use the default from parameter signature
                Debug.WriteLine($"Warning: Model name was empty, defaulting to {this.modelName}");
            }
            else
            {
                this.modelName = modelNameInput; // Assign constructor arg to instance field
            }

            this.chatHistory = new List<Content>();

            // Optional: Configure static HttpClient defaults once if needed
            // Consider thread safety if modifying static properties after startup
            // if (httpClient.Timeout == TimeSpan.Zero) { httpClient.Timeout = TimeSpan.FromSeconds(120); }
        }

        public async Task<List<ModelInfo>?> ListModelsInConsole()
        {
            var models = await ListAvailableModelsAsync();

            if (models != null)
            {
                foreach (var model in models)
                {
                    // Filters specifically for models that can generate text/chat
                    if (model.SupportedGenerationMethods.Contains("generateContent"))
                    {
                        // Note: The API returns names like "models/gemini-pro". 
                        // You usually need to strip "models/" when making requests, 
                        // though the Google SDKs handle both.
                        Console.WriteLine($"Found Model: {model.Name} ({model.DisplayName})");
                    }
                }
                return models;
            }
            return null;
        }

        /// <summary>
        /// Sends a message as part of the ongoing conversation, maintaining history.
        /// </summary>
        /// <param name="userMessage">The user's message.</param>
        /// <returns>The model's response text as a Task<string?>, or null if an error occurred.</returns>
        public async Task<string?> SendChatMessageAsync(string userMessage) // Return nullable string
        {
            if (isSending)
            {
                Debug.WriteLine("Error: SendChatMessageAsync called while another request is in progress.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                Debug.WriteLine("Error: User message cannot be empty.");
                return null;
            }

            isSending = true;
            try
            {
                chatHistory.Add(new Content { Role = "user", Parts = new[] { new Part { Text = userMessage } } });

                // Use instance field modelName
                string apiUrl = $"{ApiUrlBase}{this.modelName}:generateContent?key={this.apiKey}";
                var requestPayload = new GeminiRequest { Contents = chatHistory.ToArray() };
                string jsonPayload = JsonConvert.SerializeObject(requestPayload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                Debug.WriteLine($"Sending chat request to {this.modelName} with {chatHistory.Count} history items...");

                using (StringContent httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage response = await httpClient.PostAsync(apiUrl, httpContent);
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        // --- Change variable type to nullable ---
                        GeminiResponse? geminiResponse = JsonConvert.DeserializeObject<GeminiResponse>(jsonResponse);
                        // ----------------------------------------

                        // Your existing null-conditional checks handle the rest correctly
                        string? responseText = geminiResponse?.Candidates?.FirstOrDefault()?
                                                        .Content?.Parts?.FirstOrDefault()?.Text;

                        if (!string.IsNullOrEmpty(responseText))
                        {
                            // ... rest of success logic ...
                            chatHistory.Add(new Content { Role = "model", Parts = new[] { new Part { Text = responseText } } });
                            return responseText.Trim();
                        }
                        else
                        {
                            Debug.WriteLine($"Warning: Gemini returned null or empty inference response object/text. Raw JSON:\n{jsonResponse}");
                            return string.Empty;
                        }
                    }
                    // ... rest of method ...
                    else
                    {
                        Debug.WriteLine($"API Error: {(int)response.StatusCode} - {response.ReasonPhrase}\nResponse: {jsonResponse}");
                        if (chatHistory.Any() && chatHistory.Last().Role == "user") { chatHistory.RemoveAt(chatHistory.Count - 1); }
                        return null;
                    }
                }
            }
            catch (HttpRequestException httpEx) { Debug.WriteLine($"Network Error sending chat: {httpEx.ToString()}"); if (chatHistory.Any() && chatHistory.Last().Role == "user") { chatHistory.RemoveAt(chatHistory.Count - 1); } return null; }
            catch (JsonException jsonEx) { Debug.WriteLine($"JSON Error processing chat response: {jsonEx.ToString()}"); return null; } // Use fully qualified name if needed, but removing the other using should fix ambiguity
            catch (Exception ex) { Debug.WriteLine($"Unexpected Error sending chat: {ex.ToString()}"); if (chatHistory.Any() && chatHistory.Last().Role == "user") { chatHistory.RemoveAt(chatHistory.Count - 1); } return null; }
            finally { isSending = false; }
        }

        /// <summary>
        /// Sends a single message for inference without using or modifying the conversation history.
        /// </summary>
        /// <param name="userMessage">The user's message/prompt.</param>
        /// <returns>The model's response text as a Task<string?>, or null if an error occurred.</returns>
        public async Task<string?> InferAsync(string userMessage) // Return nullable string
        {
            if (string.IsNullOrWhiteSpace(userMessage)) { Debug.WriteLine("Error: User message for inference cannot be empty."); return null; }

            try
            {
                // Use instance field modelName
                string apiUrl = $"{ApiUrlBase}{this.modelName}:generateContent?key={this.apiKey}";
                var requestPayload = new GeminiRequest { Contents = new[] { new Content { Role = "user", Parts = new[] { new Part { Text = userMessage } } } } };
                string jsonPayload = JsonConvert.SerializeObject(requestPayload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                Debug.WriteLine($"Sending inference request to {this.modelName}...");

                using (StringContent httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage response = await httpClient.PostAsync(apiUrl, httpContent);
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        // --- Change variable type to nullable ---
                        GeminiResponse? geminiResponse = JsonConvert.DeserializeObject<GeminiResponse>(jsonResponse);
                        // ----------------------------------------

                        // Your existing null-conditional checks handle the rest correctly
                        string? responseText = geminiResponse?.Candidates?.FirstOrDefault()?
                                                        .Content?.Parts?.FirstOrDefault()?.Text;

                        if (!string.IsNullOrEmpty(responseText))
                        {
                            // ... rest of success logic ...
                            return responseText.Trim();
                        }
                        else
                        {
                            // Handle case where deserialization might have worked but responseText is still null/empty
                            // Or if geminiResponse itself was null
                            Debug.WriteLine($"Warning: Gemini returned null or empty response object/text. Raw JSON:\n{jsonResponse}");
                            if (chatHistory.Any() && chatHistory.Last().Role == "user") { chatHistory.RemoveAt(chatHistory.Count - 1); }
                            return string.Empty;
                        }
                    }
                    // ... rest of method ...
                    else
                    {
                        Debug.WriteLine($"API Error during inference: {(int)response.StatusCode} - {response.ReasonPhrase}\nResponse: {jsonResponse}");
                        return null;
                    }
                }
            }
            catch (HttpRequestException httpEx) { Debug.WriteLine($"Network Error during inference: {httpEx.ToString()}"); return null; }
            catch (JsonException jsonEx) { Debug.WriteLine($"JSON Error processing inference response: {jsonEx.ToString()}"); return null; } // Use fully qualified name if needed
            catch (Exception ex) { Debug.WriteLine($"Unexpected Error during inference: {ex.ToString()}"); return null; }
        }

        /// <summary>
        /// Retrieves a list of available models from the Gemini API.
        /// </summary>
        /// <returns>A list of ModelInfo objects, or null if the request fails.</returns>
        public async Task<List<ModelInfo>?> ListAvailableModelsAsync()
        {
            try
            {
                // Construct URL: Remove trailing slash from Base and add API key
                // Endpoint: https://generativelanguage.googleapis.com/v1beta/models?key=YOUR_KEY
                string baseUrl = ApiUrlBase.TrimEnd('/');
                string apiUrl = $"{baseUrl}?key={this.apiKey}";

                Debug.WriteLine("Fetching available models...");

                // Perform GET request
                HttpResponseMessage response = await httpClient.GetAsync(apiUrl);
                string jsonResponse = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var listResponse = JsonConvert.DeserializeObject<ModelListResponse>(jsonResponse);

                    // Optional: Filter only for models that support content generation (chat)
                    // return listResponse?.Models.Where(m => m.SupportedGenerationMethods.Contains("generateContent")).ToList();

                    return listResponse?.Models.ToList();
                }
                else
                {
                    Debug.WriteLine($"API Error listing models: {(int)response.StatusCode} - {response.ReasonPhrase}\nResponse: {jsonResponse}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error listing models: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clears the internal conversation history for this client instance.
        /// </summary>
        public void ClearHistory()
        {
            this.chatHistory.Clear();
            Debug.WriteLine("Chat history cleared.");
        }

        // Optional: Add method to get current history if needed
        public IReadOnlyList<Content> GetHistory() => chatHistory.AsReadOnly();

    } // End of GeminiAI class
} // End of namespace
