using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types;
using Telegram.Bot;
using JWLMerge.BackupFileServices;
using JWLMerge.BackupFileServices.Models;
using JWLMerge.BackupFileServices.Helpers;
using Telegram.Bot.Types.ReplyMarkups;
using JWLMergeBot.Properties;
using static JWLMergeBot.FileHandling;
using Microsoft.Extensions.Logging;
using Polly;
using JWLMerge.BackupFileServices.Exceptions;
using System.Text.RegularExpressions;
using System.Globalization;
using Newtonsoft.Json;
using JWLMergeBot.Models;
using DocumentFormat.OpenXml.Drawing.Charts;
using JWLMergeBot.Helpers;

namespace JWLMergeBot
{
    partial class Logic
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

                // Preview before merging (if enabled): show what would change and wait for the user's decision
                if (ChatConfig.Load(message.Chat.Id).PreviewBeforeMerge)
                {
                    MergePreviewResult preview = null;
                    try
                    {
                        preview = new MergePreview().Compute(MainJWLibraryFile, TempJWLibraryFile);
                    }
                    catch (Exception exception)
                    {
                        // A failing preview must never block the merge itself: fall back to the classic behaviour
                        Worker.Logger.LogError(message: exception.Message, exception: exception);
                    }

                    if (preview != null)
                    {
                        try
                        {
                            if (!preview.HasChanges)
                            {
                                FileHandling.DeleteFile(FileType.Temp, message.Chat.Id);
                                await Worker.botClient.SendTextMessageAsync(message.Chat.Id, HealthText("merge_nothing_new", message.Chat.Id));
                                return;
                            }

                            // Park the new file until the user decides. A newer upload replaces an older waiting one
                            FileHandling.ClearPending(message.Chat.Id);
                            FileHandling.ChangeFileType(FileType.Temp, FileType.Pending, message.Chat.Id);
                            await ShowMergePreview(message, false, preview, new Dictionary<string, bool>());
                        }
                        catch (Exception exception)
                        {
                            Worker.Logger.LogError(message: exception.Message, exception: exception);
                            FileHandling.DeleteFile(FileType.Temp, message.Chat.Id);
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.processing_error, exception.Message));
                        }
                        return;
                    }
                }

                await MergeAndSend(message, MainJWLibraryFile, TempJWLibraryFile, FileType.Temp, null, null);
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
                    {
                        // Check if file exists
                        if (!FileHandling.FileExists(FileType.Main, message.Chat.Id))
                        {
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.file_not_exists);
                            return;
                        }

                        // Load stored file
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
                    }
                    break;

                case Command.BotInfo:
                    // Feedback
                    await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.bot_info_details, Assembly.GetEntryAssembly().GetName().Version.ToString()).Replace("\\n", "\n"));
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

                case Command.EditFile:
                    // Check if any file is stored
                    if (!FileHandling.FileExists(FileType.Main, message.Chat.Id))
                    {
                        await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.file_not_exists);
                        return;
                    }

                    // Set edit file buttons
                    InlineKeyboardMarkup editFileKeyboardMarkup = new InlineKeyboardMarkup(new[] {
                             new[] { InlineKeyboardButton.WithCallbackData(Strings.delete_favorites, Command.DeleteFavorites) },
                             new[] { InlineKeyboardButton.WithCallbackData(HealthText("health_button", message.Chat.Id), Command.Health) },
                             new[] { InlineKeyboardButton.WithCallbackData(HealthText("stats_button", message.Chat.Id), Command.StudyStats) },
                             new[] { InlineKeyboardButton.WithCallbackData(HealthText("tags_btn", message.Chat.Id), Command.Tags) },
                             new[] { InlineKeyboardButton.WithCallbackData(HealthText("extract_btn", message.Chat.Id), Command.Extract) },
                        });

                    // Insert or update the menu
                    InsertUpdateMenu(Strings.edit_stored_file, message, fromCallback, editFileKeyboardMarkup);

                    break;

                case Command.DeleteFavorites:
                    // Prompt the user to confirm
                    InsertUpdateMenu(Strings.delete_favorites_confirm, message, fromCallback, new InlineKeyboardMarkup(
                    new[] {
                                new[] { InlineKeyboardButton.WithCallbackData(Strings.yes, Command.DeleteFavoritesConfirmed) },
                                new[] { InlineKeyboardButton.WithCallbackData(Strings.no, Command.EditFile) }
                        }));
                    break;

                case Command.DeleteFavoritesConfirmed:
                    {
                        // Check if any file is stored
                        if (!FileHandling.FileExists(FileType.Main, message.Chat.Id))
                        {
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, Strings.file_not_exists);
                            return;
                        }

                        // Feedback
                        InsertUpdateMenu(Strings.editing_file, message, fromCallback, null);

                        // Load stored file
                        IBackupFileService backupFileService = new BackupFileService();
                        BackupFile MainJWLibraryFile = null;
                        try
                        {
                            MainJWLibraryFile = backupFileService.Load(FileHandling.GetFilePath(FileType.Main, message.Chat.Id));

                            // Delete favorites
                            MainJWLibraryFile.Database.TagMaps.RemoveAll(tagmap => tagmap.TagId == 1);

                            // Update last modified date
                            MainJWLibraryFile.Manifest.UserDataBackup.LastModifiedDate = DateTime.Now.ToString(ManifestDateTimeFormat);
                            MainJWLibraryFile.Manifest.CreationDate = DateTime.Now.ToString(ManifestDateTimeFormat);

                            // Write the merged database
                            backupFileService.WriteNewDatabase(MainJWLibraryFile, FileHandling.GetFilePath(FileType.Temp, message.Chat.Id), FileHandling.GetFilePath(FileType.Main, message.Chat.Id), new List<string> { MainJWLibraryFile.FilePath });

                            // Now the modified file become the main stored file
                            FileHandling.ChangeFileType(FileType.Temp, FileType.Main, message.Chat.Id);

                            // Get the user settings
                            ChatConfig chatConfig = ChatConfig.Load(message.Chat.Id);

                            // Send edited file
                            using (FileStream fs = System.IO.File.OpenRead(FileHandling.GetFilePath(FileType.Main, message.Chat.Id)))
                            {
                                InputOnlineFile inputOnlineFile = new InputOnlineFile(fs, string.Format(Strings.edited_filename, DateTime.Now.ToString("s")));
                                await Worker.botClient.SendDocumentAsync(
                                        chatId: message.Chat.Id,
                                        document: inputOnlineFile,
                                        caption: Strings.edited_file + "\n\n" + GetFileInfoString(MainJWLibraryFile, message.Chat.Id),
                                        replyMarkup: chatConfig.AutoDeleteFile ? null : new InlineKeyboardMarkup(new[] {
                                        InlineKeyboardButton.WithCallbackData(Strings.delete_file, Command.Delete)
                                        })
                                        );
                            }
                        }
                        catch (Exception exception)
                        {
                            // Feedback
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.file_error, exception.Message));
                            return;
                        }
                    }
                    break;

                case Command.StudyStats:
                    {
                        // Check if any file is stored
                        if (!FileHandling.FileExists(FileType.Main, message.Chat.Id))
                        {
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, HealthText("health_no_file", message.Chat.Id).Replace("/health", Command.StudyStats));
                            return;
                        }

                        await Worker.botClient.SendChatActionAsync(message.Chat.Id, Telegram.Bot.Types.Enums.ChatAction.Typing);

                        try
                        {
                            BackupFile statsFile = new BackupFileService().Load(FileHandling.GetFilePath(FileType.Main, message.Chat.Id));
                            StudyStatsResult stats = new StudyStats().Compute(statsFile, DateTime.UtcNow);
                            await Worker.botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: BuildStudyStatsText(stats, message.Chat.Id), parseMode: Telegram.Bot.Types.Enums.ParseMode.Html);
                        }
                        catch (Exception exception)
                        {
                            Worker.Logger.LogError(message: exception.Message, exception: exception);
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.file_error, exception.Message));
                        }
                    }
                    break;

                case Command.Health:
                    {
                        // Check if any file is stored
                        if (!FileHandling.FileExists(FileType.Main, message.Chat.Id))
                        {
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, HealthText("health_no_file", message.Chat.Id));
                            return;
                        }

                        // Symulate typing
                        await Worker.botClient.SendChatActionAsync(message.Chat.Id, Telegram.Bot.Types.Enums.ChatAction.Typing);

                        // Load stored file and scan it (read only: nothing is changed here)
                        HealthReport healthReport;
                        try
                        {
                            BackupFile healthFile = new BackupFileService().Load(FileHandling.GetFilePath(FileType.Main, message.Chat.Id));
                            healthReport = new LibraryDoctor().Scan(healthFile);
                        }
                        catch (Exception exception)
                        {
                            Worker.Logger.LogError(message: exception.Message, exception: exception);
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.file_error, exception.Message));
                            return;
                        }

                        await Worker.botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: BuildHealthReportText(healthReport, message.Chat.Id),
                            replyMarkup: healthReport.HasFixable ? new InlineKeyboardMarkup(new[] {
                                new[] { InlineKeyboardButton.WithCallbackData(HealthText("health_fix_button", message.Chat.Id), Command.HealthFix) }
                            }) : null);
                    }
                    break;

                case Command.HealthFix:
                    {
                        // Check if any file is stored
                        if (!FileHandling.FileExists(FileType.Main, message.Chat.Id))
                        {
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, HealthText("health_no_file", message.Chat.Id));
                            return;
                        }

                        // Remove the button, so it can't be pressed twice
                        if (fromCallback)
                        {
                            await Worker.botClient.EditMessageReplyMarkupAsync(
                                message.Chat.Id,
                                message.MessageId,
                                new InlineKeyboardMarkup(new List<InlineKeyboardButton>()));
                        }

                        // Check if I can handle the file (if a temporary file exist, it means I'm working on it...)
                        if (FileHandling.IsTempFileBusy(message.Chat.Id))
                        {
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.busy, Command.HealthFix).Replace("\\n", "\n"));
                            return;
                        }

                        await Worker.botClient.SendTextMessageAsync(message.Chat.Id, HealthText("health_fixing", message.Chat.Id));
                        await Worker.botClient.SendChatActionAsync(message.Chat.Id, Telegram.Bot.Types.Enums.ChatAction.Typing);

                        try
                        {
                            IBackupFileService backupFileService = new BackupFileService();
                            BackupFile MainJWLibraryFile = backupFileService.Load(FileHandling.GetFilePath(FileType.Main, message.Chat.Id));

                            // Repair in memory. If anything is still inconsistent this throws and the stored file stays untouched
                            int repaired = new LibraryDoctor().Repair(MainJWLibraryFile);
                            if (repaired == 0)
                            {
                                await Worker.botClient.SendTextMessageAsync(message.Chat.Id, HealthText("health_nothing_to_fix", message.Chat.Id));
                                return;
                            }

                            // Update last modified date
                            MainJWLibraryFile.Manifest.UserDataBackup.LastModifiedDate = DateTime.Now.ToString(ManifestDateTimeFormat);
                            MainJWLibraryFile.Manifest.CreationDate = DateTime.Now.ToString(ManifestDateTimeFormat);

                            // Write the repaired database
                            backupFileService.WriteNewDatabase(MainJWLibraryFile, FileHandling.GetFilePath(FileType.Temp, message.Chat.Id), FileHandling.GetFilePath(FileType.Main, message.Chat.Id), new List<string> { MainJWLibraryFile.FilePath });

                            // Now the repaired file become the main stored file
                            FileHandling.ChangeFileType(FileType.Temp, FileType.Main, message.Chat.Id);

                            // Get the user settings
                            ChatConfig chatConfig = ChatConfig.Load(message.Chat.Id);

                            // Send repaired file
                            using (FileStream fs = System.IO.File.OpenRead(FileHandling.GetFilePath(FileType.Main, message.Chat.Id)))
                            {
                                InputOnlineFile inputOnlineFile = new InputOnlineFile(fs, string.Format(Strings.edited_filename, DateTime.Now.ToString("s")));
                                await Worker.botClient.SendDocumentAsync(
                                        chatId: message.Chat.Id,
                                        document: inputOnlineFile,
                                        caption: string.Format(HealthText("health_fixed", message.Chat.Id), repaired) + "\n\n" + GetFileInfoString(MainJWLibraryFile, message.Chat.Id),
                                        replyMarkup: chatConfig.AutoDeleteFile ? null : new InlineKeyboardMarkup(new[] {
                                            InlineKeyboardButton.WithCallbackData(Strings.delete_file, Command.Delete)
                                        })
                                        );
                            }

                            // Check if you have to delete file after send
                            if (chatConfig.AutoDeleteFile)
                                OnCommand(message, Command.Delete, false);
                        }
                        catch (Exception exception)
                        {
                            Worker.Logger.LogError(message: exception.Message, exception: exception);
                            await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(HealthText("health_fix_failed", message.Chat.Id), exception.Message));
                        }
                        finally
                        {
                            // Never leave a stale temp file behind
                            FileHandling.DeleteFile(FileType.Temp, message.Chat.Id);
                        }
                    }
                    break;

                case Command.Settings:
                    // Set settings buttons
                    InlineKeyboardMarkup settingsKeyboardMarkup = new InlineKeyboardMarkup(new[] {
                             new[] { InlineKeyboardButton.WithCallbackData(Strings.change_language_detail, Command.SetLang)},
                             ChatConfig.Load(message.Chat.Id).PreviewBeforeMerge?
                             new[] { InlineKeyboardButton.WithCallbackData(string.Format(HealthText("preview_setting", message.Chat.Id), Strings.yes),Command.PreviewOff)}:
                             new[] { InlineKeyboardButton.WithCallbackData(string.Format(HealthText("preview_setting", message.Chat.Id), Strings.no),Command.PreviewOn) },
                             ChatConfig.Load(message.Chat.Id).AutoDeleteFile?
                             new[] { InlineKeyboardButton.WithCallbackData(string.Format(Strings.auto_delete, Strings.yes),Command.AutodeleteOff)}:
                             new[] { InlineKeyboardButton.WithCallbackData(string.Format(Strings.auto_delete, Strings.no),Command.AutodeleteOn) }
                        });

                    // Insert or update the menu
                    InsertUpdateMenu(Strings.change_settings, message, fromCallback, settingsKeyboardMarkup);

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
                                new[] { InlineKeyboardButton.WithCallbackData(Strings.lang_de, Command.SetLangDe) },
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

                // Change language to german
                case Command.SetLangDe:
                    {
                        ChatConfig chatConfig = ChatConfig.Load(message.Chat.Id);
                        chatConfig.Language = "de";
                        chatConfig.Save(message.Chat.Id);
                        chatConfig.ApplyLanguage();
                    }
                    goto case Command.Settings;

                // Change merge preview setting
                case Command.PreviewOn:
                case Command.PreviewOff:
                    {
                        ChatConfig chatConfig = ChatConfig.Load(message.Chat.Id);
                        chatConfig.PreviewBeforeMerge = command.Equals(Command.PreviewOn);
                        chatConfig.Save(message.Chat.Id);
                    }
                    goto case Command.Settings;

                // Merge preview decisions
                case Command.MergeGo:
                case Command.MergeCancel:
                case "/mo":
                    await OnMergeCommand(message, command);
                    break;

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

            // Conflict review buttons (/mr_N show, /mk_N keep stored, /mn_N use new)
            if (Regex.IsMatch(command, @"^/m[rkn]_\d+$"))
            {
                await OnMergeCommand(message, command);
                return;
            }

            // Search, tag manager and extract
            if (await OnToolsCommand(message, command, fromCallback))
                return;

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
                                            // Use the chat language, or English when the message has no text in that language
                                            string broadcastText = botMessage.Text.ContainsKey(chatConfig.Language) ? botMessage.Text[chatConfig.Language] : botMessage.Text["en"];
                                            await Worker.botClient.SendTextMessageAsync(ChatId, broadcastText);
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

        private static String ManifestDateTimeFormat = "yyyy-MM-ddTHH:mm:sszzz";

        // ---------------------------------------------------------------- Merge (shared by the direct and the previewed path)

        private static readonly HashSet<long> MergesInProgress = new HashSet<long>();

        /// <summary>Merges, stores and sends the result. Used by the direct path and after the user confirmed a preview.</summary>
        private static async Task MergeAndSend(Message message, BackupFile MainJWLibraryFile, BackupFile TempJWLibraryFile, FileType incomingType, MergePreviewResult preview, IReadOnlyDictionary<string, bool> choices)
        {
            IBackupFileService backupFileService = new BackupFileService();
            try
            {
                // Merge files
                BackupFile backup = backupFileService.Merge(new List<BackupFile>() { MainJWLibraryFile, TempJWLibraryFile });

                // Apply the versions the user picked for notes edited on both devices
                if (preview != null && choices != null && choices.Count > 0)
                    new MergePreview().ApplyChoices(backup, preview.Conflicts, choices);

                // Set the greatest Modification date
                if (
                    DateTime.TryParseExact(MainJWLibraryFile.Manifest.UserDataBackup.LastModifiedDate, ManifestDateTimeFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime MainJWLibraryFileLastModifiedDate) &&
                    DateTime.TryParseExact(TempJWLibraryFile.Manifest.UserDataBackup.LastModifiedDate, ManifestDateTimeFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime TempJWLibraryFileLastModifiedDate)
                    )
                {
                    if (MainJWLibraryFileLastModifiedDate > TempJWLibraryFileLastModifiedDate)
                        backup.Manifest.UserDataBackup.LastModifiedDate = MainJWLibraryFile.Manifest.UserDataBackup.LastModifiedDate;
                    else
                        backup.Manifest.UserDataBackup.LastModifiedDate = TempJWLibraryFile.Manifest.UserDataBackup.LastModifiedDate;
                }
                MainJWLibraryFile.Manifest.CreationDate = DateTime.Now.ToString(ManifestDateTimeFormat);

                // Write the merged database
                backupFileService.WriteNewDatabase(backup, FileHandling.GetFilePath(FileType.Merged, message.Chat.Id), FileHandling.GetFilePath(FileType.Main, message.Chat.Id), new List<string> { MainJWLibraryFile.FilePath, TempJWLibraryFile.FilePath });

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
                Worker.Logger.LogError(message: exception.Message, exception: exception);
                await Worker.botClient.SendTextMessageAsync(message.Chat.Id, string.Format(Strings.processing_error, exception.Message));
            }
            finally
            {
                // At the end, delete the incoming file (and any waiting-decision state)
                if (incomingType == FileType.Pending)
                    FileHandling.ClearPending(message.Chat.Id);
                else
                    FileHandling.DeleteFile(incomingType, message.Chat.Id);
            }
        }

        // ---------------------------------------------------------------- Merge preview and conflict review

        private class PendingMerge
        {
            public BackupFile Main;
            public BackupFile Incoming;
            public MergePreviewResult Preview;
            public Dictionary<string, bool> Decisions;
        }

        private static Dictionary<string, bool> LoadDecisions(long chatId)
        {
            try
            {
                string path = FileHandling.GetFilePath(FileType.PendingState, chatId);
                if (System.IO.File.Exists(path))
                    return JsonConvert.DeserializeObject<Dictionary<string, bool>>(System.IO.File.ReadAllText(path)) ?? new Dictionary<string, bool>();
            }
            catch (Exception)
            {
            }
            return new Dictionary<string, bool>();
        }

        private static void SaveDecisions(long chatId, Dictionary<string, bool> decisions)
        {
            System.IO.File.WriteAllText(FileHandling.GetFilePath(FileType.PendingState, chatId), JsonConvert.SerializeObject(decisions));
        }

        /// <summary>Reloads everything from disk, so a bot restart between preview and decision is harmless.</summary>
        private static PendingMerge LoadPendingMerge(long chatId)
        {
            if (!FileHandling.IsPendingValid(chatId) || !FileHandling.FileExists(FileType.Main, chatId))
                return null;

            IBackupFileService service = new BackupFileService();
            PendingMerge pending = new PendingMerge();
            pending.Main = service.Load(FileHandling.GetFilePath(FileType.Main, chatId));
            pending.Incoming = service.Load(FileHandling.GetFilePath(FileType.Pending, chatId));
            pending.Preview = new MergePreview().Compute(pending.Main, pending.Incoming);
            pending.Decisions = LoadDecisions(chatId);
            return pending;
        }

        private static string Trunc(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max).TrimEnd() + "…";
        }

        private static async Task ShowMergePreview(Message message, bool edit, MergePreviewResult preview, Dictionary<string, bool> decisions)
        {
            long chatId = message.Chat.Id;
            string T(string key) => HealthText(key, chatId);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(T("merge_title"));
            sb.AppendLine();
            sb.AppendLine(T("merge_intro"));
            if (preview.NewNotes > 0) sb.AppendLine(string.Format(T("merge_line_notes"), preview.NewNotes));
            if (preview.NewHighlights > 0) sb.AppendLine(string.Format(T("merge_line_highlights"), preview.NewHighlights));
            if (preview.NewBookmarks > 0) sb.AppendLine(string.Format(T("merge_line_bookmarks"), preview.NewBookmarks));
            if (preview.NewTags > 0) sb.AppendLine(string.Format(T("merge_line_tags"), preview.NewTags));
            if (preview.NewPlaylistItems > 0) sb.AppendLine(string.Format(T("merge_line_playlist"), preview.NewPlaylistItems));
            if (preview.UnchangedNotes > 0) sb.AppendLine(string.Format(T("merge_line_unchanged"), preview.UnchangedNotes));

            List<InlineKeyboardButton[]> rows = new List<InlineKeyboardButton[]>();
            if (preview.Conflicts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format(T("merge_line_conflicts"), preview.Conflicts.Count));
                int decided = preview.Conflicts.Count(c => decisions.ContainsKey(c.Guid));
                if (decided > 0)
                    sb.AppendLine(string.Format(T("merge_line_decided"), decided, preview.Conflicts.Count));
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(string.Format(T("merge_btn_review"), preview.Conflicts.Count), "/mr_0") });
            }
            sb.AppendLine();
            sb.Append(T("merge_footer"));

            rows.Add(new[] {
                InlineKeyboardButton.WithCallbackData(T("merge_btn_go"), Command.MergeGo),
                InlineKeyboardButton.WithCallbackData(T("merge_btn_cancel"), Command.MergeCancel)
            });

            InlineKeyboardMarkup markup = new InlineKeyboardMarkup(rows);
            if (edit)
                await Worker.botClient.EditMessageTextAsync(chatId: chatId, messageId: message.MessageId, text: sb.ToString(), replyMarkup: markup);
            else
                await Worker.botClient.SendTextMessageAsync(chatId: chatId, text: sb.ToString(), replyMarkup: markup);
        }

        private static async Task ShowMergeConflict(Message message, MergePreviewResult preview, Dictionary<string, bool> decisions, int index)
        {
            long chatId = message.Chat.Id;
            string T(string key) => HealthText(key, chatId);
            CultureInfo culture = CultureInfo.GetCultureInfo(ChatConfig.Load(chatId).Language);

            index = Math.Max(0, Math.Min(index, preview.Conflicts.Count - 1));
            NoteConflict c = preview.Conflicts[index];
            string Date(NoteVersion v) => v.Modified == DateTime.MinValue ? "?" : v.Modified.ToString("g", culture);
            string Body(NoteVersion v)
            {
                string title = Trunc(v.Title, 120);
                string content = Trunc(v.Content, 450);
                if (title == null && content == null) return T("merge_empty");
                return (title != null ? title + "\n" : "") + (content ?? "");
            }

            bool? choice = decisions.TryGetValue(c.Guid, out bool takeNew) ? takeNew : (bool?)null;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format(T("merge_conflict_title"), index + 1, preview.Conflicts.Count, c.Where));
            sb.AppendLine();
            sb.AppendLine(string.Format(T("merge_stored_label"), Date(c.Stored)));
            sb.AppendLine(Body(c.Stored));
            sb.AppendLine();
            sb.AppendLine(string.Format(T("merge_new_label"), Date(c.Incoming)));
            sb.AppendLine(Body(c.Incoming));
            sb.AppendLine();
            if (choice == null)
                sb.Append(T(c.IncomingWinsByDefault ? "merge_default_new" : "merge_default_stored"));
            else
                sb.Append(T(choice.Value ? "merge_chosen_new" : "merge_chosen_stored"));

            List<InlineKeyboardButton[]> rows = new List<InlineKeyboardButton[]>();
            rows.Add(new[] {
                InlineKeyboardButton.WithCallbackData((choice == false ? "✓ " : "") + T("merge_btn_stored"), "/mk_" + index),
                InlineKeyboardButton.WithCallbackData((choice == true ? "✓ " : "") + T("merge_btn_new"), "/mn_" + index)
            });
            List<InlineKeyboardButton> nav = new List<InlineKeyboardButton>();
            if (index > 0) nav.Add(InlineKeyboardButton.WithCallbackData("◀", "/mr_" + (index - 1)));
            nav.Add(InlineKeyboardButton.WithCallbackData(T("merge_btn_overview"), "/mo"));
            if (index < preview.Conflicts.Count - 1) nav.Add(InlineKeyboardButton.WithCallbackData("▶", "/mr_" + (index + 1)));
            rows.Add(nav.ToArray());
            rows.Add(new[] {
                InlineKeyboardButton.WithCallbackData(T("merge_btn_go_now"), Command.MergeGo),
                InlineKeyboardButton.WithCallbackData(T("merge_btn_cancel"), Command.MergeCancel)
            });

            await Worker.botClient.EditMessageTextAsync(chatId: chatId, messageId: message.MessageId, text: sb.ToString(), replyMarkup: new InlineKeyboardMarkup(rows));
        }

        /// <summary>Handles /merge_go, /merge_cancel, /mo (overview) and /mr_N, /mk_N, /mn_N (conflict review).</summary>
        private static async Task OnMergeCommand(Message message, string command)
        {
            try
            {
                await OnMergeCommandCore(message, command);
            }
            catch (Exception exception)
            {
                // e.g. Telegram refuses to edit a message into the very same text after a double tap: harmless, never crash the bot
                Worker.Logger.LogError(message: exception.Message, exception: exception);
            }
        }

        private static async Task OnMergeCommandCore(Message message, string command)
        {
            long chatId = message.Chat.Id;

            if (command == Command.MergeCancel)
            {
                FileHandling.ClearPending(chatId);
                await Worker.botClient.EditMessageTextAsync(chatId: chatId, messageId: message.MessageId, text: HealthText("merge_cancelled", chatId));
                return;
            }

            PendingMerge pending;
            try
            {
                pending = LoadPendingMerge(chatId);
            }
            catch (Exception exception)
            {
                Worker.Logger.LogError(message: exception.Message, exception: exception);
                FileHandling.ClearPending(chatId);
                await Worker.botClient.EditMessageTextAsync(chatId: chatId, messageId: message.MessageId, text: string.Format(HealthText("merge_pending_error", chatId), exception.Message));
                return;
            }

            if (pending == null)
            {
                await Worker.botClient.EditMessageTextAsync(chatId: chatId, messageId: message.MessageId, text: HealthText("merge_no_pending", chatId));
                return;
            }

            if (command == Command.MergeGo)
            {
                lock (MergesInProgress)
                {
                    if (!MergesInProgress.Add(chatId))
                        return; // double tap
                }

                try
                {
                    await Worker.botClient.EditMessageTextAsync(chatId: chatId, messageId: message.MessageId, text: HealthText("merge_running", chatId));
                    await Worker.botClient.SendChatActionAsync(chatId, Telegram.Bot.Types.Enums.ChatAction.Typing);
                    await MergeAndSend(message, pending.Main, pending.Incoming, FileType.Pending, pending.Preview, pending.Decisions);
                }
                finally
                {
                    lock (MergesInProgress)
                    {
                        MergesInProgress.Remove(chatId);
                    }
                }
                return;
            }

            if (command == "/mo")
            {
                await ShowMergePreview(message, true, pending.Preview, pending.Decisions);
                return;
            }

            Match m = Regex.Match(command, @"^/m([rkn])_(\d+)$");
            if (!m.Success || pending.Preview.Conflicts.Count == 0)
            {
                await ShowMergePreview(message, true, pending.Preview, pending.Decisions);
                return;
            }

            int index = int.Parse(m.Groups[2].Value);
            if (index >= pending.Preview.Conflicts.Count)
                index = pending.Preview.Conflicts.Count - 1;

            if (m.Groups[1].Value != "r")
            {
                // Remember the choice, then go on to the next conflict (or back to the overview after the last one)
                pending.Decisions[pending.Preview.Conflicts[index].Guid] = m.Groups[1].Value == "n";
                SaveDecisions(chatId, pending.Decisions);

                if (index + 1 >= pending.Preview.Conflicts.Count)
                {
                    await ShowMergePreview(message, true, pending.Preview, pending.Decisions);
                    return;
                }
                index++;
            }

            await ShowMergeConflict(message, pending.Preview, pending.Decisions, index);
        }

        // Localized text for the health check, resolved with the chat language (safe across awaits)
        private static string HealthText(string key, long chatId)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(ChatConfig.Load(chatId).Language);
            return (Strings.ResourceManager.GetString(key, culture) ?? key).Replace("\\n", "\n");
        }

        private static string BuildHealthReportText(HealthReport report, long chatId)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format(HealthText("health_summary", chatId), report.Notes, report.Highlights, report.Bookmarks, report.Tags));
            sb.AppendLine();

            if (report.Issues.Count == 0)
            {
                sb.Append(HealthText("health_ok", chatId));
                return sb.ToString();
            }

            sb.AppendLine(HealthText("health_found", chatId));
            foreach (HealthIssue issue in report.Issues.OrderByDescending(i => i.Severity))
            {
                string icon = issue.Severity == HealthSeverity.Critical ? "🔴" : issue.Severity == HealthSeverity.Warning ? "🟡" : "⚪";
                string format = HealthText("health_issue_" + issue.Kind.ToString().ToLowerInvariant(), chatId);
                sb.AppendLine(icon + " " + string.Format(format, issue.Count));
            }
            sb.AppendLine();
            sb.AppendLine(HealthText("health_legend", chatId));
            if (report.HasFixable)
                sb.Append(HealthText("health_fix_hint", chatId));
            else
                sb.Append(HealthText("health_ok", chatId));
            return sb.ToString();
        }

        // Telegram HTML needs only these three characters escaped
        private static string Esc(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string StatsBlock(IEnumerable<NamedCount> items)
        {
            return "<pre>" + Esc(TextBars.Table(items.Select(i => new KeyValuePair<string, int>(i.Name, i.Count)))) + "</pre>";
        }

        private static string BuildStudyStatsText(StudyStatsResult s, long chatId)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(ChatConfig.Load(chatId).Language);
            string T(string key) => HealthText(key, chatId);
            string Date(DateTime? d) => d == null ? "-" : d.Value.ToString("d MMM yyyy", culture); // unambiguous: 8 May 2017
            string N(int n) => n.ToString("N0", culture);

            if (s.Notes == 0 && s.Highlights == 0 && s.Bookmarks == 0)
                return Esc(T("stats_empty"));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>" + Esc(T("stats_title")) + "</b>");
            sb.AppendLine();
            sb.AppendLine(Esc(string.Format(T("stats_totals"), N(s.Notes), N(s.NotesWords), N(s.Highlights), N(s.Bookmarks), N(s.Tags))));
            if (s.AverageWordsPerNote > 0)
                sb.AppendLine(Esc(string.Format(T("stats_words"), N(s.AverageWordsPerNote), N(s.LongestNoteWords))));

            if (s.ActiveDays > 0)
            {
                string busiest = s.BusiestWeekday == null ? "-" : culture.DateTimeFormat.GetDayName(s.BusiestWeekday.Value);
                sb.AppendLine();
                sb.AppendLine(Esc(string.Format(T("stats_activity"), Date(s.FirstActivity), Date(s.LastActivity), N(s.ActiveDays),
                    s.LongestStreak, s.CurrentStreak, s.NotesLast30Days, busiest)));
            }

            if (s.NotesPerMonth.Any(m => m.Value > 0))
            {
                sb.AppendLine();
                sb.AppendLine("<b>" + Esc(T("stats_monthly")) + "</b>");
                sb.AppendLine("<pre>" + Esc(TextBars.Table(s.NotesPerMonth.Select(m => new KeyValuePair<string, int>(m.Key.ToString("MMM yy", culture), m.Value)))) + "</pre>");
            }

            sb.AppendLine();
            sb.AppendLine(Esc(string.Format(T("stats_bible"), s.BibleBooksTouched, N(s.BibleChaptersTouched))));
            if (s.TopBibleBooks.Count > 0)
            {
                sb.AppendLine(Esc(T("stats_top_books")));
                sb.AppendLine(StatsBlock(s.TopBibleBooks));
            }

            if (s.TopHighlightedChapters.Count > 0)
            {
                sb.AppendLine("<b>" + Esc(T("stats_top_chapters")) + "</b>");
                sb.AppendLine(StatsBlock(s.TopHighlightedChapters));
            }

            if (s.TopTags.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>" + Esc(T("stats_top_tags")) + "</b>");
                sb.AppendLine(StatsBlock(s.TopTags));
            }

            if (s.TopPublications.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>" + Esc(T("stats_top_pubs")) + "</b>");
                sb.AppendLine(StatsBlock(s.TopPublications));
            }

            if (s.Highlights > 0)
            {
                string[] colorIcons = { "", "🟨", "🟩", "🟦", "🌸", "🟧", "🟪" };
                StringBuilder colors = new StringBuilder();
                for (int i = 1; i <= 6; i++)
                    if (s.HighlightColors[i] > 0)
                        colors.Append($"{colorIcons[i]} {N(s.HighlightColors[i])}  ");
                if (colors.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("<b>" + Esc(T("stats_colors")) + "</b>");
                    sb.AppendLine(colors.ToString().TrimEnd());
                }
            }

            if (s.Milestones.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>" + Esc(T("stats_milestones")) + "</b>");
                foreach (string m in s.Milestones)
                {
                    string[] parts = m.Split(':');
                    sb.AppendLine("🏅 " + Esc(string.Format(T("stats_ms_" + parts[0]), N(int.Parse(parts[1])))));
                }
            }

            sb.AppendLine();
            sb.Append("<i>" + Esc(T("stats_note")) + "</i>");
            return sb.ToString();
        }

        private static String GetFileInfoString(BackupFile JWLibraryFile, long chatId)
        {
            return string.Format(Strings.file_info_details, FileHandling.GetReadableFilesize(FileType.Main, chatId), JWLibraryFile.Database.Notes.Count, JWLibraryFile.Database.Bookmarks.Count, JWLibraryFile.Database.UserMarks.Count, JWLibraryFile.Database.Tags.Count, JWLibraryFile.Database.Tags.Count(t => t.Type == 2)).Replace("\\n", "\n");
        }

        private static async void InsertUpdateMenu(String title, Message message, bool fromCallback, InlineKeyboardMarkup keyboardMarkup)
        {
            // If the command came from a callback, it means that the user press the "back" button. So edit the message
            if (fromCallback)
            {
                await Worker.botClient.EditMessageTextAsync(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: title
                );
                if(keyboardMarkup != null)
                    await Worker.botClient.EditMessageReplyMarkupAsync(
                            chatId: message.Chat.Id,
                            messageId: message.MessageId,
                            replyMarkup: keyboardMarkup
                            );
            }
            else
            {
                // Otherwise, send a new message
                await Worker.botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: title,
                    replyMarkup: keyboardMarkup
                    );
            }
        }

    }
}
