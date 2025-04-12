This is a sample c# MCP client for use with the x64Dbg MCP Server.

To use import the environment variables or hardcode the values into 

Ln. 564 (QuickstartClient\Program.cs) - string? GeminiAIKey = Environment.GetEnvironmentVariable("GeminiAIKey");
Ln. 565 (QuickstartClient\Program.cs)- string? MCPServerIP = Environment.GetEnvironmentVariable("MCPServerIP");

Once completed, Update the prompt to use your intended usecase (Also located in Program.cs), then start.

The client is expected to connect to the MCP Server and will start issuing commands to begin its debug process within X64DBG