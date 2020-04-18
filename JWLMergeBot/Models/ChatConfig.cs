using Newtonsoft.Json;
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
		public string Language { get; set; }
		public bool AutoDeleteFile { get; set; }

		public void ApplyLanguage()
		{
			Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(Language!=null?Language:"en");
		}

		public void Save(long ChatId)
		{
			var json = JsonConvert.SerializeObject(this, Formatting.Indented);
			File.WriteAllText(FileHandling.GetChatConfigFilePath(ChatId), json);
		}

		public static ChatConfig Load(long ChatId)
		{
			try
			{
				var json = File.ReadAllText(FileHandling.GetChatConfigFilePath(ChatId));
				return JsonConvert.DeserializeObject<ChatConfig>(json);
			}catch(Exception e)
			{
				e.ToString();

				// Default settings
				ChatConfig config = new ChatConfig();
				config.Language = "en";
				config.AutoDeleteFile = false;

				return config;
			}
		}
	}
}
