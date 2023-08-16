using System;
using System.Collections.Generic;
using System.IO;

namespace JWLMergeBot
{
    static class FileHandling
    {
        #region Constants
        public static List<string> JWLibraryExtensions = new List<string> { ".jwlibrary", ".jwlibrary.bin" };
        private static int MaxFilesizeByte = 20 * 1024 * 1024;
        private static long TempFileBusyTimeout = 60;
        public enum FileType { Main, Temp, Merged, Log, Chat };
        #endregion

        #region Directories
        public static string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"JWLMergeBot");
        private static string GetApplicationDirectory()
        {
            Directory.CreateDirectory(AppDataPath);
            return AppDataPath;
        }
        private static string GetTempDirectory()
        {
            return Directory.CreateDirectory(Path.Combine(GetApplicationDirectory(), "Temp")).FullName;
        }
        private static string GetTempChatDirectory(long chatId)
        {
            return Directory.CreateDirectory(Path.Combine(GetApplicationDirectory(), "Temp", $"{chatId}")).FullName;
        }
        private static string GetLogDirectory()
        {
            return Directory.CreateDirectory(Path.Combine(GetApplicationDirectory(), "Log")).FullName;
        }
        #endregion

        #region Files path
        public static String GetLogFilePath(String LogName)
        {
            return Path.Combine(GetLogDirectory(), LogName);
        }
        public static String GetConfigFilePath()
        {
            return "config.json";
        }
        public static String GetChatConfigFilePath(long ChatId)
        {
            return Path.Combine(GetApplicationDirectory(), $"{ChatId}.json");
        }
        #endregion

        #region Validation
        public static bool IsValidFileSize(long FileSize)
        {
            return FileSize < MaxFilesizeByte;
        }

        public static bool IsValidFileExtension(string FileName)
        {
            foreach (string Extension in JWLibraryExtensions)
                if (FileName.EndsWith(Extension))
                    return true;
            return false;
        }
        #endregion

        public static String GetFilePath(FileType fileType, long chatId, Telegram.Bot.Types.Document document = null)
        {
            switch (fileType)
            {
                case FileType.Main:
                    return Path.Combine(GetApplicationDirectory(), $"{chatId}.jwlibrary");
                case FileType.Temp:
                    return Path.Combine(GetTempDirectory(), $"{chatId}_temp.jwlibrary");
                case FileType.Merged:
                    return Path.Combine(GetTempDirectory(), $"{chatId}_merged.jwlibrary");
                case FileType.Chat:
                    return Path.Combine(GetTempChatDirectory(chatId), document.FileName);
                default:
                    return null;
            }
        }

        public static void ChangeFileType(FileType fileTypeFrom, FileType fileTypeTo, long chatId)
        {
            File.Move(GetFilePath(fileTypeFrom, chatId), GetFilePath(fileTypeTo,chatId), overwrite: true);
        }

        public static void DeleteFile(FileType fileType, long chatId)
        {
            File.Delete(GetFilePath(fileType, chatId));
        }

        public static bool FileExists(FileType fileType, long chatId)
        {
            return File.Exists(GetFilePath(fileType, chatId));
        }

        public static bool IsTempFileBusy(long chatId)
        {
            if (FileExists(FileType.Temp,chatId))
            {
                if (File.GetLastWriteTime(GetFilePath(FileType.Temp,chatId)).AddSeconds(TempFileBusyTimeout).CompareTo(DateTime.Now) > 0)
                    return true;
                else
                {
                    // Delete the stucked temp file
                    DeleteFile(FileType.Temp, chatId);
                    return false;
                }
            }
            else
                return false;
        }

        public static string GetReadableFilesize(FileType fileType, long chatId)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = new FileInfo(GetFilePath(FileType.Main,chatId)).Length;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return String.Format("{0:0.##} {1}", len, sizes[order]);
        }

        public static string[] GetMainFiles()
        {
            return Directory.GetFiles(GetApplicationDirectory(), "*" + JWLibraryExtensions[0], SearchOption.TopDirectoryOnly);
        }

        public static string[] GetConfigFiles()
        {
            return Directory.GetFiles(GetApplicationDirectory(), "*.json", SearchOption.TopDirectoryOnly);
        }
    }
}
