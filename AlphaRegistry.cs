using SeeSharp.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace SeeSharp
{
    public class AnthropicToolRegistry : ToolKit
    {
        const string ANTHROPIC_SUMMARIZE_TOOL_NAME = "ANTHROPIC_SUMMARIZE";
        public new Dictionary<string, string> GetToolkitInformation()
        {
            Dictionary<string, string> result = base.GetToolkitInformation();
            result.Add(ANTHROPIC_SUMMARIZE_TOOL_NAME,
                "Summarizes a long text into a concise summary. \r\n    :param text: The long text to summarize.\r\n    :return: A concise summary of the input text.");
            return result;
        }
        public Dictionary<string, object> AnthropicSummarize_Tool(string text, ConsoleColor AgentToolColor)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            Console.ForegroundColor = AgentToolColor;
            Console.WriteLine($"[Tool] Summarizing text with Anthropic API.");
            // Placeholder for actual summarization logic using Anthropic API
            string summary = $"[Summary of the provided text: {text.Substring(0, Math.Min(100, text.Length))}...]";
            result.Add("summary", summary);
            return result;
        }
    }   
}
