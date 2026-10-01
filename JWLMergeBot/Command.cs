using System;
using System.Collections.Generic;
using System.Text;

namespace JWLMergeBot
{
    class Command
    {
        #region Public
        public const string BotInfo = "/botinfo";
        public const string Delete = "/delete";
        public const string FileInfo = "/fileinfo";
        public const string SetLang = "/setlang";
        public const string SetLangEn = "/setlang_en";
        public const string SetLangIt = "/setlang_it";
        public const string SetLangDe = "/setlang_de";
        public const string Settings = "/settings";
        public const string Start = "/start";
        public const string AutodeleteOn = "/autodelete_on";
        public const string AutodeleteOff = "/autodelete_off";
        public const string EditFile = "/editfile";
        public const string DeleteFavorites = "/deletefavorites";
        public const string DeleteFavoritesConfirmed = "/deletefavorites_confirm";
        public const string Health = "/health";
        public const string HealthFix = "/health_fix";
        public const string StudyStats = "/studystats";
        public const string PreviewOn = "/preview_on";
        public const string PreviewOff = "/preview_off";
        public const string MergeGo = "/merge_go";
        public const string MergeCancel = "/merge_cancel";
        #endregion

        #region Admins only
        public const string Changelog = "/changelog";
        public const string Stat = "/stat";
        public const string Stats = "/stats";
        public const string SendMessage = "/sendmessage";
        #endregion

        #region Regex of commands
        public const string SendMessageRegex = "^\\/sendmessage ({(\n|.)+})";
        #endregion
    }
}
