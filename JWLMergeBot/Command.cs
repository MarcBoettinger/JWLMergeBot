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
        public const string Settings = "/settings";
        public const string Start = "/start";
        public const string AutodeleteOn = "/autodelete_on";
        public const string AutodeleteOff = "/autodelete_off";
        #endregion

        #region Admins only
        public const string Changelog = "/changelog";
        public const string Stat = "/stat";
        public const string Stats = "/stats";
        #endregion
    }
}
