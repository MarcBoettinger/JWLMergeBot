using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace JWLMergeBot.Models
{
    class BotMessage
    {
        public string Recipients { get; set; }  // Recipients. One of the following: WithStoredFile, WithSettingsInitialized or a number corresponding to a ChatId
        public Dictionary<string, string> Text { get; set; } // A dictionary with the language name as the key and the text as the value

        [JsonIgnore]
        public static string RecipientsWithStoredFile = "WithStoredFile", RecipientsWithSettingsInitialized = "WithSettingsInitialized";

        public bool IsValid()
        {
            bool ValidRecipients = int.TryParse(Recipients, out int number) || Recipients.Equals(RecipientsWithStoredFile) || Recipients.Equals(RecipientsWithSettingsInitialized);
            bool ValidText = Text.ContainsKey("it") && Text.ContainsKey("en");
            return ValidRecipients && ValidText;
        }

        public bool IsSingleRecipient()
        {
            return int.TryParse(Recipients, out int number);
        }
    }
}
