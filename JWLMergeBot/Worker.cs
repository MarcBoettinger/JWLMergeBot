using JWLMergeBot.Properties;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;

namespace JWLMergeBot
{
    class Worker : BackgroundService
    {
        public static ITelegramBotClient botClient;
        private static int ConnectionDelay = 5000;
        public static ILogger<Worker> Logger;

        public Worker(ILogger<Worker> workerLogger)
        {
            Logger = workerLogger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Begin
            Logger.LogInformation(string.Format(Strings.bot_started, Assembly.GetEntryAssembly().GetName().Version.ToString()));

            // Print working directory
            Logger.LogInformation(FileHandling.AppDataPath);

            try
            {
                // Init Telegram Bot Client
                botClient = new TelegramBotClient(ConfigFile.Load().BotToken);

                // Listen for messages
                botClient.OnMessage += Bot_OnMessage;
                botClient.OnCallbackQuery += BotClient_OnCallbackQuery;
                botClient.OnReceiveGeneralError += BotClient_OnReceiveGeneralError;
                botClient.StartReceiving();

            }
            catch (FileNotFoundException fnfe)
            {
                Logger.LogError(message: Strings.config_not_found, exception: fnfe);
            }
            catch (ArgumentException aex)
            {
                Logger.LogError(message: Strings.invalid_token, exception: aex);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(5000, stoppingToken);
            }

            // End gracefully
            botClient.StopReceiving();
        }

        public static void BotClient_OnReceiveGeneralError(object sender, ReceiveGeneralErrorEventArgs e)
        {
            // If an error occours, stop listening for a while...
            botClient.StopReceiving();
            // ...wait...
            Thread.Sleep(ConnectionDelay);
            // ...and then try to, restart
            botClient.StartReceiving();
        }

        public static void Bot_OnMessage(object sender, MessageEventArgs e)
        {
            if (e.Message.Text != null)
            {
                // Gotta somethings
                Worker.Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_text, (e.Message.Chat.FirstName + " " + e.Message.Chat.LastName).Trim(), e.Message.Chat.Id, e.Message.Text));

                // Process command
                Logic.OnCommand(e.Message, e.Message.Text, false);
            }
            else if (e.Message.Document != null)
            {
                // Gotta somethings
                Worker.Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_file, e.Message.Chat.FirstName + " " + e.Message.Chat.LastName, e.Message.Chat.Id, e.Message.Document.FileName));

                // Process file
                Logic.OnFile(e.Message);
            }
            else if(e.Message.Type != MessageType.Sticker)
            { 
                // Gotta somethings
                Worker.Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_unhandled, e.Message.Chat.FirstName + " " + e.Message.Chat.LastName, e.Message.Chat.Id, e.Message.Type.ToString()));

                // If another unhandled type of content
                Logic.OnOtherContent(e.Message);
            }
        }

        public static async void BotClient_OnCallbackQuery(object sender, CallbackQueryEventArgs e)
        {
            // Gotta somethings
            Worker.Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_callbackquery, (e.CallbackQuery.Message.Chat.FirstName + " " + e.CallbackQuery.Message.Chat.LastName).Trim(), e.CallbackQuery.Message.Chat.Id, e.CallbackQuery.Data));

            // Answer to the callback (in this way you indicate you got it)
            await botClient.AnswerCallbackQueryAsync(e.CallbackQuery.Id);

            // Process command
            Logic.OnCommand(e.CallbackQuery.Message, e.CallbackQuery.Data, true);
        }
    }
}
