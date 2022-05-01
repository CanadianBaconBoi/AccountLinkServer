using Discord;
using log4net;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OblivionUtils.Services
{
    public static class Logger
    {
        private static ILog _log = LogManager.GetLogger("Default");
        private static ILog _discordLog = LogManager.GetLogger("Discord.NET");
        public static Task DiscordLogger(LogMessage message)
        {
            switch (message.Severity)
            {
                case LogSeverity.Critical:
                    _discordLog.Fatal($"{message.Source}: {message.Message} {message.Exception}");
                    break;

                case LogSeverity.Error:
                    _discordLog.Error($"{message.Source}: {message.Message} {message.Exception}");
                    break;

                case LogSeverity.Warning:
                    _discordLog.Warn($"{message.Source}: {message.Message} {message.Exception}");
                    break;

                case LogSeverity.Info:
                    _discordLog.Info($"{message.Source}: {message.Message} {message.Exception}");
                    break;

                case LogSeverity.Verbose:
                case LogSeverity.Debug:
                    _discordLog.Debug($"{message.Source}: {message.Message} {message.Exception}");
                    break;
            }
            return Task.CompletedTask;
        }

        public static void Log(LogLevel level, string message)
        {
            switch(level)
            {
                case LogLevel.Critical:
                    _log.Fatal(message);
                    break;
                case LogLevel.Error:
                    _log.Error(message);
                    break;
                case LogLevel.Warning:
                    _log.Warn(message);
                    break;
                case LogLevel.Information:
                    _log.Info(message);
                    break;
                case LogLevel.Trace:
                case LogLevel.Debug:
                    _log.Debug(message);
                    break;
            }
        }
    }
}
