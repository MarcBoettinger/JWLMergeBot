using JWLMerge.BackupFileServices;
using JWLMerge.BackupFileServices.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace JWLMergeBot
{
    class Tests
    {

        public static void BaseMerge()
        {
            IBackupFileService backupFileService = new BackupFileService();
            var list = new List<string>() {
                "C:\\Users\\Hyperion\\Desktop\\nwsty_10_bookmarks.jwlibrary",
                "C:\\Users\\Hyperion\\Desktop\\nwsty_5_bookmarks.jwlibrary"
            };
            BackupFile backup = backupFileService.Merge(list);
            backupFileService.WriteNewDatabase(backup, $"C:\\Users\\Hyperion\\Desktop\\{DateTime.Now.ToString("yyyy-MM-dd HH mm ss")}_Merged.jwlibrary", list[0], list);
        }
    }
}
