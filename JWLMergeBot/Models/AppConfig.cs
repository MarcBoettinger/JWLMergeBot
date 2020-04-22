using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace JWLMergeBot
{
	public class AppConfig
	{
		public string BotToken { get; set; }
		public List<string> Admins { get; set; }

		public void Save()
		{
			var json = JsonConvert.SerializeObject(this, Formatting.Indented);
			File.WriteAllText(FileHandling.GetConfigFilePath(), json);
		}
		public static AppConfig Load() 
		{
			var json = File.ReadAllText(FileHandling.GetConfigFilePath()); 
			return JsonConvert.DeserializeObject<AppConfig>(json);
		}

		public bool IsAdmin(string adminUsername)
		{
			return Admins != null && Admins.Contains(adminUsername);// && Array.Exists(Admins,username => username == adminUsername);
		}
	}
}
