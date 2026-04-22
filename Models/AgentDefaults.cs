using System;
using System.Collections.Generic;
using System.Text;

namespace SeeSharp.Models
{
    public static class AgentDefaults
    {

        public static readonly ConsoleColor YouColor = ConsoleColor.Magenta;
        public static readonly ConsoleColor AgentColor = ConsoleColor.DarkCyan;
        public static readonly ConsoleColor ErrorColor = ConsoleColor.Red;
        public static readonly ConsoleColor ResetColor = ConsoleColor.White;
        public static readonly ConsoleColor AgentToolColor = ConsoleColor.Green;
        public static readonly ConsoleColor AgentReasoningColor = ConsoleColor.DarkCyan;
        public static readonly ConsoleColor AgentActionColor = ConsoleColor.Yellow;

        public static readonly string LIST_FILE_TOOL_NAME = "LIST_FILE";
        public static readonly string EDIT_FILE_TOOL_NAME = "EDIT_FILE";
        public static readonly string READ_TOOL_NAME = "READ_FILE";
        public static readonly string WEB_CALL_TOOL_NAME = "WEB_CALL";
        public static readonly string BASH_TOOL_NAME = "BASH";
    }
}
