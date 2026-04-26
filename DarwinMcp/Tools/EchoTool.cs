using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

// [McpServerToolType] marks this class as a container of MCP tools so the
// reflection scanner in WithTools<EchoTool>() picks it up. Without it, the
// [McpServerTool] methods inside would not be discovered.
[McpServerToolType]
public class EchoTool
{
    // [McpServerTool] exposes this method to MCP clients. The [Description]
    // text becomes the tool's `description` field in tools/list — this is what
    // the LLM reads to decide whether to call the tool, so write it for the LLM,
    // not for human developers.
    //
    // Method name → tool name (PascalCase auto-converted to snake_case):
    //   EchoMessage → echo_message
    //
    // Parameter types → JSON Schema:
    //   string message  → {"type":"string","required":true}
    // Per-parameter [Description] attributes feed the schema's property descriptions.
    //
    // Return type → MCP CallToolResult content:
    //   string return  → single text content block
    [McpServerTool, Description("Echoes back the message you send. Use this to verify the MCP server is working.")]
    public static string EchoMessage(
        [Description("The message to echo back")] string message)
    {
        return $"Echo: {message}";
    }
}
