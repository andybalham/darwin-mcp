using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

[McpServerToolType]
public class EchoTool
{
    [McpServerTool, Description("Echoes back the message you send. Use this to verify the MCP server is working.")]
    public static string EchoMessage(
        [Description("The message to echo back")] string message)
    {
        return $"Echo: {message}";
    }
}
