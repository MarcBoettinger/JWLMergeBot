using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JWLMerge.BackupFileServices;
using JWLMerge.BackupFileServices.Helpers;
using JWLMerge.BackupFileServices.Models;
using JWLMergeBot.Models;
using JWLMergeBot.Properties;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using static JWLMergeBot.FileHandling;

namespace JWLMergeBot
{
    // Search, tag manager and extract. Everything here works on the file the user stored.
    partial class Logic
    {
        private const int MaxSearchHits = 8;
        private const int MaxTagLines = 30;
        private const int MaxTagButtons = 12;

        private static string Tx(string key, long chatId)
        {
            return HealthText(key, chatId);
        }

        /// <summary>Handles /search, /tags, /renametag, /mergetags and /extract with their buttons. Returns true if the command was one of these.</summary>
        private static async Task<bool> OnToolsCommand(Message message, string command, bool fromCallback)
        {
            long chatId = message.Chat.Id;
            try
            {
                Match m = Regex.Match(command, @"^/search(?:@\w+)?(?:\s+(.+))?$", RegexOptions.Singleline);
                if (m.Success)
                {
                    await OnSearch(message, m.Groups[1].Success ? m.Groups[1].Value.Trim() : "");
                    return true;
                }

                m = Regex.Match(command, @"^/renametag(?:@\w+)?(?:\s+(.+))?$", RegexOptions.Singleline);
                if (m.Success)
                {
                    await OnRenameTag(message, m.Groups[1].Success ? m.Groups[1].Value : "");
                    return true;
                }

                m = Regex.Match(command, @"^/mergetags(?:@\w+)?(?:\s+(.+))?$", RegexOptions.Singleline);
                if (m.Success)
                {
                    await OnMergeTagsCommand(message, m.Groups[1].Success ? m.Groups[1].Value : "");
                    return true;
                }

                m = Regex.Match(command, @"^/ext_t_(\d+)$");
                if (m.Success)
                {
                    await OnExtractByTag(message, int.Parse(m.Groups[1].Value));
                    return true;
                }

                m = Regex.Match(command, @"^/ext_p_(sy0|sy1|m6|m12)$");
                if (m.Success)
                {
                    await OnExtractByPeriod(message, m.Groups[1].Value);
                    return true;
                }

                switch (command)
                {
                    case Command.Tags:
                        await ShowTags(message, fromCallback);
                        return true;
                    case Command.TagsUnused:
                        await AskDeleteUnusedTags(message, fromCallback);
                        return true;
                    case Command.TagsUnusedConfirm:
                        await DeleteUnusedTags(message);
                        return true;
                    case Command.Extract:
                        await ShowExtractMenu(message, fromCallback);
                        return true;
                    case Command.ExtractTags:
                        await ShowExtractTags(message, fromCallback);
                        return true;
                }
            }
            catch (Exception exception)
            {
                // Never let a failing tool take the bot down (also covers Telegram refusing to edit a message into the same text)
                Worker.Logger.LogError(message: exception.Message, exception: exception);
            }

            return false;
        }

        /// <summary>Loads the stored file, or tells the user that there is none.</summary>
        private static async Task<BackupFile> LoadStoredOrExplain(long chatId)
        {
            if (!FileHandling.FileExists(FileType.Main, chatId))
            {
                await Worker.botClient.SendTextMessageAsync(chatId, Tx("tools_no_file", chatId));
                return null;
            }

            try
            {
                return new BackupFileService().Load(FileHandling.GetFilePath(FileType.Main, chatId));
            }
            catch (Exception exception)
            {
                Worker.Logger.LogError(message: exception.Message, exception: exception);
                await Worker.botClient.SendTextMessageAsync(chatId, string.Format(Strings.file_error, exception.Message));
                return null;
            }
        }

