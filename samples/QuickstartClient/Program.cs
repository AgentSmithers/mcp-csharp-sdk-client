using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using QuickstartClient;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public class Program
{
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
    const int VK_SHIFT = 0x10;

    public static async Task Main(string[] args)
    {

        string? GeminiAIKey = Environment.GetEnvironmentVariable("GeminiAIKey");
        string? AnthropicAIKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        string? MCPServerIP = Environment.GetEnvironmentVariable("MCPServerIP");
        GeminiAI MyGem = new GeminiAI(GeminiAIKey);
        AnthropicAI MyClaude = new AnthropicAI(AnthropicAIKey);
        //Debug.WriteLine(await MyGem.SendChatMessageAsync(initialPrompt));
        //Debug.WriteLine(await MyGem.SendChatMessageAsync("My name is Mike"));
        //Debug.WriteLine(await MyGem.SendChatMessageAsync("What is my name?"));

        // Place these static helpers inside the Program class or keep them accessible
        await using var mcpClient = await McpClientFactory.CreateAsync(new()
        {
            Id = "demo-server",
            Name = "Demo Server",
            TransportType = TransportTypes.Sse,
            Location = "http://" + MCPServerIP + ":3001/sse",
            //Location = "http://" + "Localhost" + ":4858/McpHandler.ashx/sse", //LocalHost is required for IIS, 127.0.0.1 will not work
            //Location = "http://" + "Localhost" + ":64163/McpHandler.ashx/sse", //LocalHost is required for IIS, 127.0.0.1 will not work
            //Location = "http://localhost:3001/sse",
        });
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>();

        var (command, arguments) = GetCommandAndArguments(args);

        // Create a command processor
        var commandProcessor = new CommandProcessor();

        // Initialize with available commands from the server
        await commandProcessor.RefreshAvailableCommandsAsync(mcpClient);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("MCP Client Started!");
        Console.ResetColor();

        PromptForConsoleInput();

        String? CurrentMessageToPassBackToAi = initialPrompt;
        //ListDebuggerCommands
        bool AllowForInput = false;
        bool SignalBackToHumanControl = false;
        bool AnyCommandProcess = false;
        //while (Console.ReadLine() is string query && !"exit".Equals(query, StringComparison.OrdinalIgnoreCase))
        bool shiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
        while (CurrentMessageToPassBackToAi != "exit")
        {
            string? query = "";

            //if (shiftPressed)
            //{
            //    AllowForInput = true;
            //    Debug.WriteLine("Ready for Human input");
            //    CurrentMessageToPassBackToAi = Console.ReadLine();
            //}

            while (AllowForInput || !SignalBackToHumanControl) //If shift is held, skip to commandline
            {

                shiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

                if (shiftPressed)
                {
                    AllowForInput = true;
                    SignalBackToHumanControl = true;
                    break;
                }

                if (!SignalBackToHumanControl)
                {
                    if (string.IsNullOrWhiteSpace(CurrentMessageToPassBackToAi))
                    {
                        Console.WriteLine("Message to AI is empty or null, stopping conversation.");
                        break; // Exit the outer loop if the message is invalid
                    }
                    //query = await MyGem.SendChatMessageAsync(CurrentMessageToPassBackToAi);
                    query = await MyClaude.SendChatMessageAsync(CurrentMessageToPassBackToAi);
                    if (string.IsNullOrWhiteSpace(query))
                    {
                        PromptForConsoleInput();
                        continue;
                    }
                }
                else
                {
                    query = CurrentMessageToPassBackToAi;
                }

                if (string.IsNullOrWhiteSpace(query))
                {
                    Debug.WriteLine("Query is blank");
                    break;
                }

                //Console.ForegroundColor = ConsoleColor.Cyan;
                //Console.WriteLine(CurrentMessageToPassBackToAi);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(query);
                Console.ResetColor();


                // Assume 'query' is the input string potentially containing multiple lines
                string[] lines = query.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                AnyCommandProcess = false;
                CurrentMessageToPassBackToAi = "";
                foreach (var line in lines)
                {
                    // Trim whitespace from the start and end of the line
                    string trimmedLine = line.Trim();

                    // Skip if the line is effectively empty after trimming
                    if (string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        continue;
                    }

                    if (line.Equals("continue", StringComparison.OrdinalIgnoreCase))
                    {
                        SignalBackToHumanControl = false;
                        AllowForInput = false;
                        continue;
                    }
                    if (line.Equals("help", StringComparison.OrdinalIgnoreCase))
                    {
                        CurrentMessageToPassBackToAi += commandProcessor.DisplayHelpInfo();
                        AnyCommandProcess = true;
                        //PromptForConsoleInput();
                        continue;
                    }
                    else if (line.Equals("refresh", StringComparison.OrdinalIgnoreCase))
                    {
                        CurrentMessageToPassBackToAi += await commandProcessor.RefreshAvailableCommandsAsync(mcpClient);
                        AnyCommandProcess = true;
                        //PromptForConsoleInput();
                        continue;
                    }
                    else if (line.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    {
                        AllowForInput = true;
                        SignalBackToHumanControl = true;
                        break;
                    }


                    //Console.WriteLine($"--- Processing Line: '{trimmedLine}' ---"); // Added for clarity

                    // Try to process this individual line using your command processor
                    AnyCommandProcess = commandProcessor.ProcessUserInput(trimmedLine, out string method, out Dictionary<string, object?> parameters);
                    if (AnyCommandProcess)
                    {
                        Console.WriteLine($"Invoking command from line '{trimmedLine}'...");

                        try
                        {
                            // Pass the parameters extracted from this line
                            var response = await mcpClient.CallToolAsync(method, parameters);

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("Response:");
                            Console.ResetColor();

                            if (response is ModelContextProtocol.Protocol.Types.CallToolResponse toolResponse)
                            {
                                foreach (var content in toolResponse.Content)
                                {
                                    // Original response handling logic applied to the response for this line
                                    // Note: Removed redundant check `if (content.Type == "Text" && !String.IsNullOrEmpty(content.Text))`
                                    // as the subsequent check `if (!string.IsNullOrWhiteSpace(content.Text))` covers non-empty text.
                                    // If the original check had a different specific purpose, it might need to be adjusted.

                                    if (!string.IsNullOrWhiteSpace(content.Text))
                                    {
                                        Console.WriteLine(content.Text);
                                        // Be aware: This will be overwritten by the text from the *last* successfully processed line's response content.
                                        // If you need to accumulate results, you'll need a different approach (e.g., a List<string>).
                                        CurrentMessageToPassBackToAi += content.Text;
                                    }
                                    else if (content.Data is string data)
                                    {
                                        Console.WriteLine(data);
                                        // Consider if data should also update CurrentMessageToPassBackToAi
                                    }
                                    else if (content.Resource is { Uri: not null } resource)
                                    {
                                        Console.WriteLine($"[Resource]: {resource.Uri}");
                                        // Consider if resource URIs should update CurrentMessageToPassBackToAi
                                    }
                                    else
                                    {
                                        Console.WriteLine("[Unknown content format in response]");
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine(response?.ToString() ?? "[null response]");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            // Include the specific line that caused the error in the message
                            Console.WriteLine($"Error calling method for line '{trimmedLine}': {ex.Message}");
                            Console.ResetColor();
                            // Optionally decide if an error on one line should stop processing subsequent lines (e.g., add a 'break;' here)
                        }
                    }
                    else
                    {
                        // Handle cases where ProcessUserInput fails for a specific line
                        //Console.ForegroundColor = ConsoleColor.Yellow;
                        //Console.WriteLine($"Could not process input line: '{trimmedLine}'");
                        //Console.ResetColor();
                    }
                }

                if (!AnyCommandProcess && !SignalBackToHumanControl)
                {
                    CurrentMessageToPassBackToAi = "Okay, continue...";
                }
                PromptForConsoleInput();
            }
            Debug.WriteLine("Ready for Human input");
            CurrentMessageToPassBackToAi = Console.ReadLine();
        }
    }

    public static async Task Main_bak(string[] args)
    {


        GeminiAI MyGem = new GeminiAI("", "gemini-2.5-pro-exp-03-25");
        //Debug.WriteLine(await MyGem.SendChatMessageAsync("Hello my name is mike."));
        //Debug.WriteLine(await MyGem.SendChatMessageAsync("What is my name?"));



        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>();

        var (command, arguments) = GetCommandAndArguments(args);

        await using var mcpClient = await McpClientFactory.CreateAsync(new()
        {
            Id = "demo-server",
            Name = "Demo Server",
            TransportType = TransportTypes.Sse,
            Location = "http://localhost:3001/sse",
        });

        // Create a command processor
        var commandProcessor = new CommandProcessor();

        // Initialize with available commands from the server
        await commandProcessor.RefreshAvailableCommandsAsync(mcpClient);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("MCP Client Started!");
        Console.ResetColor();

        PromptForConsoleInput();
        while (Console.ReadLine() is string query && !"exit".Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                PromptForConsoleInput();
                continue;
            }

            // Special commands
            if (query.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                commandProcessor.DisplayHelpInfo();
                PromptForConsoleInput();
                continue;
            }
            else if (query.Equals("refresh", StringComparison.OrdinalIgnoreCase))
            {
                await commandProcessor.RefreshAvailableCommandsAsync(mcpClient);
                PromptForConsoleInput();
                continue;
            }

            // Process user command
            if (commandProcessor.ProcessUserInput(query, out string method, out Dictionary<string, object?> parameters))
            {
                Console.WriteLine($"Invoking {method}...");

                try
                {
                    // Pass the parameters as IReadOnlyDictionary<string, object?>
                    var response = await mcpClient.CallToolAsync(method, parameters);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Response:");
                    Console.ResetColor();

                    if (response is ModelContextProtocol.Protocol.Types.CallToolResponse toolResponse)
                    {
                        foreach (var content in toolResponse.Content)
                        {
                            if (content.Type == "Text" && !String.IsNullOrEmpty(content.Text))
                            {
                                continue;
                            }
                            if (!string.IsNullOrWhiteSpace(content.Text))
                            {
                                Console.WriteLine(content.Text);
                            }
                            else if (content.Data is string data)
                            {
                                Console.WriteLine(data);
                            }
                            else if (content.Resource is { Uri: not null } resource)
                            {
                                Console.WriteLine($"[Resource]: {resource.Uri}");
                            }
                            else
                            {
                                Console.WriteLine("[Unknown content format]");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine(response?.ToString() ?? "[null response]");
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error calling method: {ex.Message}");
                    Console.ResetColor();
                }
            }

            PromptForConsoleInput();
        }
    }

    static bool TryParseGeminiCommand(string? text, out string commandName, out Dictionary<string, object?> parameters)
    {
        // ... (Implementation from previous step using Regex) ...
        commandName = string.Empty;
        parameters = new Dictionary<string, object?>();
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string? commandLine = lines.FirstOrDefault(line => line.Trim().StartsWith("COMMAND:", StringComparison.OrdinalIgnoreCase));
        if (commandLine == null) return false;
        string commandPart = commandLine.Substring("COMMAND:".Length).Trim();
        var parts = commandPart.Split(new[] { ' ' }, 2);
        commandName = parts[0];
        if (parts.Length > 1)
        {
            MatchCollection matches = Regex.Matches(parts[1], "(\\w+)\\s*=\\s*\\\"([^\\\"]*)\\\"");
            foreach (Match match in matches)
            {
                if (match.Groups.Count == 3) parameters[match.Groups[1].Value] = match.Groups[2].Value;
            }
            // Basic fallback
            if (!parameters.Any())
            {
                var paramParts = parts[1].Split(' ');
                foreach (var p in paramParts)
                {
                    var kv = p.Split('=');
                    if (kv.Length == 2) parameters[kv[0]] = kv[1];
                }
            }
        }
        return true;
    }

    static string FormatMcpResponse(object? mcpResponse)
    {
        // ... (Implementation from previous step formatting CallToolResponse) ...
        if (mcpResponse == null) return "Tool executed successfully, but returned no specific data (null response).";
        if (mcpResponse is ModelContextProtocol.Protocol.Types.CallToolResponse toolResponse)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tool execution result:");
            if (toolResponse.Content == null || !toolResponse.Content.Any()) { sb.AppendLine("[No content returned]"); }
            else
            {
                foreach (var content in toolResponse.Content)
                {
                    if (!string.IsNullOrWhiteSpace(content.Text)) sb.AppendLine($"Text: {content.Text}");
                    else if (content.Data is string data && !string.IsNullOrWhiteSpace(data)) sb.AppendLine($"Data: {data}");
                    else if (content.Resource is { Uri: not null } resource) sb.AppendLine($"Resource: {resource.Uri}");
                    else sb.AppendLine("[Unknown content format in response]");
                }
            }
            return sb.ToString();
        }
        return $"Tool execution result: {mcpResponse}";
    }

    static void PromptForInput()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Enter command (help, 'refresh', 'history', 'clear', 'exit') or message for AI > ");
        Console.ResetColor();
    }

    // for x64dbg Debugger
    //static string TaskString =
    //    Before you start you should also call Help to see the latest function, 
    //    then find the command to show you how to load the application.While reviewing the Debugger commands you'll have to review the ""DebugControl"" area of the documentation.
    //    Once you have tha Application loaded in the debugger, use 'Refresh' to see new available commands as well and 'Help' again to see their updated documentation.
    //    If the command returns ""True"", then the command was successfully executed and you may move to the next step.
    //    "Your current task is to: Find the command to load the application located at 'C:\\InjectGetTickCount\\InjectSpeed.exe', then get the list of loaded modules for that application process. Finally, report the total count of modules found and list their names.\r\n        " +
    //    "Once that task is completed, start labeling each important function within the disassembly of the main application, ignore the JMP table at the top of the binary for this task. REMEMBER, THIS IS A 64BIT APPLICAITON!\r\n        " +
    //    "Once you have labeled 10 critical parts of the code, update the code to point to calc.exe instead of TenMilesToSafety-Win64-Shipping.exe
    //    To get the Modules EntryPoint use the command: DbgValFromString value=""?entry""
    //    To get the base address of a module use the command: DbgValFromString value = ""ModuleName.exe""
    //    To get the result of the last command executed use: DbgValFromString value = ""$VariableName"" Example: DbgValFromString value = ""$result""
    //    To get address results from findallmem scans: DbgValFromString value = ref.addr(zeroBasedIndex) Example: DbgValFromString value = ref.addr(0)
    //    ";

    //static string TaskString =
    //    Before you start you should also call Help to see the latest function, 
    //    then find the command to show you how to load the application.While reviewing the Debugger commands you'll have to review the ""DebugControl"" area of the documentation.
    //    Once you have tha Application loaded in the debugger, use 'Refresh' to see new available commands as well and 'Help' again to see their updated documentation.
    //    If the command returns ""True"", then the command was successfully executed and you may move to the next step.
    //    "Your current task is to: Find the command to load the application located at 'D:\\SteamLibrary\\steamapps\\common\\10 Miles To Safety\\TenMilesToSafety\\Binaries\\Win64\\TenMilesToSafety-Win64-Shipping.exe', " +
    //    "then find which method is best to create a Speedhack for the video game. You may label and comment the binary, " +
    //    "but do not write any memory to the binary. Once done, state the exact steps required for the least amount of resistant to pull off the speed hack.
    //    To get the Modules EntryPoint use the command: DbgValFromString value=""?entry""
    //    To get the base address of a module use the command: DbgValFromString value = ""ModuleName.exe""
    //    To get the result of the last command executed use: DbgValFromString value = ""$VariableName"" Example: DbgValFromString value = ""$result""
    //    To get address results from findallmem scans: DbgValFromString value = ref.addr(zeroBasedIndex) Example: DbgValFromString value = ref.addr(0)
    //    ";

    static string TaskString = "Your current task is to: Test and validate all function are working as expected. To see new available commands use 'Help'";

    static string initialPrompt = $@"You are an AI assistant with access to an MCP (Model Context Protocol) server. Your goal is to complete tasks by calling the available commands on this server.
        When you need to execute a command, output ONLY the command on a line, followed by the command name and parameters in the format: paramName=""value"". 
        Example: 
        my_command input_path=""C:\\path\\file.txt"", verbose=""true""

        Wait for the result of the command before deciding your next step. I will provide the result of each command you issue.

        {TaskString}

        If a command fails to work as expected, ensure your Quotes and Commas are in the correct place!!!

        Start by determining the first command you need to issue to begin this task. Remember to not use any prefix when you want to execute a command, just the command and the arguments itself.

        # MCP Integration Guide for AI Assistants

        ## Overview
        You are connected to a Model Context Protocol(MCP) server that provides dynamic capabilities through various tools and commands.
        These tools are not fixed but can change over time as the server evolves, requiring you to adapt to new capabilities.

        This guide will help you understand how to discover and interact with the available tools correctly.

        ## Tool Discovery
        When interacting with users, you will receive information about available tools from the MCP server. Each tool has the following properties:
        - `name`: The unique identifier of the tool
        - `description`: A human-readable description of what the tool does
        - `inputSchema`: A schema describing the parameters the tool accepts

        ## Command Execution

        ### Basic Command Structure
        When a user asks you to perform a task that requires using a server tool, follow this pattern:
        1. Determine which tool is appropriate for the task
        2. Format the parameters according to the tool's inputSchema
        3. Call the tool with properly formatted parameters

        ### Parameter Types
        Tools may require different parameter types:
        - **Strings**: Simple text values (`""example""`)
        - **Numbers**: Integer or decimal values (`42`, `3.14`)
        - **Booleans**: True/false values (`true`, `false`)
        - **Arrays**: Collections of values, which must be properly formatted

        ### Working with Commands with multiple arguments or parameters
        When executing a command with multiple arguments or parameters, ensure each argument is separated by a Comma "","" followed by the variable=value. DO NOT ESCAPE SPECIAL CHARATERS!
        1. **Multiple arguments / parameters** use Commas to separate each parameter=value from the next. 
           ```
           Command Param1=Value1, Param2=Value2, Param3=Value3
           Command Param1=C:\\Path To\Program.exe, Param2=ArgumentValue2, Param3=ArgVal3
           ```

        ### Working with Arrays
        Many tools accept array parameters. When passing arrays, format them as follows:
        1. **String Arrays**: Use the pipe (`|`) separator between elements
           ```
           Command arrayParam=value1|value2|value3
           ```

        2. **Nested Arrays**: For more complex nested arrays, follow the specific format required by the tool's schema

        ## Example Scenarios
        Here are examples showing how to interact with various tools:

        ### Example 1: Simple String Parameter
        If a tool named ""Echo"" requires a ""message"" parameter:
        ```
        Echo message=Hello world
        ```
        ### Example 2: Multiple Parameters
        If a tool named ""GetWeather"" requires ""latitude"" and ""longitude"" parameters separate each parameters by a comma "","":
        ```
        GetWeather latitude=40.7128, longitude=-74.0060
        ```
        If command fails, return message will be -> Missing required parameter...

        ### Example 3: Array Parameter
        If a tool named ""ProcessItems"" requires an array of strings:
        ```
        ProcessItems items=apple|banana|orange
        ```

        ### Example 4: Complex Parameters
        If a tool has multiple parameter types, ensure to separate each Param by a comma "","":
        ```
        AnalyzeData values=10|20|30,threshold=5.5,enableFiltering=true
        ```
        ### Example 5: Batching multiple commands
        Always batch commands when possible.
        ```
        I will attempt to batch these three commands
        AnalyzeData values=10|20|30,threshold=5.5,enableFiltering=true
        GetWeather latitude=40.7128, longitude=-74.0060
        Echo message=Hello world
        ```

        ## Best Practices
        1. **Always check available tools first** before attempting to use them
        2. **Review parameter requirements** in the tool's inputSchema
        3. **Format parameters correctly** according to their expected types
        4. **Handle errors gracefully** if a tool is unavailable or parameters are invalid
        5. **Use command aliases** when appropriate (some commands may have shorthand aliases)
        6. **Do not escape out quotes or symbols within the command line arguments, especially when using ExecuteDebuggerCommand.

        ## Parameter Validation
        Before calling a tool, ensure:
        1. All required parameters are provided
        2. Parameter values match the expected types
        3. Array parameters are properly formatted with the pipe separator

        ## Handling Tool Updates
        Since the server capabilities can change, periodically check for updated tools during long conversations. If a user reports a tool isn't working as expected, recommend refreshing the available tools list.

        ## Common Errors and Solutions
        - **""Unknown command""**: The tool name may have changed or been removed. Check available tools.
        - **""Missing required parameter""**: Ensure all required parameters are provided.
        - **""Cannot convert parameter""**: Ensure parameter types match what the tool expects.

        Remember, the MCP server's capabilities are dynamic. Always adapt to the current set of available tools rather than assuming specific tools will be available.

        Once all task are completed type: exit
        ";

    static void PromptForConsoleInput()
    {
        Console.WriteLine("Enter a command (or exit to quit or help for available commands):");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("> ");
        Console.ResetColor();
    }

    /// <summary>
    /// Determines the command (executable) to run and the script/path to pass to it.
    /// </summary>
    static (string command, string arguments) GetCommandAndArguments(string[] args)
    {
        return ("C:\\Users\\User\\source\\repos\\mcp-csharp-sdk\\artifacts\\bin\\QuickstartWeatherServer\\Debug\\net8.0\\QuickstartWeatherServer.exe", "");
    }
}

// Create a command processor to handle dynamic commands
public class CommandProcessor
{
    // Helper class to store command information
    private class CommandInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object?> Parameters { get; set; }
        public List<string> RequiredParameters { get; set; }
        public Dictionary<string, string> ParameterTypes { get; set; }
        public Dictionary<string, string> ParameterDescriptions { get; set; }

        public CommandInfo(string name, string description)
        {
            Name = name;
            Description = description;
            Parameters = new Dictionary<string, object?>();
            RequiredParameters = new List<string>();
            ParameterTypes = new Dictionary<string, string>();
            ParameterDescriptions = new Dictionary<string, string>();
        }
    }

    // Dictionary to store available commands
    private Dictionary<string, CommandInfo> availableCommands = new Dictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CommandInfo> commandAliases = new Dictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);



    // Call this when initializing the application or when you want to refresh the commands
    public async Task<string> RefreshAvailableCommandsAsync(IMcpClient mcpClient)
    {
        // Use StringBuilder for efficient string construction
        StringBuilder outputLog = new StringBuilder();

        // Clear existing commands first
        availableCommands.Clear();
        commandAliases.Clear();

        try
        {
            var tools = await mcpClient.ListToolsAsync();
            foreach (var tool in tools)
            {
                string registerMsg = $"Registering tool: {tool.Name}";
                Console.WriteLine(registerMsg);
                outputLog.AppendLine(registerMsg);

                var command = new CommandInfo(tool.Name, tool.Description);

                // Parse JSON schema to extract parameter information
                // Check if JsonSchema is an object before trying to access properties
                if (tool.JsonSchema.ValueKind == JsonValueKind.Object)
                {
                    // Safely try to get the 'properties' property
                    if (tool.JsonSchema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in properties.EnumerateObject())
                        {
                            string paramName = property.Name;
                            string paramType = "string"; // Default type
                            string paramDescription = "";

                            if (property.Value.TryGetProperty("type", out var typeElement))
                            {
                                paramType = typeElement.GetString() ?? "string";
                            }

                            if (property.Value.TryGetProperty("description", out var descElement))
                            {
                                paramDescription = descElement.GetString() ?? "";
                            }

                            command.ParameterTypes[paramName] = paramType;
                            command.ParameterDescriptions[paramName] = paramDescription;
                        }
                    }

                    // Get required parameters
                    if (tool.JsonSchema.TryGetProperty("required", out var requiredElement) &&
                        requiredElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in requiredElement.EnumerateArray())
                        {
                            string? reqParam = item.GetString();
                            if (!string.IsNullOrEmpty(reqParam))
                            {
                                command.RequiredParameters.Add(reqParam);
                            }
                        }
                    }
                } // End of JsonSchema processing

                // Add command to dictionary
                availableCommands[tool.Name] = command;

                // Add command alias (lowercase version)
                commandAliases[tool.Name.ToLowerInvariant()] = command;
            }

            string successMsg = $"Registered {availableCommands.Count} commands from the server.";
            Console.WriteLine(successMsg);
            outputLog.AppendLine(successMsg);

            //string exampleHeader = $"-Here are some example call formats-";
            //Console.WriteLine(exampleHeader);
            //outputLog.AppendLine(exampleHeader);

            //string example1 = $"sampleLLM prompt=hi, maxTokens=5";
            //Console.WriteLine(example1);
            //outputLog.AppendLine(example1);

            //string example2 = $"MyarrayFunction arg=String1|String2|string3";
            //Console.WriteLine(example2);
            //outputLog.AppendLine(example2);

        }
        catch (Exception ex)
        {
            string errorMsg = $"Error refreshing commands: {ex.Message}";
            Console.WriteLine(errorMsg);
            outputLog.AppendLine(errorMsg); // Append the error message to the log as well
        }

        // Return the accumulated log string in all cases (success or error)
        return outputLog.ToString();
    }

    // Process user input and map to appropriate commands
    public bool ProcessUserInput(string userInput, out string method, out Dictionary<string, object?> parameters)
    {
        method = string.Empty;
        parameters = new Dictionary<string, object?>();

        if (string.IsNullOrWhiteSpace(userInput))
            return false;

        // Split input into command and arguments
        string[] parts = userInput.Split(new[] { ' ' }, 2);
        string commandName = parts[0].ToLowerInvariant();
        string args = parts.Length > 1 ? parts[1] : string.Empty;

        CommandInfo? command = null;

        // Check for direct command match
        if (availableCommands.TryGetValue(commandName, out command) || commandAliases.TryGetValue(commandName, out command))
        {
            // Command found directly
        }
        else
        {
            //Console.WriteLine($"Unknown command: {commandName}");
            //Console.WriteLine("Available commands:");
            //foreach (var cmd in availableCommands.Values.Distinct())
            //{
            //    Console.WriteLine($"- {cmd.Name}: {cmd.Description}");
            //}
            return false;
        }

        method = command.Name;

        // Parse parameters based on schema
        if (!TryParseParameters(args, command, parameters))
        {
            return false;
        }

        // Check if all required parameters are provided
        foreach (var requiredParam in command.RequiredParameters)
        {
            if (!parameters.ContainsKey(requiredParam))
            {
                Console.WriteLine($"Missing required parameter: {requiredParam}");
                Console.WriteLine($"Usage: {command.Name} {string.Join(", ", command.RequiredParameters.Select(p => p + "=<value>"))}");
                return false;
            }
        }

        return true;
    }

    // Helper method to parse parameters
    private bool TryParseParameters(string args, CommandInfo command, Dictionary<string, object?> parameters)
    {
        try
        {
            if (!string.IsNullOrEmpty(args))
            {
                // Parse as key=value pairs while handling quotes and commas correctly
                var argPairs = SplitArgumentsRespectingQuotes(args);
                foreach (var pair in argPairs)
                {
                    var keyValue = pair.Split(new[] { '=' }, 2);
                    if (keyValue.Length == 2)
                    {
                        string key = keyValue[0].Trim();
                        string value = keyValue[1].Trim().Trim('"'); // remove surrounding quotes if any

                        if (command.ParameterTypes.TryGetValue(key, out var paramType))
                        {
                            switch (paramType.ToLowerInvariant())
                            {
                                case "integer":
                                    parameters[key] = int.TryParse(value, out var intValue) ? intValue : 0;
                                    break;
                                case "number":
                                    parameters[key] = double.TryParse(value, out var dblValue) ? dblValue : 0.0;
                                    break;
                                case "boolean":
                                    parameters[key] = value.ToLowerInvariant() == "true";
                                    break;
                                case "array":
                                    parameters[key] = value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                                    break;
                                default:
                                    parameters[key] = value;
                                    break;
                            }
                        }
                        else
                        {
                            parameters[key] = value;
                        }
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing command parameters: {ex.Message}");
            return false;
        }
    }

    private List<string> SplitArgumentsRespectingQuotes(string input)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c); // Keep quotes in case needed later
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
            result.Add(sb.ToString().Trim());

        return result;
    }



    // Helper method to display information about available commands
    public string DisplayHelpInfo()
    {
        // Use StringBuilder for efficient string construction
        StringBuilder helpTextBuilder = new StringBuilder();

        // --- Available Commands Section ---
        string availableCommandsHeader = "Help Menu and Available commands:";
        Console.WriteLine(availableCommandsHeader);
        helpTextBuilder.AppendLine(availableCommandsHeader);

        foreach (var cmd in availableCommands.Values) // Assuming availableCommands is accessible
        {
            // Command Name and Description
            string cmdLine = $"- {cmd.Name}: {cmd.Description}";
            Console.WriteLine(cmdLine);
            helpTextBuilder.AppendLine(cmdLine);

            // Required Parameters
            if (cmd.RequiredParameters != null && cmd.RequiredParameters.Count > 0)
            {
                string reqParamLine = $"  Required parameters: {string.Join(", ", cmd.RequiredParameters)}";
                Console.WriteLine(reqParamLine);
                helpTextBuilder.AppendLine(reqParamLine);
            }

            // Parameter Details
            if (cmd.ParameterTypes != null && cmd.ParameterTypes.Count > 0)
            {
                string paramsHeader = "  Parameters:";
                Console.WriteLine(paramsHeader);
                helpTextBuilder.AppendLine(paramsHeader);

                foreach (var param in cmd.ParameterTypes)
                {
                    // Safely get description, default to empty string if not found
                    string description = (cmd.ParameterDescriptions != null && cmd.ParameterDescriptions.TryGetValue(param.Key, out var desc))
                                         ? desc
                                         : "";
                    string paramDetailLine = $"    {param.Key} ({param.Value}): {description}";
                    Console.WriteLine(paramDetailLine);
                    helpTextBuilder.AppendLine(paramDetailLine);
                }
            }

            // Blank line between commands
            Console.WriteLine();
            helpTextBuilder.AppendLine();
        }

        // Blank line before special commands
        Console.WriteLine();
        helpTextBuilder.AppendLine();

        // --- Special Commands Section ---
        string specialCommandsHeader = "Special commands:";
        Console.WriteLine(specialCommandsHeader);
        helpTextBuilder.AppendLine(specialCommandsHeader);

        string helpLine = "- help: Display this help information";
        Console.WriteLine(helpLine);
        helpTextBuilder.AppendLine(helpLine);

        string refreshLine = "- refresh: Refresh the list of available commands from the server";
        Console.WriteLine(refreshLine);
        helpTextBuilder.AppendLine(refreshLine);

        string exitLine = "- exit: Exit the application";
        Console.WriteLine(exitLine);
        helpTextBuilder.AppendLine(exitLine);

        // Return the complete help string
        return helpTextBuilder.ToString();
    }
}