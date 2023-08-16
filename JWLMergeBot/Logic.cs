using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types;
using Telegram.Bot;
using JWLMerge.BackupFileServices;
using JWLMerge.BackupFileServices.Models;
using Telegram.Bot.Types.ReplyMarkups;
using JWLMergeBot.Properties;
using static JWLMergeBot.FileHandling;
using Microsoft.Extensions.Logging;
using Polly;
using JWLMerge.BackupFileServices.Exceptions;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using JWLMergeBot.Models;

namespace JWLMergeBot
{
    class Logic
    {
        public static async void OnFile(Message message)
        {
            // Set user language
            ChatConfig.Load(message.Chat.Id).ApplyLanguage();

            // Check if I can handle the file (if a temporary file exist, it means I'm working on it...)
            if (FileHandling.IsTempFileBusy(message.Chat.Id))
            {
                // Feedback
                await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.busy, message.Document.FileName).Replace("\\n", "\n"));
                return;
            }

            // Check if the received file is a jwlibrary file
            if (!FileHandling.IsValidFileExtension(message.Document.FileName))
            {
                // Feedback
                await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.wrong_filetype);
                return;
            }

            // Check the incoming file size
            if (!FileHandling.IsValidFileSize((long)(message.Document.FileSize)))
            {
                // Feedback
                await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.max_filesize);
                return;
            }

            // Symulate typing
            await Worker.botClient.SendChatActionAsync(message.Chat.Id, Telegram.Bot.Types.Enums.ChatAction.Typing);

            // Get info about the file to download
            Telegram.Bot.Types.File TelegramFile = null;
            Policy
                .Handle<Exception>()
                .WaitAndRetry(20, index => TimeSpan.FromSeconds(1), 
                (exception,timeSpan) => {
                    Worker.Logger.LogError(message: exception.Message, exception: exception);
                }).Execute(() => {
                    TelegramFile = Worker.botClient.GetFileAsync(message.Document.FileId).Result; 
                });
            if(TelegramFile == null)
            {
                // Feedback
                await Worker.botClient.SendTextMessageAsync(message.Chat, Strings.cannot_download_file_retry);
                return;
            }

            // Init JWLMerge
            IBackupFileService backupFileService = new BackupFileService();

            // Download file in the temp path
            using (FileStream fs = new FileStream(FileHandling.GetFilePath(FileType.Temp, message.Chat.Id), FileMode.OpenOrCreate, FileAccess.Write))
            {
                await Worker.botClient.DownloadFileAsync(TelegramFile.FilePath, fs);
            }

            // Load the file (in this way you can check if is a valid jwlibrary file)
            BackupFile TempJWLibraryFile = null;
            try
            {
                TempJWLibraryFile = backupFileService.Load(FileHandling.GetFilePath(FileType.Temp, message.Chat.Id));
            }
            catch (Exception exception)
            {
                // Delete wrong temp file
                FileHandling.DeleteFile(FileType.Temp, message.Chat.Id);

                // Feedback for WrongDatabaseVersionException
                if (exception is WrongDatabaseVersionException)
                {
                    int Expected = ((WrongDatabaseVersionException)exception).ExpectedVersion;
                    int Found = ((WrongDatabaseVersionException)exception).FoundVersion;
                    if (Found<Expected)
                        await Worker.botClient.SendTextMessageAsync(message.Chat, string.Format(Strings.wrong_database_version_lower, Found, Expected));
                    else
                        await Worker.botClient.SendTextMessageAsync(message.Chat, string.Format(Strings.wrong_database_version_higher, Found, Expected));
                }
                else
                    // Generic feedback
                    await Worker.botClient.SendTextMessageAsync(message.Chat, string.Format(Strings.file_error, exception.Message));
                return;
            }

            // Se c'era già un file in memoria, fai il merge
            if (FileHandling.FileExists(FileType.Main, message.Chat.Id))
            {
                // Feedback
                await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.received_file2);

                // Symulate typing
                await Worker.botClient.SendChatActionAsync(message.Chat.Id, Telegram.Bot.Types.Enums.ChatAction.Typing);

                // Use JWLMerge to merge files
                BackupFile MainJWLibraryFile = null;
                try
                {
                    // Open also the main file
                    MainJWLibraryFile = backupFileService.Load(FileHandling.GetFilePath(FileType.Main, message.Chat.Id));
                }
                catch (Exception exception)
                {
                    // If the stored files failed to load, it (probably) means that the supported schema version has changed.
                    Worker.Logger.LogError(message: exception.Message, exception: exception);
                    
                    // Send back the old backup
                    using (FileStream fs = System.IO.File.OpenRead(FileHandling.GetFilePath(FileType.Main, message.Chat.Id)))
                    {
                        InputOnlineFile inputOnlineFile = new InputOnlineFile(fs, Strings.old_filename);
                        await Worker.botClient.SendDocumentAsync(
                                chatId: message.Chat.Id,
                                document: inputOnlineFile,
                                caption: Strings.old_schema_error.Replace("\\n", "\n")
                               );
                    }

                    // Replace the old file with this one
                    FileHandling.ChangeFileType(FileType.Temp, FileType.Main, message.Chat.Id);
                    return;
                }

                try
                {
                    // Merge files
                    BackupFile backup = backupFileService.Merge(new List<BackupFile>() { MainJWLibraryFile, TempJWLibraryFile });
                    backupFileService.WriteNewDatabase(backup, FileHandling.GetFilePath(FileType.Merged, message.Chat.Id), FileHandling.GetFilePath(FileType.Main, message.Chat.Id));

                    // Now the merged file become the main stored file
                    FileHandling.ChangeFileType(FileType.Merged, FileType.Main, message.Chat.Id);

                    // Get the user settings
                    ChatConfig chatConfig = ChatConfig.Load(message.Chat.Id);

                    // Send merged file
                    using (FileStream fs = System.IO.File.OpenRead(FileHandling.GetFilePath(FileType.Main, message.Chat.Id)))
                    {
                        InputOnlineFile inputOnlineFile = new InputOnlineFile(fs, string.Format(Strings.merged_filename, DateTime.Now.ToString("s")));
                        await Worker.botClient.SendDocumentAsync(
                                chatId: message.Chat.Id,
                                document: inputOnlineFile,
                                caption: (chatConfig.AutoDeleteFile ? Strings.merged_file : Strings.merged_file_keep) + "\n\n" + GetFileInfoString(backup, message.Chat.Id),
                                replyMarkup: chatConfig.AutoDeleteFile ? null : new InlineKeyboardMarkup(new[] {
                                         InlineKeyboardButton.WithCallbackData(Strings.delete_file, Command.Delete)
                                })
                               );
                    }

                    // Check if you have to delete file after send
                    if (chatConfig.AutoDeleteFile)
                        OnCommand(message, Command.Delete, false);

                    // Increase merged files count
                    chatConfig.MergedFileCount++;
                    chatConfig.Save(message.Chat.Id);

                }
                catch (Exception exception)
                {
                    // Delete temp file, since it won't be processed anymore
                    Worker.Logger.LogError(message:exception.Message, exception: exception);
                    await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.processing_error, exception.Message));
                }
                finally
                {
                    // At the end, delete temp file
                    FileHandling.DeleteFile(FileType.Temp, message.Chat.Id);
                }
            }
            else
            {
                try
                {
                    // Now the temp file become the main file
                    FileHandling.ChangeFileType(FileType.Temp, FileType.Main, message.Chat.Id);

                    // Feedback
                    await Worker.botClient.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: Strings.received_file1 + "\n\n" + GetFileInfoString(TempJWLibraryFile, message.Chat.Id),
                    replyMarkup: new InlineKeyboardMarkup(new[] {
                                     InlineKeyboardButton.WithCallbackData(Strings.delete_file,Command.Delete)
                            }));
                }
                catch (Exception exception)
                {
                    // Log errors while moving temp file
                    Worker.Logger.LogError(message: exception.Message, exception: exception);
                    await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.processing_error, exception.Message));
                }
            }
        }

        public static async void OnOtherContent(Message message)
        {
            // Set user language
            ChatConfig.Load(message.Chat.Id).ApplyLanguage();

            // If another type of content
            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.wrong_filetype);
        }

        public static async void OnCommand(Message message, string command, bool fromCallback)
        {
            // Set user language
            ChatConfig.Load(message.Chat.Id).ApplyLanguage();

            switch (command)
            {
                case Command.Start:
                    // Send welcome message
                    await Worker.botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: string.Format(Strings.start_details, message.Chat.FirstName).Replace("\\n", "\n")
                        );
                    break;

                case Command.Delete:
                    {
                        // If you deleted the file, remove the buttons 
                        if (fromCallback)
                        {
                            // Find out which buttons to keep
                            List<InlineKeyboardButton> buttons = new List<InlineKeyboardButton>();
                            foreach (var keyboard in message.ReplyMarkup.InlineKeyboard)
                                foreach (InlineKeyboardButton button in keyboard)
                                    if (!button.CallbackData.Equals(Command.Delete))
                                        buttons.Add(button);

                            // Refresh buttons
                            await Worker.botClient.EditMessageReplyMarkupAsync(
                                message.Chat.Id,
                                message.MessageId,
                                new InlineKeyboardMarkup(buttons));
                        }

                        if (FileHandling.FileExists(FileType.Main, message.Chat.Id))
                        {
                            // Delete stored files
                            FileHandling.DeleteFile(FileType.Main, message.Chat.Id);
                            // Feedback
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.file_deleted);
                        }
                        else
                        {
                            // Feedback
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.nothing_to_delete);
                        }
                    }
                    break;

                case Command.FileInfo:
                    // Check if file exists
                    if (!FileHandling.FileExists(FileType.Main, message.Chat.Id))
                    {
                        await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.file_not_exists);
                        return;
                    }

                    // Init JWLMerge
                    IBackupFileService backupFileService = new BackupFileService();

                    BackupFile MainJWLibraryFile = null;
                    try
                    {
                        MainJWLibraryFile = backupFileService.Load(FileHandling.GetFilePath(FileType.Main, message.Chat.Id));
                    }
                    catch (Exception exception)
                    {
                        // Feedback
                        await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.file_error, exception.Message));
                        return;
                    }

                    // Get stored file infos
                    await Worker.botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: GetFileInfoString(MainJWLibraryFile, message.Chat.Id),
                            replyMarkup: new InlineKeyboardMarkup(new[] {
                                 InlineKeyboardButton.WithCallbackData(Strings.delete_file,Command.Delete)
                            }));
                    break;

                case Command.BotInfo:
                    // Feedback
                    await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.bot_info_details, Assembly.GetEntryAssembly().GetName().Version.ToString(), Assembly.GetAssembly(typeof(BackupFileService)).GetName().Version.ToString()).Replace("\\n", "\n"));
                    break;

                case Command.Stat:
                case Command.Stats:
                    // Get some statistics, if admin
                    if (AppConfig.Load().IsAdmin(message.Chat.Username))
                    {

                        // Symulate typing
                        await Worker.botClient.SendChatActionAsync(message.Chat.Id, Telegram.Bot.Types.Enums.ChatAction.Typing);

                        // List stored files
                        string[] StoredFiles = FileHandling.GetMainFiles();
                        StringBuilder sb = new StringBuilder();
                        foreach (string file in StoredFiles)
                        {
                            try
                            {
                                var ChatInfo = Worker.botClient.GetChatAsync(Path.GetFileNameWithoutExtension(file)).Result;
                                sb.AppendLine($"{ChatInfo.FirstName} {ChatInfo.LastName}".Trim() + (ChatInfo.Username != null ? $" @{ChatInfo.Username}" : ""));
                            }
                            catch (Exception e)
                            {
                                e.ToString();
                            }
                        }
                        await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.stat, StoredFiles.Length, sb.ToString()).Replace("\\n", "\n"));
                    }
                    break;

                case Command.SendMessage:
                    if (AppConfig.Load().IsAdmin(message.Chat.Username))
                        await Worker.botClient.SendTextMessageAsync(message.Chat, Strings.message_syntax.Replace("\\n", "\n"));
                    break;

                case Command.Changelog:
                    // TODO Post changelog
                    if (AppConfig.Load().IsAdmin(message.Chat.Username))
                        await Worker.botClient.SendTextMessageAsync(message.Chat, "Not implemented yet...");
                    break;

                case Command.Settings:
                    // Change settings
                    InlineKeyboardMarkup settingsKeyboardMarkup = new InlineKeyboardMarkup(new[] {
                             new[] { InlineKeyboardButton.WithCallbackData(Strings.change_language_detail, Command.SetLang)},
                             ChatConfig.Load(message.Chat.Id).AutoDeleteFile?
                             new[] { InlineKeyboardButton.WithCallbackData(string.Format(Strings.auto_delete, Strings.yes),Command.AutodeleteOff)}:
                             new[] { InlineKeyboardButton.WithCallbackData(string.Format(Strings.auto_delete, Strings.no),Command.AutodeleteOn) }
                        });

                    if (fromCallback)
                    {
                        await Worker.botClient.EditMessageTextAsync(
                            chatId: message.Chat.Id,
                            messageId: message.MessageId,
                            text: Strings.change_settings
                        );
                        await Worker.botClient.EditMessageReplyMarkupAsync(
                                chatId: message.Chat.Id,
                                messageId: message.MessageId,
                                replyMarkup: settingsKeyboardMarkup
                                );
                    }
                    else
                    {
                        await Worker.botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: Strings.change_settings,
                            replyMarkup: settingsKeyboardMarkup
                            );
                    }

                    break;


                // Change language 
                case Command.SetLang:
                    await Worker.botClient.EditMessageTextAsync(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        text: Strings.change_language
                    );
                    await Worker.botClient.EditMessageReplyMarkupAsync(
                            chatId: message.Chat.Id,
                            messageId: message.MessageId,
                            replyMarkup: new InlineKeyboardMarkup(
                                new[] {
                                new[] { InlineKeyboardButton.WithCallbackData(Strings.lang_it, Command.SetLangIt) },
                                new[] { InlineKeyboardButton.WithCallbackData(Strings.lang_en, Command.SetLangEn) },
                                new[] { InlineKeyboardButton.WithCallbackData(Strings.back, Command.Settings) }
                                    }
                            )
                            );
                    break;

                // Change language to italian
                case Command.SetLangIt:
                    {
                        ChatConfig chatConfig = ChatConfig.Load(message.Chat.Id);
                        chatConfig.Language = "it";
                        chatConfig.Save(message.Chat.Id);
                        chatConfig.ApplyLanguage();
                    }
                    goto case Command.Settings;

                // Change language to english
                case Command.SetLangEn:
                    {
                        ChatConfig chatConfig = ChatConfig.Load(message.Chat.Id);
                        chatConfig.Language = "en";
                        chatConfig.Save(message.Chat.Id);
                        chatConfig.ApplyLanguage();
                    }
                    goto case Command.Settings;

                // Change autodelete setting
                case Command.AutodeleteOn:
                case Command.AutodeleteOff:
                    {
                        ChatConfig chatConfig = ChatConfig.Load(message.Chat.Id);
                        chatConfig.AutoDeleteFile = command.Equals(Command.AutodeleteOn);
                        chatConfig.Save(message.Chat.Id);
                    }
                    goto case Command.Settings;
            }

            // Regex commands
            // Regexs
            List<String> regexs = new List<string> { Command.SendMessageRegex };
            foreach (string regex in regexs)
            {
                Match regexMatch = Regex.Match(command, regex);
                if (regexMatch.Success)
                {
                    switch (regex)
                    {
                        case Command.SendMessageRegex:
                            // Send message, if admin
                            if (AppConfig.Load().IsAdmin(message.Chat.Username))
                            {
                                // Try to parse the message
                                BotMessage botMessage = JsonConvert.DeserializeObject<BotMessage>(regexMatch.Groups[1].Value);

                                // Check if the message is valid
                                if (botMessage != null && botMessage.IsValid())
                                {
                                    List<int> ChatIds = new List<int>();
                                    // Single or multiple recipient?
                                    if (botMessage.IsSingleRecipient())
                                    {
                                        // Single recipient
                                        ChatIds.Add(int.Parse(botMessage.Recipients));
                                    }
                                    else
                                    {
                                        // Multiple recipients
                                        string[] files = new string[0];
                                        if (botMessage.Recipients.Equals(BotMessage.RecipientsWithStoredFile))
                                            files = FileHandling.GetMainFiles();
                                        else if(botMessage.Recipients.Equals(BotMessage.RecipientsWithSettingsInitialized))
                                            files = FileHandling.GetConfigFiles();
                                        foreach (string file in files)
                                            if (int.TryParse(Path.GetFileNameWithoutExtension(file), out int number))
                                                ChatIds.Add(number);
                                    }

                                    // Send Text to every chat
                                    List<string> messagesExceptions = new List<string>();
                                    foreach(int ChatId in ChatIds)
                                    {
                                        // Get chatIdSettings
                                        ChatConfig chatConfig = ChatConfig.Load(ChatId);

                                        // Send the message
                                        try
                                        {
                                            await Worker.botClient.SendTextMessageAsync(ChatId, botMessage.Text[chatConfig.Language]);
                                        }
                                        catch (Exception exception)
                                        {
                                            // Feedback
                                            messagesExceptions.Add(string.Format("({0}) {1}", ChatId, exception.Message));
                                        }
                                    }

                                    // Feedback
                                    int exceptionCount = messagesExceptions.Count;
                                    int sentCount = ChatIds.Count - exceptionCount;
                                    string messageText = string.Format(Strings.messages_sent, sentCount);
                                    if (exceptionCount > 0)
                                    {
                                        messageText += "\n" + string.Format(Strings.messages_not_sent, exceptionCount);
                                        foreach (string exc in messagesExceptions)
                                            messageText += "\n" + exc;
                                    }
                                    Worker.Logger.LogError(message: messageText);
                                    await Worker.botClient.SendTextMessageAsync(message.Chat.Id, messageText);
                                }
                                else
                                    await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.invalid_message_syntax);
                            }
                            break;
                    }
                }
            }
        }

        private static String GetFileInfoString(BackupFile JWLibraryFile, long chatId)
        {
            return string.Format(Strings.file_info_details, FileHandling.GetReadableFilesize(FileType.Main, chatId), JWLibraryFile.Database.Notes.Count, JWLibraryFile.Database.Bookmarks.Count, JWLibraryFile.Database.UserMarks.Count, JWLibraryFile.Database.Tags.Count).Replace("\\n", "\n");
        }
    }
}
