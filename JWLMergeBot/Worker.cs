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
using JWLMergeBot.Helpers;

namespace JWLMergeBot
{
    class Worker : BackgroundService
    {
        public static ITelegramBotClient botClient;
        private static int ConnectionDelay = 5000;
        public static ILogger<Worker> Logger;
        private bool Disconnected = false;

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

                // Try to connect
                while(!cancellationToken.IsCancellationRequested)
                {
                    try { 
                        await botClient.ReceiveAsync(
                                HandleUpdateAsync,
                                HandleErrorAsync,
                                new ReceiverOptions
                                {
                                    AllowedUpdates = { /*UpdateType.Message, UpdateType.CallbackQuery */},
                                    ThrowPendingUpdates = false
                                },
                                cancellationToken
                            );
                    }
                    catch (Telegram.Bot.Exceptions.RequestException e)
                    {
                        Thread.Sleep(ConnectionDelay);
                    }
                }
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
            // An update received: clear DisconnectionArise flag
            Disconnected = false;

            // If the update is a message
            if (update.Message is Message message)
            {
                // Check if this chat is banned. If so, stop processing the message
                if(Banner.CheckIsChatBanned(message.Chat.Id))
                {
                    Logger.LogWarning(string.Format(Strings.message_dropped, GetFormattedChatName(message.Chat)));
                    return;
                }

                if (message.Text != null)
                {
                    // Gotta somethings
                    Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_text, GetFormattedChatName(message.Chat), message.Text));

                    // Process command
                    Logic.OnCommand(message, message.Text, false);
                }
                else if (message.Document != null)
                {
                    // Gotta somethings
                    Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_file, GetFormattedChatName(message.Chat), message.Document.FileName));

                    // Process file
                    Logic.OnFile(message);
                }
                else if (message.Type != MessageType.Sticker)
                {
                    // Gotta somethings
                    Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_unhandled, GetFormattedChatName(message.Chat), message.Type.ToString()));

                    // If another unhandled type of content
                    Logic.OnOtherContent(message);
                }
            } else 
            // If the update is a callback query
            if (update.CallbackQuery is CallbackQuery callbackQuery)
            {
                // Gotta somethings
                Logger.LogInformation(string.Format(Strings.received_something, Strings.message_type_callbackquery, GetFormattedChatName(callbackQuery.Message.Chat), callbackQuery.Data));

                // Answer to the callback (in this way you indicate you got it)
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);

                // Process command
                Logic.OnCommand(callbackQuery.Message, callbackQuery.Data, true);
            }
        }

        private static String GetFormattedChatName(Chat chat)
        {
            return string.Format(Strings.chat_name, (chat.FirstName + " " + chat.LastName).Trim(), (chat.Username != null ? " @" + chat.Username : ""), chat.Id);
        }

        async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            if(!Disconnected)
                Logger.LogError(message: Strings.bot_disconnected /*, exception: exception*/);
            Disconnected = true;

            // Wait a bit (otherwise botClient will try to reconnect with a bit too zeal)
            Thread.Sleep(ConnectionDelay);
        }
    }
}
