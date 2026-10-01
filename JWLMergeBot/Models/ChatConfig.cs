using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace JWLMergeBot
{
    class ChatConfig
	{
		private string _Language;
		public string Language {
			get { return _Language != null ? _Language : "en"; }
			set { _Language = value; } 
		}
		public bool AutoDeleteFile { get; set; }
		private bool? _PreviewBeforeMerge;
		// On by default, also for chats saved before this setting existed
		public bool PreviewBeforeMerge
		{
			get { return _PreviewBeforeMerge ?? true; }
			set { _PreviewBeforeMerge = value; }
		}
		private int _MergedFileCount;
        public int MergedFileCount
        {
            get { return _MergedFileCount; }
            set
            {
				_MergedFileCount = value;
				LastMerge = DateTime.Now;
            }
        }
        [JsonConverter(typeof(IsoDateTimeConverter))]
        public DateTime? LastMerge { get; set; }

        public void ApplyLanguage()
		{
			Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(Language);
		}

		public void Save(long ChatId)
		{
			var json = JsonConvert.SerializeObject(this, Formatting.Indented);
			File.WriteAllText(FileHandling.GetChatConfigFilePath(ChatId), json);
		}

		public static ChatConfig Load(long ChatId)
		{
			if (File.Exists(FileHandling.GetChatConfigFilePath(ChatId)))
			{ 
				try
				{
					var json = File.ReadAllText(FileHandling.GetChatConfigFilePath(ChatId));
					return JsonConvert.DeserializeObject<ChatConfig>(json);
				}catch (Exception)
                {
				}
			}

			// Default settings
			ChatConfig config = new ChatConfig();
			config.Language = "en";
			config.AutoDeleteFile = false;
			config.MergedFileCount = 0;
			config.LastMerge = null;

			return config;
		}
	}
}