        private static async Task ShowText(Message message, bool edit, string text, InlineKeyboardMarkup markup, bool html)
        {
            if (edit && html)
                await Worker.botClient.EditMessageTextAsync(chatId: message.Chat.Id, messageId: message.MessageId, text: text, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: markup);
            else if (edit)
                await Worker.botClient.EditMessageTextAsync(chatId: message.Chat.Id, messageId: message.MessageId, text: text, replyMarkup: markup);
            else if (html)
                await Worker.botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: text, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: markup);
            else
                await Worker.botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: text, replyMarkup: markup);
        }

        // ------------------------------------------------------------------ Search

        private static async Task OnSearch(Message message, string query)
        {
            long chatId = message.Chat.Id;
            if (query.Length == 0)
            {
                await Worker.botClient.SendTextMessageAsync(chatId, Tx("search_usage", chatId));
                return;
            }

            if (query.Length > 100)
                query = query.Substring(0, 100);

            BackupFile file = await LoadStoredOrExplain(chatId);
            if (file == null)
                return;

            CultureInfo culture = CultureInfo.GetCultureInfo(ChatConfig.Load(chatId).Language);
            NoteSearchResult result = new NoteSearch().Search(file, query, MaxSearchHits);

            if (result.Total == 0)
            {
                await Worker.botClient.SendTextMessageAsync(chatId, string.Format(Tx("search_none", chatId), query));
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format(Tx("search_header", chatId), result.Total, query));
            foreach (NoteHit hit in result.Hits)
            {
                sb.AppendLine();
                string place = string.IsNullOrEmpty(hit.Where) ? "📝" : "📖 " + hit.Where;
                sb.AppendLine(place + " · " + hit.Modified.ToString("d MMM yyyy", culture));
                if (hit.Title.Length > 0)
                    sb.AppendLine(hit.Title.Length > 100 ? hit.Title.Substring(0, 100) + "…" : hit.Title);
                if (hit.Snippet.Length > 0)
                    sb.AppendLine(hit.Snippet);
                if (hit.Tags.Count > 0)
                    sb.AppendLine("🏷 " + string.Join(", ", hit.Tags));
            }
            if (result.Total > result.Hits.Count)
            {
                sb.AppendLine();
                sb.Append(string.Format(Tx("search_more", chatId), result.Total - result.Hits.Count));
            }

            string text = sb.ToString();
            if (text.Length > 4000)
                text = text.Substring(0, 3990).TrimEnd() + "…";

            // plain text on purpose: notes may contain any character
            await Worker.botClient.SendTextMessageAsync(chatId, text);
        }

        // -------------------------------------------------------------------- Tags

        private static async Task ShowTags(Message message, bool fromCallback)
        {
            long chatId = message.Chat.Id;
            BackupFile file = await LoadStoredOrExplain(chatId);
            if (file == null)
                return;

            TagManager manager = new TagManager();
            List<TagInfo> tags = manager.List(file.Database);
            if (tags.Count == 0)
            {
                await ShowText(message, fromCallback, Esc(Tx("tags_none", chatId)), null, true);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>" + Esc(Tx("tags_title", chatId)) + "</b>");
            sb.AppendLine("<pre>" + Esc(TextBars.Table(tags.Take(MaxTagLines).Select(t => new KeyValuePair<string, int>(t.Name, t.Items)), 18)) + "</pre>");
            if (tags.Count > MaxTagLines)
                sb.AppendLine(Esc(string.Format(Tx("tags_more", chatId), tags.Count - MaxTagLines)));
            sb.AppendLine();
            sb.Append(Esc(Tx("tags_help", chatId)));

            int unused = manager.CountUnused(file.Database);
            InlineKeyboardMarkup markup = unused == 0 ? null : new InlineKeyboardMarkup(new[] {
                new[] { InlineKeyboardButton.WithCallbackData(string.Format(Tx("tags_btn_unused", chatId), unused), Command.TagsUnused) }
            });

            await ShowText(message, fromCallback, sb.ToString(), markup, true);
        }

        private static async Task AskDeleteUnusedTags(Message message, bool fromCallback)
        {
            long chatId = message.Chat.Id;
            BackupFile file = await LoadStoredOrExplain(chatId);
            if (file == null)
                return;

            int unused = new TagManager().CountUnused(file.Database);
            if (unused == 0)
            {
                await ShowText(message, fromCallback, Tx("tags_unused_none", chatId), null, false);
                return;
            }

            await ShowText(message, fromCallback, string.Format(Tx("tags_unused_ask", chatId), unused), new InlineKeyboardMarkup(new[] {
                new[] {
                    InlineKeyboardButton.WithCallbackData(Tx("tags_btn_yes", chatId), Command.TagsUnusedConfirm),
                    InlineKeyboardButton.WithCallbackData(Tx("merge_btn_cancel", chatId), Command.Tags)
                }
            }), false);
        }

        private static async Task DeleteUnusedTags(Message message)
        {
            long chatId = message.Chat.Id;
            BackupFile file = await LoadStoredOrExplain(chatId);
            if (file == null)
                return;

            // take the buttons away, so the action can't run twice
            await Worker.botClient.EditMessageReplyMarkupAsync(chatId, message.MessageId, new InlineKeyboardMarkup(new List<InlineKeyboardButton>()));

            int deleted = new TagManager().DeleteUnused(file.Database);
            if (deleted == 0)
            {
                await Worker.botClient.SendTextMessageAsync(chatId, Tx("tags_unused_none", chatId));
                return;
            }

            await SaveStoredAndSend(message, file, string.Format(Tx("tags_deleted", chatId), deleted));
        }

        /// <summary>Splits "first > second" (the first ">" separates them).</summary>
        private static bool TrySplitPair(string args, out string first, out string second)
        {
            first = second = null;
            int i = args.IndexOf('>');
            if (i <= 0)
                return false;
            first = args.Substring(0, i).Trim();
            second = args.Substring(i + 1).Trim();
            return first.Length > 0 && second.Length > 0;
        }

        private static string TagFailureText(TagResult result, string name, long chatId)
        {
            switch (result)
            {
                case TagResult.NotFound: return string.Format(Tx("tags_not_found", chatId), name);
                case TagResult.Ambiguous: return string.Format(Tx("tags_ambiguous", chatId), name);
                case TagResult.NameTaken: return string.Format(Tx("tags_name_taken", chatId), name);
                case TagResult.Same: return Tx("tags_same", chatId);
                default: return Tx("tags_invalid", chatId);
            }
        }

        private static async Task OnRenameTag(Message message, string args)
        {
            long chatId = message.Chat.Id;
            if (!TrySplitPair(args, out string oldName, out string newName))
            {
                await Worker.botClient.SendTextMessageAsync(chatId, Tx("tags_rename_usage", chatId));
                return;
            }

            BackupFile file = await LoadStoredOrExplain(chatId);
            if (file == null)
                return;

            TagResult result = new TagManager().Rename(file.Database, oldName, newName);
            if (result != TagResult.Ok)
            {
                // a rename that fails because of the new name should name the new name
                string name = result == TagResult.NameTaken || result == TagResult.Invalid ? newName : oldName;
                await Worker.botClient.SendTextMessageAsync(chatId, TagFailureText(result, name, chatId));
                return;
            }

            await SaveStoredAndSend(message, file, string.Format(Tx("tags_renamed", chatId), oldName, newName.Trim()));
        }

        private static async Task OnMergeTagsCommand(Message message, string args)
        {
            long chatId = message.Chat.Id;
            if (!TrySplitPair(args, out string source, out string target))
            {
                await Worker.botClient.SendTextMessageAsync(chatId, Tx("tags_merge_usage", chatId));
                return;
            }

            BackupFile file = await LoadStoredOrExplain(chatId);
            if (file == null)
                return;

            TagResult result = new TagManager().Merge(file.Database, source, target, out int moved);
            if (result != TagResult.Ok)
            {
                // tell which of the two names caused the problem
                TagManager probe = new TagManager();
                string name = probe.Find(file.Database, source, out _) != TagResult.Ok ? source : target;
                await Worker.botClient.SendTextMessageAsync(chatId, TagFailureText(result, name, chatId));
                return;
            }

            await SaveStoredAndSend(message, file, string.Format(Tx("tags_merged", chatId), source, target, moved));
        }

        /// <summary>Writes the edited file as the new stored file and sends it back, like the other edit tools do.</summary>
        private static async Task SaveStoredAndSend(Message message, BackupFile file, string caption)
        {
            long chatId = message.Chat.Id;

            if (FileHandling.IsTempFileBusy(chatId))
            {
                await Worker.botClient.SendTextMessageAsync(chatId, string.Format(Strings.busy, "").Replace("\\n", "\n"));
                return;
            }

            try
            {
                file.Manifest.UserDataBackup.LastModifiedDate = DateTime.Now.ToString(ManifestDateTimeFormat);
                file.Manifest.CreationDate = DateTime.Now.ToString(ManifestDateTimeFormat);

                // If anything is inconsistent this throws, and the stored file stays untouched
                new BackupFileService().WriteNewDatabase(file, FileHandling.GetFilePath(FileType.Temp, chatId), FileHandling.GetFilePath(FileType.Main, chatId), new List<string> { file.FilePath });
                FileHandling.ChangeFileType(FileType.Temp, FileType.Main, chatId);

                ChatConfig chatConfig = ChatConfig.Load(chatId);
                using (FileStream fs = System.IO.File.OpenRead(FileHandling.GetFilePath(FileType.Main, chatId)))
                {
                    InputOnlineFile inputOnlineFile = new InputOnlineFile(fs, string.Format(Strings.edited_filename, DateTime.Now.ToString("s")));
                    await Worker.botClient.SendDocumentAsync(
                        chatId: chatId,
                        document: inputOnlineFile,
                        caption: caption,
                        replyMarkup: chatConfig.AutoDeleteFile ? null : new InlineKeyboardMarkup(new[] {
                            InlineKeyboardButton.WithCallbackData(Strings.delete_file, Command.Delete)
                        }));
                }

                if (chatConfig.AutoDeleteFile)
                    OnCommand(message, Command.Delete, false);
            }
            catch (Exception exception)
            {
                Worker.Logger.LogError(message: exception.Message, exception: exception);
                await Worker.botClient.SendTextMessageAsync(chatId, string.Format(Tx("tools_edit_failed", chatId), exception.Message));
            }
            finally
            {
                FileHandling.DeleteFile(FileType.Temp, chatId);
            }
        }

        // ----------------------------------------------------------------- Extract

        private static async Task ShowExtractMenu(Message message, bool fromCallback)
        {
            long chatId = message.Chat.Id;
            if (!FileHandling.FileExists(FileType.Main, chatId))
            {
                await Worker.botClient.SendTextMessageAsync(chatId, Tx("tools_no_file", chatId));
                return;
            }

            InlineKeyboardMarkup markup = new InlineKeyboardMarkup(new[] {
                new[] { InlineKeyboardButton.WithCallbackData(Tx("extract_btn_tag", chatId), Command.ExtractTags) },
                new[] { InlineKeyboardButton.WithCallbackData(Tx("extract_btn_sy0", chatId), "/ext_p_sy0") },
                new[] { InlineKeyboardButton.WithCallbackData(Tx("extract_btn_sy1", chatId), "/ext_p_sy1") },
                new[] { InlineKeyboardButton.WithCallbackData(Tx("extract_btn_m6", chatId), "/ext_p_m6") },
                new[] { InlineKeyboardButton.WithCallbackData(Tx("extract_btn_m12", chatId), "/ext_p_m12") },
            });
            await ShowText(message, fromCallback, Tx("extract_menu", chatId), markup, false);
        }

        private static async Task ShowExtractTags(Message message, bool fromCallback)
        {
            long chatId = message.Chat.Id;
            BackupFile file = await LoadStoredOrExplain(chatId);
            if (file == null)
                return;

            List<TagInfo> tags = new TagManager().List(file.Database).Where(t => t.Items > 0).Take(MaxTagButtons).ToList();
            if (tags.Count == 0)
            {
                await ShowText(message, fromCallback, Tx("tags_none", chatId), null, false);
                return;
            }

            List<InlineKeyboardButton[]> rows = tags
                .Select(t => new[] { InlineKeyboardButton.WithCallbackData((t.Name.Length > 28 ? t.Name.Substring(0, 27) + "…" : t.Name) + " (" + t.Items + ")", "/ext_t_" + t.TagId) })
                .ToList();
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData(Strings.back, Command.Extract) });
            await ShowText(message, fromCallback, Tx("extract_pick_tag", chatId), new InlineKeyboardMarkup(rows), false);
        }

        private static async Task OnExtractByTag(Message message, int tagId)
        {
            long chatId = message.Chat.Id;
            BackupFile file = await LoadStoredOrExplain(chatId);
            if (file == null)
                return;

            ExtractResult result = new LibraryExtractor().KeepNotesWithTags(file, new[] { tagId });
            await SendExtract(message, file, result);
        }

        private static async Task OnExtractByPeriod(Message message, string code)
        {
            long chatId = message.Chat.Id;
            BackupFile file = await LoadStoredOrExplain(chatId);
            if (file == null)
                return;

            DateTime today = DateTime.Now.Date;
            DateTime rangeStart, rangeEnd;
            switch (code)
            {
                case "sy0":
                    (rangeStart, rangeEnd) = LibraryExtractor.ServiceYear(today, 0);
                    break;
                case "sy1":
                    (rangeStart, rangeEnd) = LibraryExtractor.ServiceYear(today, 1);
                    break;
                case "m6":
                    rangeStart = today.AddMonths(-6);
                    rangeEnd = today.AddDays(1);
                    break;
                default:
                    rangeStart = today.AddMonths(-12);
                    rangeEnd = today.AddDays(1);
                    break;
            }

            ExtractResult result = new LibraryExtractor().KeepNotesModifiedBetween(file, rangeStart, rangeEnd);
            await SendExtract(message, file, result);
        }

        /// <summary>Writes the cut-down backup to a temporary file and sends it. The stored file is never replaced.</summary>
        private static async Task SendExtract(Message message, BackupFile file, ExtractResult result)
        {
            long chatId = message.Chat.Id;

            if (result.Notes == 0)
            {
                await Worker.botClient.SendTextMessageAsync(chatId, Tx("extract_none", chatId));
                return;
            }

            if (FileHandling.IsTempFileBusy(chatId))
            {
                await Worker.botClient.SendTextMessageAsync(chatId, string.Format(Strings.busy, "").Replace("\\n", "\n"));
                return;
            }

            await Worker.botClient.SendTextMessageAsync(chatId, Tx("extract_working", chatId));
            await Worker.botClient.SendChatActionAsync(chatId, Telegram.Bot.Types.Enums.ChatAction.UploadDocument);

            try
            {
                file.Manifest.UserDataBackup.LastModifiedDate = DateTime.Now.ToString(ManifestDateTimeFormat);
                file.Manifest.CreationDate = DateTime.Now.ToString(ManifestDateTimeFormat);

                // No media: playlists are not part of an extract
                new BackupFileService().WriteNewDatabase(file, FileHandling.GetFilePath(FileType.Temp, chatId), FileHandling.GetFilePath(FileType.Main, chatId), new List<string>());

                using (FileStream fs = System.IO.File.OpenRead(FileHandling.GetFilePath(FileType.Temp, chatId)))
                {
                    InputOnlineFile inputOnlineFile = new InputOnlineFile(fs, string.Format(Tx("extract_filename", chatId), DateTime.Now.ToString("s")));
                    await Worker.botClient.SendDocumentAsync(
                        chatId: chatId,
                        document: inputOnlineFile,
                        caption: string.Format(Tx("extract_done", chatId), result.Notes, result.Highlights, result.Tags));
                }
            }
            catch (Exception exception)
            {
                Worker.Logger.LogError(message: exception.Message, exception: exception);
                await Worker.botClient.SendTextMessageAsync(chatId, string.Format(Tx("extract_failed", chatId), exception.Message));
            }
            finally
            {
                FileHandling.DeleteFile(FileType.Temp, chatId);
            }
        }
    }
}
