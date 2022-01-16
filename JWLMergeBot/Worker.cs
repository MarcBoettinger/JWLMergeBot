using JWLMergeBot.Properties;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;

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

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            // Begin
            Logger.LogInformation(string.Format(Strings.bot_started, Assembly.GetEntryAssembly().GetName().Version.ToString()));

            // Print working directory
            Logger.LogInformation(FileHandling.AppDataPath);

            try
            {
                // Init Telegram Bot Client
                botClient = new TelegramBotClient(AppConfig.Load().BotToken);

                // Listen for messages
                startReceiving(cancellationToken);

            }
            catch (FileNotFoundException fnfe)
            {
                Logger.LogError(message: Strings.config_not_found, exception: fnfe);
            }
            catch (ArgumentException aex)
            {
                Logger.LogError(message: Strings.invalid_token, exception: aex);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(5000, cancellationToken);
            }
        }

        async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // If the update is a message
            if (update.Message is Message message)
            {
                if (message.Text != null)
                {
                    // Gotta somethings
                    Worker.Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_text, (message.Chat.FirstName + " " + message.Chat.LastName).Trim(), message.Chat.Id, message.Text));

                    // Process command
                    Logic.OnCommand(message, message.Text, false);
                }
                else if (message.Document != null)
                {
                    // Gotta somethings
                    Worker.Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_file, message.Chat.FirstName + " " + message.Chat.LastName, message.Chat.Id, message.Document.FileName));

                    // Process file
                    Logic.OnFile(message);
                }
                else if (message.Type != MessageType.Sticker)
                {
                    // Gotta somethings
                    Worker.Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_unhandled, message.Chat.FirstName + " " + message.Chat.LastName, message.Chat.Id, message.Type.ToString()));

                    // If another unhandled type of content
                    Logic.OnOtherContent(message);
                }
            } else 
            // If the update is a callback query
            if (update.CallbackQuery is CallbackQuery callbackQuery)
            {
                // Gotta somethings
                Worker.Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_callbackquery, (callbackQuery.Message.Chat.FirstName + " " + callbackQuery.Message.Chat.LastName).Trim(), callbackQuery.Message.Chat.Id, callbackQuery.Data));

                // Answer to the callback (in this way you indicate you got it)
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);

                // Process command
                Logic.OnCommand(callbackQuery.Message, callbackQuery.Data, true);
            }
        }

        async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            // If an error occours, stop listening for a while...
            Thread.Sleep(ConnectionDelay);
            // ...and then try to, restart
            startReceiving(cancellationToken);
        }

        async private void startReceiving(CancellationToken cancellationToken)
        {
            try { 
            await botClient.ReceiveAsync(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    new ReceiverOptions
                    {
                        AllowedUpdates = { /*UpdateType.Message, UpdateType.CallbackQuery */},
                        ThrowPendingUpdates = true
                    },
                    cancellationToken
                );
            }catch(Telegram.Bot.Exceptions.RequestException e)
            {
                Logger.LogError(message: "Connection error", exception: e);
            }
        }
    }
}
