This is a sample C# MCP client designed for use with the x64Dbg MCP Server.

To get started, either import the necessary environment variables or hardcode the values directly into the following lines:

Line 564 (QuickstartClient\Program.cs):
string? GeminiAIKey = Environment.GetEnvironmentVariable("GeminiAIKey");

Line 565 (QuickstartClient\Program.cs):
string? MCPServerIP = Environment.GetEnvironmentVariable("MCPServerIP");

After setting the values, update the prompt to reflect your intended use case (also located in Program.cs), then run the application.

While running, your AI model will automatically begin executing tasks. You may hold Shift to interrupt its next action and manually type commands if needed.

The client is expected to connect to the MCP Server and will begin issuing commands to start the debugging process within x64Dbg.
